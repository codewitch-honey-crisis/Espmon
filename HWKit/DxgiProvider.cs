using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace HWKit
{
    public sealed class DxgiProvider : HardwareInfoProviderBase
    {
        // ETW provider GUIDs
        public static readonly Guid DXGI_provider = Guid.Parse("{CA11C036-0102-4A2D-A6AD-F03CFED5D3C9}");
        public static readonly Guid D3D9_provider = Guid.Parse("{783ACA0A-790E-4D7F-8451-AA850511C6B9}");
        public static readonly Guid DxgKrnl_provider = Guid.Parse("{802EC45A-1E99-4B83-9920-87C98277BA9D}");

        // DXGI / D3D9 present event IDs (from their ETW manifests) — app-level, carry the game's PID
        public const int EventID_DxgiPresentStart = 42;
        public const int EventID_DxgiPresentStop = 43;
        public const int EventID_D3D9PresentStart = 1;
        public const int EventID_D3D9PresentStop = 2;

        // DxgKrnl event IDs (discovered empirically on this box; stable across the Win10/11 builds we tested).
        // These are the ONLY DxgKrnl events we enable — everything else is filtered out kernel-side.
        public const int EventID_VSyncDPC = 17;                 // per-source vsync interrupt DPC → refresh cadence
        public const int EventID_VSyncDPCMultiPlane = 273;      // MPO variant, identical cadence (fallback)
        public const int EventID_PresentMultiPlaneOverlay = 251; // carries game PID + VidPnSourceId in fullscreen (iflip)
        private const ulong DxgKrnlVsyncPresentKeywords = 0x4000000008000001UL;

        TraceEventSession? _etwSession;
        Thread? _processThread;
        volatile bool _processing;

        // Frame tracking keyed by (processId, swapChainAddress).
        // This lets us distinguish the game's swap chain from overlay swap chains.
        readonly ConcurrentDictionary<(int pid, ulong swapChain), SwapChainTracker> _trackers = new();

        // The "active" tracker we report framerate from, plus its owning PID.
        // Selection is now purely "busiest presenter across all processes" — no GetForegroundWindow,
        // so this works identically in a user app and in a Session-0 service.
        volatile SwapChainTracker? _activeTracker;
        volatile int _activePid;

        // Refresh-rate side (independent of framerate selection so a DxgKrnl attribution miss
        // only degrades refresh, never FPS):
        //   game PID -> VidPnSourceId  (from PresentMultiPlaneOverlay)
        //   VidPnSourceId -> measured refresh (from VSyncDPC inter-arrival intervals)
        readonly ConcurrentDictionary<int, int> _pidToSource = new();
        readonly ConcurrentDictionary<int, VSyncSourceTracker> _vsyncTrackers = new();
        volatile float _lastRefreshHz;   // last good reading; returned when no live mapping (e.g. composited windowed)
        volatile int _activeSource = -1;                              // busiest-flipped source, for the fallback
        readonly ConcurrentDictionary<int, int> _flipRecent = new();  // VidPnSourceId -> recent PMO count
        // How often to re-evaluate which swap chain is the "primary" one.
        Timer? _primarySelectionTimer;

        /// <summary>
        /// Tracks frame timing for a single (process, swap chain) pair.
        /// Uses ETW QPC timestamps directly — no Stopwatch jitter.
        /// </summary>
        private sealed class SwapChainTracker
        {
            const int MaxFrameTimes = 100;

            // QPC timestamp of the last PresentStart for this swap chain
            long _lastPresentQpc;

            // QPC frequency, cached from ETW session
            readonly long _qpcFrequency;

            // Recent frame times in milliseconds (present-to-present intervals)
            readonly ConcurrentQueue<double> _frameTimes = new();
            int _frameTimeCount; // approximate count to avoid expensive ConcurrentQueue.Count

            // Tracks how many presents we've seen recently, for picking the "primary" swap chain
            int _recentPresentCount;

            public SwapChainTracker(long qpcFrequency)
            {
                _qpcFrequency = qpcFrequency;
            }

            /// <summary>
            /// Record a PresentStart event using the ETW event's QPC timestamp.
            /// Returns true if a valid frame time was recorded.
            /// </summary>
            public bool RecordPresent(long qpcTimestamp)
            {
                Interlocked.Increment(ref _recentPresentCount);

                long previous = Interlocked.Exchange(ref _lastPresentQpc, qpcTimestamp);
                if (previous == 0)
                {
                    // First present — no delta yet
                    return false;
                }

                long delta = qpcTimestamp - previous;
                if (delta <= 0)
                {
                    return false;
                }

                double frameTimeMs = (double)delta / _qpcFrequency * 1000.0;

                // Discard implausible frame times.
                // < 0.5ms would be 2000+ FPS — almost certainly a duplicate/batched present.
                // > 500ms means the game was probably paused, alt-tabbed, or loading.
                if (frameTimeMs < 0.5 || frameTimeMs > 500.0)
                {
                    return false;
                }

                _frameTimes.Enqueue(frameTimeMs);
                int count = Interlocked.Increment(ref _frameTimeCount);

                // Trim to keep bounded
                while (count > MaxFrameTimes)
                {
                    if (_frameTimes.TryDequeue(out _))
                    {
                        count = Interlocked.Decrement(ref _frameTimeCount);
                    }
                    else
                    {
                        break;
                    }
                }

                return true;
            }

            /// <summary>
            /// Returns and resets the recent present count. Used for primary swap chain selection.
            /// </summary>
            public int ConsumeRecentPresentCount()
            {
                return Interlocked.Exchange(ref _recentPresentCount, 0);
            }

            /// <summary>
            /// Snapshot the current frame times for metric computation.
            /// We copy to an array to avoid issues with concurrent modification.
            /// </summary>
            private double[] SnapshotFrameTimes()
            {
                return _frameTimes.ToArray();
            }

            public float AverageFps
            {
                get
                {
                    var times = SnapshotFrameTimes();
                    if (times.Length == 0) return 0f;

                    double avgMs = 0;
                    for (int i = 0; i < times.Length; i++)
                        avgMs += times[i];
                    avgMs /= times.Length;

                    return avgMs > 0 ? (float)(1000.0 / avgMs) : 0f;
                }
            }

            public float OnePercentLowFps
            {
                get
                {
                    var times = SnapshotFrameTimes();
                    if (times.Length == 0) return 0f;

                    // 1% lows = average FPS of the slowest 1% of frames
                    // Sort ascending, take the longest frame times (highest ms values)
                    Array.Sort(times);
                    int count = Math.Max(1, times.Length / 100);

                    double sum = 0;
                    for (int i = times.Length - count; i < times.Length; i++)
                        sum += times[i];
                    double avgSlowest = sum / count;

                    return avgSlowest > 0 ? (float)(1000.0 / avgSlowest) : 0f;
                }
            }

            public float MaxFrameTimeMs
            {
                get
                {
                    var times = SnapshotFrameTimes();
                    if (times.Length == 0) return 0f;

                    double max = 0;
                    for (int i = 0; i < times.Length; i++)
                        if (times[i] > max) max = times[i];
                    return (float)max;
                }
            }

            public float MinFrameTimeMs
            {
                get
                {
                    var times = SnapshotFrameTimes();
                    if (times.Length == 0) return 0f;

                    double min = double.MaxValue;
                    for (int i = 0; i < times.Length; i++)
                        if (times[i] < min) min = times[i];
                    return (float)min;
                }
            }
        }

        /// <summary>
        /// Derives a display's refresh rate from the inter-arrival interval of its VSyncDPC events.
        /// This is the panel's scanout cadence, independent of the app's frame rate, so it reads the
        /// true Hz whether the game is hitting refresh, capped below it, or stuttering.
        /// Median (not mean) over a rolling window so an occasional missed/coalesced DPC can't skew it.
        /// </summary>
        private sealed class VSyncSourceTracker
        {
            const int MaxIntervals = 128;
            const int MinIntervalsForReading = 8;

            readonly object _lock = new();
            readonly Queue<double> _intervalsMs = new();
            double _lastTimestampMs = double.NaN;

            /// <summary>Record a vsync event timestamp (ETW session-relative milliseconds).</summary>
            public void Record(double timestampMs)
            {
                lock (_lock)
                {
                    if (!double.IsNaN(_lastTimestampMs))
                    {
                        double d = timestampMs - _lastTimestampMs;
                        // Plausible refresh interval: faster than ~5 Hz, and positive.
                        // (240 Hz ≈ 4.17 ms, 60 Hz ≈ 16.67 ms; a paused source may drop out entirely,
                        //  which just means no new intervals rather than a bad one.)
                        if (d > 0 && d < 200.0)
                        {
                            _intervalsMs.Enqueue(d);
                            while (_intervalsMs.Count > MaxIntervals)
                                _intervalsMs.Dequeue();
                        }
                    }
                    _lastTimestampMs = timestampMs;
                }
            }

            /// <summary>Median-derived refresh in Hz, or 0 if we don't yet have enough samples.</summary>
            public float RefreshHz
            {
                get
                {
                    double[] arr;
                    lock (_lock)
                    {
                        if (_intervalsMs.Count < MinIntervalsForReading) return 0f;
                        arr = _intervalsMs.ToArray();
                    }
                    Array.Sort(arr);
                    double median = arr[arr.Length / 2];
                    return median > 0 ? (float)(1000.0 / median) : 0f;
                }
            }
        }

        public DxgiProvider()
        {
        }

        protected override HardwareInfoProviderStatus GetState()
        {
            return _processing ? HardwareInfoProviderStatus.Started : HardwareInfoProviderStatus.Stopped;
        }
        
        protected override void OnStart()
        {
            if (_processing) return;
            _processing = true;

            // Re-evaluate the primary swap chain every 2 seconds.
            _primarySelectionTimer = new Timer(SelectPrimarySwapChain, null, 2000, 2000);

            // Create and configure the ETW session.
            _etwSession = new TraceEventSession(GetIdentifier());

            // App-level present providers first, unfiltered. These carry the game's PID and decode
            // fine through the dynamic path (Source.AllEvents). Enabling these BEFORE the event-ID-filtered
            // DxgKrnl provider is deliberate: perfview issue #864 shows a session whose FIRST EnableProvider
            // call uses EventIDsToEnable can receive zero events. Keeping DxgKrnl second avoids that.
            _etwSession.EnableProvider(DXGI_provider);
            _etwSession.EnableProvider(D3D9_provider);

            // DxgKrnl, narrowed kernel-side to just the three events we need. Without this filter the
            // provider is a firehose (scheduler/DMA/paging churn ≈ 98% of the stream); the event-ID filter
            // drops all of that before it reaches our buffers.
            var dxgKrnlOptions = new TraceEventProviderOptions
            {
                EventIDsToEnable = new List<int>
                {
                    EventID_VSyncDPC,
                    EventID_VSyncDPCMultiPlane,
                    EventID_PresentMultiPlaneOverlay,
                }
            };
            _etwSession.EnableProvider(DxgKrnl_provider, TraceEventLevel.Verbose,long.MaxValue, dxgKrnlOptions);

            // DXGI / D3D9 present events: handled via the dynamic AllEvents path (their payloads resolve there).
            _etwSession.Source.AllEvents += OnEtwEvent;

            // DxgKrnl events: their payloads DO NOT resolve through AllEvents — they only decode via the
            // registered (TDH) parser, which reads the OS-registered manifest. So VSyncDPC / PMO get their
            // own handler off RegisteredTraceEventParser. Each handler ignores the other's providers, so
            // there's no double-counting.
            var registered = new RegisteredTraceEventParser(_etwSession.Source);
            registered.All += OnDxgKrnlEvent;

            // Publish metrics — they read from the active tracker / refresh mapping.
            Publish("/framerate", "FPS", () => _activeTracker?.AverageFps ?? 0f);
            Publish("/refreshrate", "Hz", GetActiveRefreshRate);
            Publish("/1pctlows", "FPS", () => _activeTracker?.OnePercentLowFps ?? 0f);
            Publish("/maxrender", "MS", () => _activeTracker?.MaxFrameTimeMs ?? 0f);
            Publish("/minrender", "MS", () => _activeTracker?.MinFrameTimeMs ?? 0f);

            // Process ETW events on a background thread.
            _processThread = new Thread(() =>
            {
                _etwSession?.Source.Process();
                _processing = false;
            })
            {
                IsBackground = true,
                Name = "DxgiProvider ETW"
            };
            _processThread.Start();
        }

        /// <summary>
        /// Handles app-level DXGI / D3D9 PresentStart events for frame timing.
        /// No longer filtered by foreground PID — every process's presents are tracked, and
        /// SelectPrimarySwapChain picks the busiest. That's what makes this work in a service.
        /// </summary>
        private void OnEtwEvent(TraceEvent data)
        {
            int eventId = (int)data.ID;
            var provider = data.ProviderGuid;

            bool isDxgiPresent = eventId == EventID_DxgiPresentStart && provider == DXGI_provider;
            bool isD3d9Present = eventId == EventID_D3D9PresentStart && provider == D3D9_provider;

            if (!isDxgiPresent && !isD3d9Present)
                return;

            int pid = data.ProcessID;

            // Extract swap chain address from the event payload so we can separate the game's
            // swap chain from overlay swap chains (Steam, Discord, etc.).
            ulong swapChainAddr = 0;
            try
            {
                if (data.PayloadNames.Length > 0)
                {
                    // DXGI PresentStart (42): payload[0] = pIDXGISwapChain (pointer).
                    // D3D9 present: payload[0] used as a swap-chain identifier.
                    swapChainAddr = (ulong)data.PayloadValue(0);
                }
            }
            catch
            {
                swapChainAddr = 0; // fall back to lumping this process's presents together
            }

            var key = (pid, swapChainAddr);
            var tracker = _trackers.GetOrAdd(key, _ =>
                new SwapChainTracker(10_000_000) // DateTime ticks (100ns units) as the interval "frequency"
            );

            // TimeStamp.Ticks is derived from the original ETW QPC, so intervals from it are accurate.
            tracker.RecordPresent(data.TimeStamp.Ticks);

            // Bootstrap: if nothing is active yet, take this one; the selection timer refines within 2s.
            if (_activeTracker == null)
            {
                _activeTracker = tracker;
                _activePid = pid;
            }
        }

        /// <summary>
        /// Handles the (event-ID-filtered) DxgKrnl events used for refresh-rate attribution.
        /// </summary>
        private void OnDxgKrnlEvent(TraceEvent data)
        {
            if (data.ProviderGuid != DxgKrnl_provider) return;
            
            int id = (int)data.ID;
            //if (id == EventID_PresentMultiPlaneOverlay)
            //{
            //    Debug.WriteLine($"Observed id = {id}, kewords are {((ulong)data.Keywords).ToString("X")}");
            //}
            if (id == EventID_PresentMultiPlaneOverlay)
            {
                int src = TryGetSource(data);
                if (src < 0) return;

                // Flip activity per source → lets the fallback find the active display.
                // Composited: DWM emits these for the game's monitor. iflip: the game does.
                _flipRecent.AddOrUpdate(src, 1, (_, c) => c + 1);

                int pid = data.ProcessID;
                if (pid > 4) _pidToSource[pid] = src;   // strict attribution (skip Idle/System)
            }
            else if (id == EventID_VSyncDPC || id == EventID_VSyncDPCMultiPlane)
            {
                int src = TryGetSource(data);
                if (src < 0) return;

                var vt = _vsyncTrackers.GetOrAdd(src, _ => new VSyncSourceTracker());
                vt.Record(data.TimeStampRelativeMSec);
            }

        }

        private static int TryGetSource(TraceEvent data)
        {
            try
            {
                object v = data.PayloadByName("VidPnSourceId");
                return v == null ? -1 : Convert.ToInt32(v);
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Refresh rate for the monitor the active game is on: active PID -> VidPnSourceId -> measured Hz.
        /// Returns the last good reading when there's no live mapping (e.g. composited-windowed, where the
        /// game never carries its own source) rather than a misleading value.
        /// </summary>
        private float GetActiveRefreshRate()
        {
            // 1. Strict: active game's PID -> its source -> Hz.
            int pid = _activePid;
            if (pid != 0
                && _pidToSource.TryGetValue(pid, out int src)
                && _vsyncTrackers.TryGetValue(src, out var vt))
            {
                float hz = vt.RefreshHz;
                if (hz > 0f) { _lastRefreshHz = hz; return hz; }
            }

            // 2. Fallback: busiest-flipped display source. Covers composited games (Fallout 4),
            //    where the game never carries its own source because DWM owns the flip.
            int activeSrc = _activeSource;
            if (activeSrc >= 0 && _vsyncTrackers.TryGetValue(activeSrc, out var vt2))
            {
                float hz = vt2.RefreshHz;
                if (hz > 0f) { _lastRefreshHz = hz; return hz; }
            }

            // 3. Nothing resolvable yet.
            return _lastRefreshHz;
        }
        /// <summary>
        /// Picks the "primary" swap chain as the busiest presenter across ALL processes — the game
        /// out-presents overlays by a wide margin, so this reliably lands on it. Also prunes trackers
        /// for processes that have gone idle so memory doesn't grow unbounded.
        /// </summary>
        private void SelectPrimarySwapChain(object? state)
        {
            SwapChainTracker? bestTracker = null;
            int bestPid = 0;
            int bestCount = 0;

            var idle = new List<(int pid, ulong swapChain)>();

            foreach (var kvp in _trackers)
            {
                int count = kvp.Value.ConsumeRecentPresentCount();

                if (count == 0)
                {
                    idle.Add(kvp.Key); // no presents this interval — candidate for cleanup
                    continue;
                }

                if (count > bestCount)
                {
                    bestCount = count;
                    bestTracker = kvp.Value;
                    bestPid = kvp.Key.pid;
                }
            }

            // Remove idle trackers, but keep the currently active one so a brief pause
            // (loading screen, menu) doesn't blow away its accumulated frame history.
            foreach (var key in idle)
            {
                if (_trackers.TryGetValue(key, out var t) && !ReferenceEquals(t, _activeTracker))
                    _trackers.TryRemove(key, out _);
            }

            if (bestTracker != null)
            {
                _activeTracker = bestTracker;
                _activePid = bestPid;
            }
            // Busiest-flipped source for the refresh fallback, then reset the window.
            int bestSrc = -1, bestSrcCount = 0;
            foreach (var kvp in _flipRecent)
                if (kvp.Value > bestSrcCount) { bestSrcCount = kvp.Value; bestSrc = kvp.Key; }
            if (bestSrc >= 0) _activeSource = bestSrc;
            _flipRecent.Clear();
        }

        protected override void OnStop()
        {
            if (!_processing) return;

            Revoke("/framerate");
            Revoke("/refreshrate");
            Revoke("/1pctlows");
            Revoke("/maxrender");
            Revoke("/minrender");

            _primarySelectionTimer?.Dispose();
            _primarySelectionTimer = null;

            _etwSession?.Stop(true);
            _processThread?.Join(TimeSpan.FromSeconds(5));

            _etwSession?.Dispose();
            _etwSession = null;

            _trackers.Clear();
            _activeTracker = null;
            _activePid = 0;

            _pidToSource.Clear();
            _vsyncTrackers.Clear();
            _lastRefreshHz = 0f;
            _flipRecent.Clear();
            _activeSource = -1;
        }

        protected override string GetIdentifier()
        {
            return "dxgi";
        }

        protected override string GetDisplayName()
        {
            return "DirectX Info Provider";
        }

        protected override string GetDescription()
        {
            return "Provides frame rate and refresh rate for the active app using DXGI + DxgKrnl (ETW).";
        }
        private static readonly object _frameRateKey = new object();
        private static readonly object _1PctLowsKey = new object();
        private static readonly object _refreshRateKey = new object();
        private static readonly object _minRenderKey = new object();
        private static readonly object _maxRenderKey = new object();
        public override HardwareInfoSuggestion[] GetSuggestions(HardwareInfoSuggestionContext context)
        {
            if ((context.Expression == null || context.Expression.IsEmpty) && context.ParseException == null)
            {
                HardwareInfoSuggestion[] result = [
                    new HardwareInfoSuggestion(_frameRateKey,"Frame rate", "Gets the active frame rate in frames per second",null),
                    new HardwareInfoSuggestion(_1PctLowsKey,"1% lows","Gets the active 1% lows in frames per second",null),
                    new HardwareInfoSuggestion(_refreshRateKey,"Refresh rate","Gets the active refresh rate in Hz",null),
                    new HardwareInfoSuggestion(_minRenderKey,"Minimum render time", "Gets the minimum render time in milliseconds",null),
                    new HardwareInfoSuggestion(_maxRenderKey,"Maximum render time", "Gets the maximum render time in milliseconds",null),
                ];
                return result;
            }
            return base.GetSuggestions(context);
        }
        public override HardwareInfoExpression? ApplySuggestion(HardwareInfoSuggestionContext context, object key)
        {
            if ((context.Expression == null || context.Expression.IsEmpty) && context.ParseException == null)
            {
                if (key == _frameRateKey)
                {
                    return HardwareInfoExpression.Parse("/dxgi/framerate");
                }
                if (key == _1PctLowsKey)
                {
                    return HardwareInfoExpression.Parse("/dxgi/1pctlows");
                }
                if (key == _refreshRateKey)
                {
                    return HardwareInfoExpression.Parse("/dxgi/refreshrate");
                }
                if (key == _minRenderKey)
                {
                    return HardwareInfoExpression.Parse("/dxgi/minrender");
                }
                if (key == _maxRenderKey)
                {
                    return HardwareInfoExpression.Parse("/dxgi/maxrender");
                }

            }
            return base.ApplySuggestion(context, key);
        }
    }
}