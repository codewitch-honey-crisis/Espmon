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
        private struct CpuCoreEntry
        {
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
        // Reads the owning provider's current entry array live (under its lock), so it
        // always reflects the latest refresh and records accessor activity via Touch().
        sealed class EntryAccessor
        {
            readonly CimCpuProvider _owner;
            readonly int _index;
            public EntryAccessor(CimCpuProvider owner, int index)
            {
                ArgumentNullException.ThrowIfNull(owner, nameof(owner));
                _owner = owner;
                _index = index;
            }
            float Read(Func<CpuCoreEntry, float> selector)
            {
                _owner.Touch();
                _owner._dataMutex.WaitOne();
                try
                {
                    var entries = _owner._entries;
                    if (entries == null || _index < 0 || _index >= entries.Length)
                    {
                        return float.NaN;
                    }
                    return selector(entries[_index]);
                }
                finally
                {
                    _owner._dataMutex.ReleaseMutex();
                }
            }
            public float Frequency => Read(e => e.Frequency);
            public float Load => Read(e => e.Load);
        }

        // How fresh the cache must be before an accessor triggers a refresh.
        const long FreshnessMs = 1000;
        // How long with no accessor activity before the refresh loop parks itself.
        const long IdleMs = 5000;

        readonly Mutex _dataMutex = new Mutex();
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);

        Thread? _worker;
        volatile bool _started = false;

        long _lastQuery = 0;
        long _lastAccess = 0;

        CpuCoreEntry[]? _entries = null;

        protected override HardwareInfoProviderStatus GetState()
        {
            return _started ? HardwareInfoProviderStatus.Started : HardwareInfoProviderStatus.Stopped;
        }

        // Called by every accessor. Records activity and nudges the worker if the cache
        // has gone stale. Never blocks on a query.
        void Touch()
        {
            if (!_started) return;
            long now = Environment.TickCount64;
            Volatile.Write(ref _lastAccess, now);
            if (now - Volatile.Read(ref _lastQuery) >= FreshnessMs)
            {
                _wake.Set();
            }
        }

        void RunQuery()
        {
            var list = new List<CpuCoreEntry>();

            using (var sessionOptions = new CimSessionOptions())
            {
                sessionOptions.Culture = System.Globalization.CultureInfo.InvariantCulture;

                using (CimSession session = CimSession.Create(null, sessionOptions))
                {
                    using (var queryOptions = new CimOperationOptions())
                    {
                        queryOptions.SetCustomOption("__ProviderArchitecture", 64, false);

                        var instances = session.QueryInstances(
                            @"root\cimv2",
                            "WQL",
                            "SELECT Name, ActualFrequency, PercentProcessorTime FROM Win32_PerfFormattedData_Counters_ProcessorInformation", queryOptions);

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
                    }
                }
            }

            list.Sort((x, y) => { if (x.CpuIndex != y.CpuIndex) return x.CpuIndex.CompareTo(y.CpuIndex); return x.ThreadIndex.CompareTo(y.ThreadIndex); });

            _dataMutex.WaitOne();
            try
            {
                if (_entries == null || _entries.Length != list.Count)
                {
                    _entries = new CpuCoreEntry[list.Count];
                }
                for (var i = 0; i < list.Count; ++i)
                {
                    _entries[i] = list[i];
                }
            }
            finally
            {
                _dataMutex.ReleaseMutex();
            }
            Volatile.Write(ref _lastQuery, Environment.TickCount64);
        }

        void Worker()
        {
            while (_started)
            {
                _wake.WaitOne();
                while (_started)
                {
                    try
                    {
                        RunQuery();
                    }
                    catch
                    {
                        // Best-effort: keep the last good values on a transient failure.
                    }

                    if (Environment.TickCount64 - Volatile.Read(ref _lastAccess) >= IdleMs)
                    {
                        break;
                    }
                    if (_stop.Wait(1000))
                    {
                        break;
                    }
                }
            }
        }

        protected override void OnStart()
        {
            if (_started)
            {
                return;
            }

            _stop.Reset();
            _started = true;

            // Populate once up front, both to publish valid values immediately and so we
            // know how many threads to enumerate.
            RunQuery();
            Volatile.Write(ref _lastAccess, 0);

            CpuCoreEntry[]? snapshot = _entries;
            if (snapshot != null)
            {
                for (var i = 0; i < snapshot.Length; i++)
                {
                    CpuCoreEntry entry = snapshot[i];
                    var acc = new EntryAccessor(this, i);
                    Publish($"/cpu/{entry.CpuIndex}/thread/{entry.ThreadIndex}/clock", "MHz", new Func<float>(() => acc.Frequency));
                    Publish($"/cpu/{entry.CpuIndex}/thread/{entry.ThreadIndex}/load", "%", new Func<float>(() => acc.Load));
                }
            }

            _worker = new Thread(Worker)
            {
                IsBackground = true,
                Name = "cim_cpu_refresh"
            };
            _worker.Start();
        }
        protected override void OnStop()
        {
            if (!_started) return;
            _started = false;
            _stop.Set();
            _wake.Set();
            _worker?.Join();
            _worker = null;

            _dataMutex.WaitOne();
            try
            {
                _entries = null;
            }
            finally
            {
                _dataMutex.ReleaseMutex();
            }
        }
        protected override string GetDescription()
        {
            return "Provides CPU load and frequency information via the Windows CIM subsystem";
        }
    }

}