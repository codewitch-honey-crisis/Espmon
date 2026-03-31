using Microsoft.Diagnostics.Tracing.Session;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HWKit
{
    public sealed class DxgiProvider : HardwareInfoProviderBase
    {
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
        private sealed class FpsEntry
        {
            private static readonly double TicksPerMs = Stopwatch.Frequency / 1000.0;
            const int Maximum = 1000;
            public long Latest { get; private set; } = 0;
            public double AverageRenderMs { 
                get
                {
                    if (RenderTimes.Count == 0)
                    {
                        return 0;
                    }
                    return RenderTimes.Average();
                }
            }
            public double MaxRenderMs
            {
                get
                {
                    if (RenderTimes.Count == 0)
                    {
                        return 0;
                    }
                    return RenderTimes.Max();
                }
            }
            public double MinRenderMs
            {
                get
                {
                    if (RenderTimes.Count == 0)
                    {
                        return 0;
                    }
                    return RenderTimes.Min();
                }
            }
            public double AverageFps
            {
                get
                {
                    var avg = AverageRenderMs;
                    if (avg > 0)
                    {
                        return 1000.0d / avg;
                    }
                    return 0;
                }
            }
            public double OnePercentLowsFps
            {
                get
                {
                    if(RenderTimes.Count==0)
                    {
                        return 0;
                    }
                    var count = Math.Max(1, RenderTimes.Count / 100);
                    var avg = RenderTimes.Order().TakeLast(count).Average();
                    if (avg > 0)
                    {
                        return 1000.0d / avg;
                    }
                    return 0;
                }
            }
            public ConcurrentQueue<double> RenderTimes { get; } = new();
            public void Add(long timestamp)
            {
                if (Latest > 0)
                {
                    var renderTimeMs = (timestamp - Latest) / (double)TicksPerMs;
                    if (renderTimeMs > 750)
                    {
                        //Debug.WriteLine($"The DXGI info provider dropped a frame metric that claimed a render time of {renderTimeMs}ms because it was out of the expected range");
                        Latest = timestamp;
                        return;
                    }
                    //Debug.WriteLine($"Render time: {renderTimeMs}");
                    RenderTimes.Enqueue(renderTimeMs);
                    if (RenderTimes.Count > Maximum)
                    {
                        RenderTimes.TryDequeue(out _);
                    }
                }
                Latest = timestamp;
            }
            
            public FpsEntry()
            {
                
            }
        }
        
        //event codes (https://github.com/GameTechDev/PresentMon/blob/40ee99f437bc1061a27a2fc16a8993ee8ce4ebb5/PresentData/PresentMonTraceConsumer.cpp)
        public const int EventID_D3D9PresentStart = 1;
        public const int EventID_D3D9PresentStop = 2;
        public const int EventID_DxgiPresentStart = 42;
        public const int EventID_DxgiPresentStop = 43;
        public const int EventID_DxgKrnlPresentInfo = 0xb8; // 184 - kernel-level present event

        FpsEntry _foregroundEntry;
        Stopwatch? _watch = null;
        bool _processing = false;
        Thread _processThread;
        //ETW provider codes
        public static readonly Guid DXGI_provider = Guid.Parse("{CA11C036-0102-4A2D-A6AD-F03CFED5D3C9}");
        public static readonly Guid D3D9_provider = Guid.Parse("{783ACA0A-790E-4D7F-8451-AA850511C6B9}");
        public static readonly Guid DxgKrnl_provider = Guid.Parse("{802EC45A-1E99-4B83-9920-87C98277BA9D}");
        
        TraceEventSession? _etwSession;
        private sealed class FpsAccessor
        {
            readonly FpsEntry _entry;
            public FpsAccessor(FpsEntry entry) { _entry = entry; }
            public float AverageFps => (float)_entry.AverageFps;
            public float OnePercentLowsFps => (float)_entry.OnePercentLowsFps;
            public float MaxRenderMs => (float)_entry.MaxRenderMs;
            public float MinRenderMs => (float)_entry.MinRenderMs;
        }
        public DxgiProvider()
        {
            _etwSession = new TraceEventSession(GetIdentifier());
            _foregroundEntry = new FpsEntry();
            _watch = new Stopwatch();
            //handle event
            _etwSession.Source.AllEvents += data =>
            {
                //filter out frame presentation events
                int eventId = (int)data.ID;
                var provider = data.ProviderGuid;

                bool isPresent =

                    (eventId == EventID_DxgiPresentStart && provider == DXGI_provider);// ||
                    //(eventId == EventID_D3D9PresentStart && provider == D3D9_provider);
                    //(eventId == EventID_DxgKrnlPresentInfo && provider == DxgKrnl_provider);

                if (isPresent)
                {
                    if (eventId == EventID_DxgiPresentStart && provider == DXGI_provider)
                    {
                        //Debug.WriteLine($"tid={data.ThreadID} payload=[{string.Join(",", data.PayloadNames)}]");
                    }
                    GetWindowThreadProcessId(GetForegroundWindow(), out var fgPid);
                    int pid = data.ProcessID;
                    
                    if (pid == fgPid)
                    {
                        if (_watch != null)
                        {
                            if (provider == DXGI_provider)
                            {
                                //Debug.WriteLine($"DXGI event: id={data.ID} opcode={data.Opcode} task={data.Task}");
                            }
                            _foregroundEntry.Add(_watch.ElapsedTicks);
                            
                        }
                    }
                }
            };
            _processThread = new Thread(() =>
            {
                _etwSession?.Source.Process();
                _processing = false;
                Thread.MemoryBarrier();
            })
            {
                IsBackground = true
            };
            
            _etwSession.EnableProvider("Microsoft-Windows-D3D9");
            _etwSession.EnableProvider("Microsoft-Windows-DXGI");
            //_etwSession.EnableProvider("Microsoft-Windows-DXGKrnl");
        }
        protected override HardwareInfoProviderState GetState()
        {
            return _processing ? HardwareInfoProviderState.Started : HardwareInfoProviderState.Stopped;
        }
        protected override void OnStart()
        {
            if (_watch == null || _watch.IsRunning) { return; }
            _watch.Start();
            _processing = true;
            _processThread.Start();
            var accessor = new FpsAccessor(_foregroundEntry);
            Publish("/framerate", "FPS", () => accessor.AverageFps);
            Publish("/1pctlows", "FPS", () => accessor.OnePercentLowsFps);
            Publish("/maxrender", "MS", () => accessor.MaxRenderMs);
            Publish("/minrender", "MS", () => accessor.MinRenderMs);

        }
        protected override void OnStop()
        {
            if (_watch == null || !_watch.IsRunning) { return; }
            Revoke("/framerate");
            Revoke("/1pctlows");
            Revoke("/maxrender");
            Revoke("/minrender");
            _etwSession?.Stop(true);
            _processThread?.Join(); // wait for finish
            _watch.Stop();
            _watch.Reset();
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