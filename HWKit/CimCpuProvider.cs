using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;
using System.Runtime.InteropServices;
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

        private struct CpuEntry
        {
            public float MaxFrequency;
            public CpuEntry(float maxFrequency)
            {
                MaxFrequency = maxFrequency;
            }
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
        sealed class CoreEntryAccessor
        {
            readonly CimCpuProvider _owner;
            readonly int _index;
            public CoreEntryAccessor(CimCpuProvider owner, int index)
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
                    var entries = _owner._coreEntries;
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

        sealed class CpuEntryAccessor
        {
            readonly CimCpuProvider _owner;
            readonly int _index;
            public CpuEntryAccessor(CimCpuProvider owner, int index)
            {
                ArgumentNullException.ThrowIfNull(owner, nameof(owner));
                _owner = owner;
                _index = index;
            }
            float Read(Func<CpuEntry, float> selector)
            {
                _owner.Touch();
                _owner._dataMutex.WaitOne();
                try
                {
                    var entries = _owner._cpuEntries;
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
            public float MaxFrequency => Read(e => e.MaxFrequency);
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
        CpuEntry[]? _cpuEntries = null;
        CpuCoreEntry[]? _coreEntries = null;
        
        uint[] _perSocketMax = Array.Empty<uint>();
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
        static int PackageIndexFor(string perfName, List<CpuTopology.Package> pkgs)
        {
            // perfName like "0,3" => group 0, processor 3
            var parts = perfName.Split(',');
            ushort group = ushort.Parse(parts[0]);
            int proc = int.Parse(parts[1]);
            foreach (var p in pkgs)
                if (p.Contains(group, proc)) return p.Index;
            return -1; // "_Total"/"g,_Total" rows won't match — skip them
        }
        void RunQuery()
        {

            var list = new List<CpuCoreEntry>();
            CpuEntry[]? cpuEntries = null;
            using (var sessionOptions = new CimSessionOptions())
            {
                sessionOptions.Culture = System.Globalization.CultureInfo.InvariantCulture;

                using (CimSession session = CimSession.Create(null, sessionOptions))
                {
                    using (var queryOptions = new CimOperationOptions())
                    {
                        if (!_started)
                        {
                            var pkgs = CpuTopology.GetPackages();
                            cpuEntries = new CpuEntry[pkgs.Count];

                            var cpuInstances = session.QueryInstances(
                                @"root\cimv2", "WQL",
                                "SELECT MaxClockSpeed FROM Win32_Processor", queryOptions);

                            int socketIndex = 0;
                            foreach (var inst in cpuInstances)
                            {
                                using (inst)
                                {
                                    var max = Convert.ToUInt32(inst.CimInstanceProperties["MaxClockSpeed"].Value ?? 0u);
                                    if (socketIndex < cpuEntries.Length)
                                        cpuEntries[socketIndex] = new CpuEntry(max);
                                    socketIndex++;
                                }
                            }

                            // sanity: these should agree — one Win32_Processor instance per package
                            System.Diagnostics.Debug.Assert(socketIndex == pkgs.Count,
                                $"socket count mismatch: {socketIndex} Win32_Processor vs {pkgs.Count} packages");
                        }
                        queryOptions.SetCustomOption("__ProviderArchitecture", 64, false);

                        var instances = session.QueryInstances(
                            @"root\cimv2",
                            "WQL",
                            "SELECT Name, ActualFrequency, PercentProcessorTime FROM Win32_PerfFormattedData_Counters_ProcessorInformation", queryOptions);
                        // using var searcher = new ManagementObjectSearcher(
                        //"SELECT Name, MaxClockSpeed FROM Win32_Processor");
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
                if(_cpuEntries==null)
                {
                    _cpuEntries = cpuEntries;
                }
                if (_coreEntries == null || _coreEntries.Length != list.Count)
                {
                    _coreEntries = new CpuCoreEntry[list.Count];
                }
                for (var i = 0; i < list.Count; ++i)
                {
                    _coreEntries[i] = list[i];
                }
                _started = true;
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
           
            // Populate once up front, both to publish valid values immediately and so we
            // know how many threads to enumerate.
            RunQuery();
            Volatile.Write(ref _lastAccess, 0);
            CpuEntry[]? cpuSnapshot = _cpuEntries;
            if (cpuSnapshot != null)
            {
                for (var i = 0; i < cpuSnapshot.Length; i++)
                {
                    CpuEntry entry = cpuSnapshot[i];
                    var acc = new CpuEntryAccessor(this, i);
                    Publish($"/cpu/{i}/maxclock", "MHz", new Func<float>(() => acc.MaxFrequency));
                }
            }

            CpuCoreEntry[]? coreSnapshot = _coreEntries;
            if (coreSnapshot != null)
            {
                for (var i = 0; i < coreSnapshot.Length; i++)
                {
                    CpuCoreEntry entry = coreSnapshot[i];
                    var acc = new CoreEntryAccessor(this, i);
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
                _cpuEntries = null;
                _coreEntries = null;
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
    
    static class CpuTopology
    {
        private const int RelationProcessorPackage = 3;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetLogicalProcessorInformationEx(
            int RelationshipType, IntPtr Buffer, ref int ReturnedLength);

        public readonly struct Package
        {
            public readonly int Index;                              // 0,1,2… = enumeration order
            public readonly (ushort Group, ulong Mask)[] Affinities;
            public Package(int index, (ushort, ulong)[] aff) { Index = index; Affinities = aff; }

            public bool Contains(ushort group, int processor)
            {
                foreach (var a in Affinities)
                    if (a.Group == group && (a.Mask & (1UL << processor)) != 0) return true;
                return false;
            }
        }

        public static List<Package> GetPackages()
        {
            int len = 0;
            if (GetLogicalProcessorInformationEx(RelationProcessorPackage, IntPtr.Zero, ref len))
                throw new InvalidOperationException("Unexpected success probing length.");
            if (Marshal.GetLastWin32Error() != ERROR_INSUFFICIENT_BUFFER)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            IntPtr buffer = Marshal.AllocHGlobal(len);
            try
            {
                if (!GetLogicalProcessorInformationEx(RelationProcessorPackage, buffer, ref len))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

                var packages = new List<Package>();
                int offset = 0, index = 0;

                while (offset < len)
                {
                    IntPtr rec = IntPtr.Add(buffer, offset);
                    // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX: +0 Relationship, +4 Size, +8 union
                    int size = Marshal.ReadInt32(rec, 4);
                    // PROCESSOR_RELATIONSHIP (at +8): +8 Flags, +9 EfficiencyClass,
                    //   +10 Reserved[20], +30 GroupCount(WORD), +32 GroupMask[]
                    ushort groupCount = (ushort)Marshal.ReadInt16(rec, 30);

                    var aff = new (ushort, ulong)[groupCount];
                    for (int g = 0; g < groupCount; g++)
                    {
                        // GROUP_AFFINITY (16 bytes): +0 KAFFINITY Mask (ptr-sized), +8 Group(WORD)
                        int b = 32 + g * 16;
                        ulong mask = (ulong)(long)Marshal.ReadIntPtr(rec, b);
                        ushort grp = (ushort)Marshal.ReadInt16(rec, b + 8);
                        aff[g] = (grp, mask);
                    }
                    packages.Add(new Package(index++, aff));
                    offset += size;                    // stride by Size, not sizeof
                }
                return packages;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }
}