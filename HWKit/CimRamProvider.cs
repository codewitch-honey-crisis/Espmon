using Microsoft.Management.Infrastructure;

using System.Runtime.Versioning;


namespace HWKit
{
    [SupportedOSPlatform("windows")]
    public class CimRamProvider : HardwareInfoProviderBase
    {
        const float _multiplicand = 1000f;
        const float _divisor = (1024 * 1024);

        // How fresh the cache must be before an accessor triggers a refresh.
        const long FreshnessMs = 1000;
        // How long with no accessor activity before the refresh loop parks itself.
        const long IdleMs = 5000;

        // Guards the cached values below. Created once and reused across start/stop
        // cycles so accessors can never hit a disposed handle.
        readonly Mutex _dataMutex = new Mutex();
        // Wakes the parked worker when an accessor needs fresh data.
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        // Signals the worker to exit on stop (and interrupts its 1s sleep).
        readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);

        Thread? _worker;
        volatile bool _started = false;

        // Environment.TickCount64 timestamps. 64-bit aligned reads/writes are atomic
        // on supported runtimes; Volatile is used for ordering.
        long _lastQuery = 0;
        long _lastAccess = 0;

        float _total = 0;
        float _free = 0;
        float _freeVirtual = 0;

        protected override string GetDisplayName()
        {
            return "Windows CIM RAM Provider";
        }
        protected override string GetIdentifier()
        {
            return "cim_ram";
        }
        protected override HardwareInfoProviderStatus GetState()
        {
            return _started ? HardwareInfoProviderStatus.Started : HardwareInfoProviderStatus.Stopped;
        }

        // Called by every accessor. Records activity and, if the cached data has aged
        // past FreshnessMs, nudges the worker so it starts (or keeps) refreshing.
        // Never blocks on a query: the accessor that calls this still gets the last
        // value immediately.
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
            float total = float.NaN, free = float.NaN, freeVirtual = float.NaN;

            using (CimSession session = CimSession.Create(null))
            {
                var instances = session.QueryInstances(
                    @"root\cimv2",
                    "WQL",
                    "SELECT TotalVisibleMemorySize, FreePhysicalMemory, FreeVirtualMemory FROM Win32_OperatingSystem");

                foreach (CimInstance procObj in instances)
                {
                    total = (UInt64)procObj.CimInstanceProperties["TotalVisibleMemorySize"].Value;
                    free = (UInt64)procObj.CimInstanceProperties["FreePhysicalMemory"].Value;
                    freeVirtual = (UInt64)procObj.CimInstanceProperties["FreeVirtualMemory"].Value;
                    break;
                }
            }

            _dataMutex.WaitOne();
            try
            {
                _total = total;
                _free = free;
                _freeVirtual = freeVirtual;
            }
            finally
            {
                _dataMutex.ReleaseMutex();
            }
            Volatile.Write(ref _lastQuery, Environment.TickCount64);
        }

        // Worker lifecycle: park on _wake until an accessor needs data, then refresh
        // once a second until IdleMs passes with no access, then park again.
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
                        // Best-effort telemetry: a transient CIM failure shouldn't kill
                        // the loop. The cached values simply remain as they were.
                    }

                    if (Environment.TickCount64 - Volatile.Read(ref _lastAccess) >= IdleMs)
                    {
                        break; // no recent access -> go back to parking
                    }
                    if (_stop.Wait(1000))
                    {
                        break; // stop requested
                    }
                }
            }
        }

        private float SafeTotal
        {
            get
            {
                Touch();
                _dataMutex.WaitOne();
                try
                {
                    if (!_started) { return float.NaN; }
                    return _total * _multiplicand / _divisor;
                }
                finally
                {
                    _dataMutex.ReleaseMutex();
                }
            }
        }
        private float SafeFree
        {
            get
            {
                Touch();
                _dataMutex.WaitOne();
                try
                {
                    if (!_started) { return float.NaN; }
                    return _free * _multiplicand / _divisor;
                }
                finally
                {
                    _dataMutex.ReleaseMutex();
                }
            }
        }
        private float SafeFreeVirtual
        {
            get
            {
                Touch();
                _dataMutex.WaitOne();
                try
                {
                    if (!_started) { return float.NaN; }
                    return _freeVirtual * _multiplicand / _divisor;
                }
                finally
                {
                    _dataMutex.ReleaseMutex();
                }
            }
        }
        private float SafeLoad
        {
            get
            {
                Touch();
                _dataMutex.WaitOne();
                try
                {
                    if (!_started) { return float.NaN; }
                    if (_total <= 0) { return float.NaN; }
                    return 100 - ((float)Math.Round(_free * 100 / _total));
                }
                finally
                {
                    _dataMutex.ReleaseMutex();
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

            // Populate once up front so published values are valid immediately.
            RunQuery();
            Volatile.Write(ref _lastAccess, 0); // no accessor activity yet -> stay parked

            Publish($"/ram/total", "MB", new Func<float>(() => SafeTotal));
            Publish($"/ram/free", "MB", new Func<float>(() => SafeFree));
            Publish($"/ram/free/virtual", "MB", new Func<float>(() => SafeFreeVirtual));
            Publish($"/ram/load", "%", new Func<float>(() => SafeLoad));

            _worker = new Thread(Worker)
            {
                IsBackground = true,
                Name = "cim_ram_refresh"
            };
            _worker.Start();
        }
        protected override void OnStop()
        {
            if (!_started) return;
            _started = false;
            _stop.Set();   // break the 1s sleep
            _wake.Set();   // unpark the worker if it's waiting
            _worker?.Join();
            _worker = null;
        }
        protected override string GetDescription()
        {
            return "Provides RAM usage information via the Windows CIM subsystem";
        }

    }

}