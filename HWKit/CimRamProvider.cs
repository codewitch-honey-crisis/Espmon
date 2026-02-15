
using Microsoft.Management.Infrastructure;

using System.Runtime.Versioning;


namespace HWKit
{
    [SupportedOSPlatform("windows")]
    public class CimRamProvider : HardwareInfoProviderBase
    {
        const float _multiplicand = 1000f;
        const float _divisor = (1024*1024);
        Mutex? _mutex;
        bool _started = false;
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
        protected override HardwareInfoProviderState GetState()
        {

            return _started ? HardwareInfoProviderState.Started : HardwareInfoProviderState.Stopped;
        }
        void RunQuery()
        {
            using (CimSession session = CimSession.Create(null))
            {
                var instances = session.QueryInstances(
                    @"root\cimv2",
                    "WQL",
                    "SELECT TotalVisibleMemorySize, FreePhysicalMemory, FreeVirtualMemory FROM Win32_OperatingSystem");

                foreach (CimInstance procObj in instances)
                {
                    _total = (UInt64)procObj.CimInstanceProperties["TotalVisibleMemorySize"].Value;
                    _free = (UInt64)procObj.CimInstanceProperties["FreePhysicalMemory"].Value;
                    _freeVirtual = (UInt64)procObj.CimInstanceProperties["FreeVirtualMemory"].Value;
                    break;
                }
            }

        }
        private float SafeTotal
        {
            get
            {
                if(_mutex==null)
                {
                    return float.NaN;
                }
                _mutex.WaitOne();
                try
                {
                    if(!_started) { return float.NaN; }
                    return _total*_multiplicand/_divisor;
                }
                finally
                {
                    _mutex.ReleaseMutex();
                }
            }
        }
        private float SafeFree
        {
            get
            {
                if (_mutex == null)
                {
                    return float.NaN;
                }
                _mutex.WaitOne();
                try
                {
                    if (!_started) { return float.NaN; }
                    return _free * _multiplicand / _divisor;
                }
                finally
                {
                    _mutex.ReleaseMutex();
                }
            }
        }
        private float SafeFreeVirtual
        {
            get
            {
                if (_mutex == null)
                {
                    return float.NaN;
                }
                _mutex.WaitOne();
                try
                {
                    if (!_started) { return float.NaN; }
                    return _freeVirtual *_multiplicand/ _divisor;
                }
                finally
                {
                    _mutex.ReleaseMutex();
                }
            }
        }
        private float SafeLoad
        {
            get
            {
                if (_mutex == null)
                {
                    return float.NaN;
                }
                _mutex.WaitOne();
                try
                {
                    if (!_started) { return float.NaN; }
                    return 100-((float)Math.Round(_free * 100 / _total));
                }
                finally
                {
                    _mutex.ReleaseMutex();
                }
            }
        }
        protected override void OnStart()
        {
            if (_started)
            {
                return;
            }
            _mutex = new Mutex();

            var thread = new Thread(() =>
            {
                _mutex.WaitOne();
                var started = _started;
                _mutex.ReleaseMutex();
                while (started && _mutex != null)
                {
                    try
                    {
                        _mutex.WaitOne();
                        started = _started;
                        if (started)
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
                
            }
            finally { _mutex.ReleaseMutex(); }

            Publish($"/ram/total","MB", new Func<float>(() => SafeTotal));
            Publish($"/ram/free", "MB",new Func<float>(() => SafeFree));
            Publish($"/ram/free/virtual","MB", new Func<float>(() => SafeFreeVirtual));
            Publish($"/ram/load", "%",new Func<float>(() => SafeLoad));

            _started = true;
            thread.Start();

        }
        protected override void OnStop()
        {
            if (_mutex == null) return;
            _mutex.WaitOne();
            _started = false;
            _mutex.ReleaseMutex();
            _mutex?.Dispose();
            _mutex = null;
            

        }

    }

}
