using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;

using System.Runtime.Versioning;
namespace HWKit
{
    [SupportedOSPlatform("windows")]
    public class CimCpuProvider : HardwareInfoProviderBase
    {
        protected override string GetDisplayName()
        {
            return "Windows CIM CPU Provider";
        }
        protected override string GetIdentifier()
        {
            return "cim_cpu";
        }
        private struct CpuCoreEntry {
            public int CpuIndex;
            public int ThreadIndex;
            public float Frequency;
            public float Load;
            public CpuCoreEntry(int cpuIndex, int coreIndex, float frequency, float load)
            {
                CpuIndex = cpuIndex;
                ThreadIndex = coreIndex;
                Frequency = frequency;
                Load = load;
            }
        }
        sealed class EntryAccessor
        {
            int _index;
            CpuCoreEntry[]? _entries = null;
            public EntryAccessor(int index, CpuCoreEntry[]? entries ) { _index = index; ArgumentNullException.ThrowIfNull(entries, nameof(entries)); _entries = entries; }
            public int CpuIndex => _entries![_index].CpuIndex;
            public int ThreadIndex => _entries![_index].ThreadIndex;
            public float Frequency => _entries![_index].Frequency;
            public float Load => _entries![_index].Load;

        }
        Mutex? _mutex;
        CpuCoreEntry[]? _entries = null;
        bool _started = false;

        protected override HardwareInfoProviderState GetState()
        {
            return _started ? HardwareInfoProviderState.Started : HardwareInfoProviderState.Stopped;
        }
        void RunQuery()
        {
            using (var sessionOptions = new CimSessionOptions()) {
                sessionOptions.Culture = System.Globalization.CultureInfo.InvariantCulture;

                using (CimSession session = CimSession.Create(null,sessionOptions))
                {
                    using (var queryOptions = new CimOperationOptions())
                    {
                        queryOptions.SetCustomOption("__ProviderArchitecture", 64, false);

                        var instances = session.QueryInstances(
                            @"root\cimv2",
                            "WQL",
                            "SELECT Name, ActualFrequency, PercentProcessorTime FROM Win32_PerfFormattedData_Counters_ProcessorInformation",queryOptions);

                        var list = new List<CpuCoreEntry>();
                        foreach (CimInstance procObj in instances)
                        {
                            var name = (string)procObj.CimInstanceProperties["Name"].Value;
                            if (name.Contains("_Total"))
                            {
                                continue;
                            }
                            var tmp = name.Split(",");
                            if (tmp.Length != 2) continue;

                            int.TryParse(tmp[0], out var cpuIdx);
                            int.TryParse(tmp[1], out var threadIdx);
                            
                            var freq = Convert.ToSingle(procObj.CimInstanceProperties["ActualFrequency"].Value);
                            
                            var load = Convert.ToSingle(procObj.CimInstanceProperties["PercentProcessorTime"].Value);
                            
                            list.Add(new CpuCoreEntry(cpuIdx, threadIdx, freq, load));
                        }

                        list.Sort((x, y) => { if (x.CpuIndex != y.CpuIndex) return x.CpuIndex.CompareTo(y.CpuIndex); return x.ThreadIndex.CompareTo(y.ThreadIndex); });
                        if (_entries == null || _entries.Length != list.Count)
                        {
                            _entries = new CpuCoreEntry[list.Count];
                        }

                        for (var i = 0; i < list.Count; ++i)
                        {
                            _entries[i] = list[i];
                        }
                    }
                }
            }
        }
        
     
        protected override void OnStart()
        {
            if(_started)
            {
                return;
            }
            _mutex = new Mutex();

            var thread = new Thread(() =>
            {
                _mutex.WaitOne();
                var started = _started;
                _mutex.ReleaseMutex();
                while (started && _mutex!=null)
                {
                    try
                    {
                        _mutex.WaitOne();
                        started = _started;
                        if(started)
                        {
                            RunQuery();
                        }
                    }
                    finally
                    {
                        _mutex.ReleaseMutex();
                    }
                    Thread.Sleep(100);
                }
            });
            _mutex.WaitOne();
            try
            {
                RunQuery();
                if (_entries != null)
                {

                    for (var i = 0; i < _entries.Length; i++)
                    {
                        CpuCoreEntry entry = _entries[i];
                        var acc = new EntryAccessor(i, _entries);
                        Publish($"/cpu/{entry.CpuIndex}/thread/{entry.ThreadIndex}/clock","MHz", new Func<float>(() => acc.Frequency));
                        Publish($"/cpu/{entry.CpuIndex}/thread/{entry.ThreadIndex}/load","%", new Func<float>(() => acc.Load));
                    }
                }
            }
            finally { _mutex.ReleaseMutex(); }
            
            _started = true;
            thread.Start();

        }
        protected override void OnStop()
        {
            if (_mutex == null) return;
            _mutex.WaitOne();
            try
            {
                if (_started)
                {
                    _entries = null;
                }
                _started = false;
            }
            finally
            {
                _mutex.ReleaseMutex();
                _mutex?.Dispose();
                _mutex = null;
            }
            
        }
        
    }
        
}
