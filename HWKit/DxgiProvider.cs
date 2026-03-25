using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Tracing.StackSources;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

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
            const int Maximum = 1000;
            public string Name { get; } = string.Empty;
            public int Pid { get; } = -1;
            public long Latest { get; private set; } = 0;
            public double Fps { get; set; } = 0d;
            public ConcurrentQueue<long> Timestamps { get; } = new ConcurrentQueue<long>();
            public void Add(long timestamp)
            {
                Timestamps.Enqueue(timestamp);
                if (Timestamps.Count > Maximum)
                {
                    while (!Timestamps.TryDequeue(out _))
                    {
                        Thread.Sleep(50);
                    }
                }
                Latest = timestamp;
            }
            //get the number of timestamps withing interval
            public int GetFrameCount(long from, long to)
            {
                int c = 0;

                foreach (var ts in Timestamps)
                {
                    if (ts >= from && ts <= to) c++;
                }
                return c;
            }
            public FpsEntry(string name, int pid)
            {
                Name = name;
                Pid = pid;
            }
        }

        //event codes (https://github.com/GameTechDev/PresentMon/blob/40ee99f437bc1061a27a2fc16a8993ee8ce4ebb5/PresentData/PresentMonTraceConsumer.cpp)
        public const int EventID_D3D9PresentStart = 1;
        public const int EventID_DxgiPresentStart = 42;
        public const int EventID_DxgKrnlPresentInfo = 0xb8; // 184 - kernel-level present event

        ConcurrentDictionary<int, FpsEntry> _entries = new();
        Stopwatch? _watch = null;
        bool _processing = false;
        Thread _processThread;
        Thread _queryThread;
        int _foregroundPid = -1;
        //ETW provider codes
        public static readonly Guid DXGI_provider = Guid.Parse("{CA11C036-0102-4A2D-A6AD-F03CFED5D3C9}");
        public static readonly Guid D3D9_provider = Guid.Parse("{783ACA0A-790E-4D7F-8451-AA850511C6B9}");
        public static readonly Guid DxgKrnl_provider = Guid.Parse("{802EC45A-1E99-4B83-9920-87C98277BA9D}");

        TraceEventSession? _etwSession;
        private sealed class FpsAccessor
        {
            readonly FpsEntry _entry;
            public FpsAccessor(FpsEntry entry) { _entry = entry; }
            public float Fps => (float)_entry.Fps;
        }
        public DxgiProvider()
        {
            _etwSession = new TraceEventSession(GetIdentifier());
            _etwSession.BufferSizeMB = 256;
            
            _watch = new Stopwatch();
            //handle event
            _etwSession.Source.AllEvents += data =>
            {
                //filter out frame presentation events
                int eventId = (int)data.ID;
                var provider = data.ProviderGuid;

                bool isPresent =
                    (eventId == EventID_DxgiPresentStart && provider == DXGI_provider) ||
                    (eventId == EventID_D3D9PresentStart && provider == D3D9_provider) ||
                    (eventId == EventID_DxgKrnlPresentInfo && provider == DxgKrnl_provider);

                if (isPresent)
                {
                    int pid = data.ProcessID;
                    long t;

                    if (_watch != null)
                    {
                        t = _watch.ElapsedMilliseconds;
                        if (!_entries.TryGetValue(pid, out var frame))
                        {
                            // not yet in Dictionary, add it

                            string name = "";
                            var proc = Process.GetProcessById(pid);
                            if (proc != null)
                            {
                                using (proc)
                                {

                                    name = proc.ProcessName;

                                }
                            }
                            else { name = pid.ToString(); }
                            var entry = new FpsEntry(name, pid);
                            _entries[pid] = entry;
                            entry.Add(t);
                            var accessor = new FpsAccessor(entry);
                            Debug.WriteLine($"DXGI publish {name} ({data.ProcessID})");
                            Publish($"/processes/{pid}/fps", "FPS", () => accessor.Fps);
                            if (pid.ToString() != name)
                            {
                                Publish($"/processes/{name}/fps", "FPS", () => accessor.Fps);
                            }
                        }
                        else
                        {
                            frame.Add(t);
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
            _queryThread = new Thread(() =>
            {
                while (_processing)
                {
                    UpdateEntries();
                    Thread.Sleep(1000);
                }

            })
            {
                IsBackground = true
            };
            _etwSession.EnableProvider("Microsoft-Windows-D3D9");
            _etwSession.EnableProvider("Microsoft-Windows-DXGI");

        }
        protected override HardwareInfoProviderState GetState()
        {
            return _processing ? HardwareInfoProviderState.Started : HardwareInfoProviderState.Stopped;
        }
        protected override void OnStart()
        {
            if (_watch == null || _watch.IsRunning) { return; }
            Debug.WriteLine("DXGI start");
            _watch.Start();
            _processing = true;
            _processThread.Start();
            _queryThread.Start();

        }
        protected override void OnStop()
        {
            if (_watch == null || !_watch.IsRunning) { return; }
            Debug.WriteLine("DXGI stopping");
            _etwSession?.Stop(true);
            _processThread?.Join(); // wait for finish
            _queryThread?.Join();
            _watch.Stop();
            _watch.Reset();
            Debug.WriteLine("DXGI stopped");
        }
        protected override string GetIdentifier()
        {
            return "dxgi";
        }
        protected override string GetDisplayName()
        {
            return "DirectX Info Provider";
        }
        private void UpdateEntries()
        {
            if(0==GetWindowThreadProcessId(GetForegroundWindow(), out var fgPid))
            {
                fgPid = -1;
            }
            if(fgPid==-1 || fgPid!=_foregroundPid)
            {
                try
                {
                    Revoke("/processes/#foreground/fps");
                }
                catch { }
            } 
            _foregroundPid = fgPid;
            Thread.MemoryBarrier();
            if (_foregroundPid!=-1)
            {
                foreach(var kvp in _entries)
                {
                    if (kvp.Key==_foregroundPid)
                    {
                        var entry = kvp.Value;
                        var accessor = new FpsAccessor(entry);
                        Publish("/processes/#foreground/fps", "FPS",()=>accessor.Fps);
                        break;
                    }
                }
                
            }
            long t1, t2;
            long dt = 2000;
            var toRemove = new List<(string Name, int Pid)>();
            if (_watch != null)
            {
                t2 = _watch.ElapsedMilliseconds;
                t1 = t2 - dt;
                foreach (var x in _entries)
                {
                    if (t2 - x.Value.Latest >= 10000)
                    {
                        toRemove.Add((x.Value.Name, x.Value.Pid));
                    }
                    else
                    {
                        var name = x.Value.Name;
                        var pid = x.Key;
                        if (name != null)
                        {
                            int count = x.Value.GetFrameCount(t1, t2);
                            x.Value.Fps = (double)count / dt * 1000.0;
                            Thread.MemoryBarrier();
                        }
                    }
                }
                for (var i = 0; i < toRemove.Count; i++)
                {
                    var t = toRemove[i];
                    Debug.WriteLine($"DXGI revoke process {t.Name} ({t.Pid})");
                    if (_entries.TryRemove(t.Pid, out _))
                    {
                        try
                        {
                            Revoke($"/processes/{t.Pid}/fps");
                        }
                        catch { }
                        try
                        {
                            if (t.Name != t.Pid.ToString())
                            {
                                Revoke($"/processes/{t.Name}/fps");
                            }
                        }
                        catch { }
                    }
                }
            }
        }
    }
}