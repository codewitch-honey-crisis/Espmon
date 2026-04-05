using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace HWKit
{
    public sealed class DxgiProvider : HardwareInfoProviderBase
    {
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        // ETW provider GUIDs
        public static readonly Guid DXGI_provider = Guid.Parse("{CA11C036-0102-4A2D-A6AD-F03CFED5D3C9}");
        public static readonly Guid D3D9_provider = Guid.Parse("{783ACA0A-790E-4D7F-8451-AA850511C6B9}");
        //public static readonly Guid DxgKrnl_provider = Guid.Parse("{802EC45A-1E99-4B83-9920-87C98277BA9D}");
        // DXGI event IDs (from the ETW manifest)
        public const int EventID_DxgiPresentStart = 42;
        public const int EventID_DxgiPresentStop = 43;

        // D3D9 event IDs
        public const int EventID_D3D9PresentStart = 1;
        public const int EventID_D3D9PresentStop = 2;

        TraceEventSession? _etwSession;
        Thread? _processThread;
        volatile bool _processing;

        // Foreground PID tracking — polled on a timer instead of per-event
        volatile int _foregroundPid;
        Timer? _foregroundPollTimer;

        // Frame tracking keyed by (processId, swapChainAddress)
        // This lets us distinguish the game's swap chain from overlay swap chains
        readonly ConcurrentDictionary<(int pid, ulong swapChain), SwapChainTracker> _trackers = new();

        // The "active" tracker that we're reporting metrics from
        // Updated when we detect which swap chain is the primary one for the foreground process
        volatile SwapChainTracker? _activeTracker;

        // How often to re-evaluate which swap chain is the "primary" one
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

        public DxgiProvider()
        {
        }

        protected override HardwareInfoProviderState GetState()
        {
            return _processing ? HardwareInfoProviderState.Started : HardwareInfoProviderState.Stopped;
        }

        protected override void OnStart()
        {
            if (_processing) return;
            _processing = true;

            // Start polling foreground window every 250ms instead of per-event
            UpdateForegroundPid(null);
            _foregroundPollTimer = new Timer(UpdateForegroundPid, null, 0, 250);

            // Start primary swap chain selection every 2 seconds
            _primarySelectionTimer = new Timer(SelectPrimarySwapChain, null, 2000, 2000);

            // Create and configure the ETW session
            _etwSession = new TraceEventSession(GetIdentifier());

            // Enable DXGI and D3D9 providers
            _etwSession.EnableProvider("Microsoft-Windows-DXGI");
            _etwSession.EnableProvider("Microsoft-Windows-D3D9");
            //_etwSession.EnableProvider("Microsoft-Windows-DXGKrnl");
            // Wire up the event handler
            _etwSession.Source.AllEvents += OnEtwEvent;

            // Publish metrics — they read from _activeTracker
            Publish("/framerate", "FPS", () => _activeTracker?.AverageFps ?? 0f);
            Publish("/1pctlows", "FPS", () => _activeTracker?.OnePercentLowFps ?? 0f);
            Publish("/maxrender", "MS", () => _activeTracker?.MaxFrameTimeMs ?? 0f);
            Publish("/minrender", "MS", () => _activeTracker?.MinFrameTimeMs ?? 0f);

            // Process ETW events on a background thread
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

        private void OnEtwEvent(TraceEvent data)
        {
            int eventId = (int)data.ID;
            var provider = data.ProviderGuid;

            // We only care about PresentStart events for frame counting
            bool isDxgiPresent = eventId == EventID_DxgiPresentStart && provider == DXGI_provider;
            bool isD3d9Present = eventId == EventID_D3D9PresentStart && provider == D3D9_provider;

            if (!isDxgiPresent && !isD3d9Present)
                return;

            int pid = data.ProcessID;
            int fgPid = _foregroundPid; // read the cached value — no P/Invoke here

            if (pid != fgPid)
                return;

            // Extract swap chain address from the event payload.
            // For DXGI PresentStart (event 42), the first payload field is "pIDXGISwapChain" (a pointer).
            // For D3D9, the payload structure differs but we still want to distinguish swap chains.
            ulong swapChainAddr = 0;
            try
            {
                if (data.PayloadNames.Length > 0)
                {
                    if (isDxgiPresent)
                    {
                        // The DXGI PresentStart template "PresentStartArgs" has:
                        //   pIDXGISwapChain (Pointer), Flags (UInt32), SyncInterval (Int32)

                        swapChainAddr = (ulong)data.PayloadValue(0);
                    }
                    else if (isD3d9Present)
                    {
                        // D3D9 present — use payload field 0 as swap chain identifier
                        swapChainAddr = (ulong)data.PayloadValue(0);
                    }
                }
            }
            catch
            {
                // If payload extraction fails, fall back to 0 (all presents lumped together)
                swapChainAddr = 0;
            }

            // Use the ETW event's QPC timestamp — this is when the Present() was actually called,
            // not when our handler happened to run. This eliminates the Stopwatch jitter problem.
            long qpcTimestamp = data.TimeStamp.Ticks; // TraceEvent gives us DateTime ticks
            // Actually, we need raw QPC. TraceEvent converts to DateTime internally.
            // data.TimeStampRelativeMSec gives us milliseconds relative to session start,
            // which is derived from the original QPC. We can convert back, or just use
            // the relative milliseconds directly for interval computation.
            // Let's use TimeStampRelativeMSec since it's already in a usable form.

            var key = (pid, swapChainAddr);
            var tracker = _trackers.GetOrAdd(key, _ =>
                new SwapChainTracker(10_000_000) // using DateTime ticks (100ns units) as our "frequency"
            );

            // Use TimeStamp.Ticks as our QPC-equivalent — TraceEvent derives this from the
            // original ETW QPC timestamp, so intervals computed from it are accurate.
            tracker.RecordPresent(data.TimeStamp.Ticks);

            // If we don't have an active tracker yet and this is from the foreground process,
            // just use this one immediately (will be refined by the selection timer)
            if (_activeTracker == null)
            {
                _activeTracker = tracker;
            }
        }

        /// <summary>
        /// Polls the foreground window and caches the PID. Called by a timer every 250ms.
        /// Much cheaper than calling GetForegroundWindow on every ETW event.
        /// </summary>
        private void UpdateForegroundPid(object? state)
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(hwnd, out int pid);
                    int oldPid = _foregroundPid;
                    _foregroundPid = pid;

                    // When the foreground process changes, clear the active tracker
                    // so it gets re-selected for the new process
                    if (pid != oldPid)
                    {
                        _activeTracker = null;
                    }
                }
            }
            catch
            {
                // Silently handle if window APIs fail
            }
        }

        /// <summary>
        /// Picks the "primary" swap chain for the current foreground process.
        /// The primary is the one with the highest present rate — this filters out
        /// overlays (Steam, Discord, etc.) which present much less frequently than the game.
        /// </summary>
        private void SelectPrimarySwapChain(object? state)
        {
            int fgPid = _foregroundPid;
            if (fgPid == 0) return;

            SwapChainTracker? bestTracker = null;
            int bestCount = 0;

            // Also clean up trackers for processes that are no longer foreground
            var keysToRemove = new List<(int pid, ulong swapChain)>();

            foreach (var kvp in _trackers)
            {
                int count = kvp.Value.ConsumeRecentPresentCount();

                if (kvp.Key.pid == fgPid)
                {
                    if (count > bestCount)
                    {
                        bestCount = count;
                        bestTracker = kvp.Value;
                    }
                }
                else
                {
                    // This tracker is for a non-foreground process — mark for cleanup
                    keysToRemove.Add(kvp.Key);
                }
            }

            // Clean up stale trackers to avoid unbounded memory growth
            foreach (var key in keysToRemove)
            {
                _trackers.TryRemove(key, out _);
            }

            if (bestTracker != null)
            {
                _activeTracker = bestTracker;
            }
        }

        protected override void OnStop()
        {
            if (!_processing) return;

            Revoke("/framerate");
            Revoke("/1pctlows");
            Revoke("/maxrender");
            Revoke("/minrender");

            _foregroundPollTimer?.Dispose();
            _foregroundPollTimer = null;

            _primarySelectionTimer?.Dispose();
            _primarySelectionTimer = null;

            _etwSession?.Stop(true);
            _processThread?.Join(TimeSpan.FromSeconds(5));

            _etwSession?.Dispose();
            _etwSession = null;

            _trackers.Clear();
            _activeTracker = null;
        }

        protected override string GetIdentifier()
        {
            return "dxgi";
        }

        protected override string GetDisplayName()
        {
            return "DirectX Info Provider";
        }
    }
}