using System.Runtime.InteropServices;

namespace HWKit
{
    public class CoreTempCpuProvider : HardwareInfoProviderBase
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr OpenFileMapping(uint dwDesiredAccess, bool bInheritHandle, string lpName);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr MapViewOfFile(IntPtr handle, uint dwDesiredAccess,uint offsetHigh, uint offsetLow,int size );
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool UnmapViewOfFile(IntPtr pointer);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr handle);
        const uint FILE_MAP_READ = 0x04;
       
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Ansi,Pack =1)]
        private struct CoreTempSharedDataEx
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public uint[] uiLoad;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            public uint[] uiTjMax;
            public uint uiCoreCnt;
            public uint uiCPUCnt;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public float[] fTemp;
            public float fVID;
            public float fCPUSpeed;
            public float fFSBSpeed;
            public float fMultiplier;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)]
            public string sCPUName;
            public byte ucFahrenheit;
            public byte ucDeltaToTjMax;
            // uiStructVersion = 2
            public byte ucTdpSupported;
            public byte ucPowerSupported;
            public uint uiStructVersion;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            public uint[] uiTdp;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            public float[] fPower;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public float[] fMultipliers;
        }
        private interface IAccessor
        {
            float Value { get; }
        }
        private sealed class CoreLoadAccessor : IAccessor
        {
            CoreTempCpuProvider _provider;
            int _index;
            public CoreLoadAccessor(CoreTempCpuProvider provider, int index)
            {
                _provider = provider;
                _index = index;
            }
            public float Value { 
                get {
                    if (_provider.State != HardwareInfoProviderState.Started) return float.NaN;
                    var data = Marshal.PtrToStructure<CoreTempSharedDataEx>(_provider._sharedPtr);
                    return data.uiLoad[_index];
                } 
            }
        }
        private sealed class CoreTemperatureAccessor : IAccessor
        {
            CoreTempCpuProvider _provider;
            int _cpu;
            int _index;
            public CoreTemperatureAccessor(CoreTempCpuProvider provider, int cpu, int index)
            {
                _provider= provider;
                _cpu = cpu;
                _index = index;
            }
            public float Value
            {
                get
                {
                    if (_provider.State != HardwareInfoProviderState.Started) return float.NaN;
                    var data = Marshal.PtrToStructure<CoreTempSharedDataEx>(_provider._sharedPtr);
                    float val = data.fTemp[_index];
                    if (0 == data.ucDeltaToTjMax)
                    {
                        
                        val = data.fTemp[_index];
                    }
                    else
                    {
                        val = data.uiTjMax[_cpu] - data.fTemp[_index];
                    }
                    if (data.ucFahrenheit > 0)
                    {
                        return (float)((val - 32) / 1.8);
                    }
                    return (float)val;
                }
            }
        }
        private sealed class CpuTJMaxAccessor : IAccessor
        {
            CoreTempCpuProvider _provider;
            int _index;
            public CpuTJMaxAccessor(CoreTempCpuProvider provider, int index)
            {
                _provider = provider;
                _index = index;
            }
            public float Value
            {
                get
                {
                    if (_provider.State != HardwareInfoProviderState.Started) return float.NaN;
                    var data = Marshal.PtrToStructure<CoreTempSharedDataEx>(_provider._sharedPtr);
                    if (data.ucFahrenheit > 0)
                    {
                        return (float)((data.uiTjMax[_index] - 32) / 1.8);
                    }
                    return data.uiTjMax[_index];
                }
            }
        }
        private sealed class CoreFrequencyAccessor : IAccessor
        {
            CoreTempCpuProvider _provider;
            int _index;
            public CoreFrequencyAccessor(CoreTempCpuProvider provider, int index)
            {
                _provider = provider;
                _index = index;
            }
            public float Value
            {
                get
                {
                    if (_provider.State != HardwareInfoProviderState.Started) return float.NaN;
                    var data = Marshal.PtrToStructure<CoreTempSharedDataEx>(_provider._sharedPtr);
                    return data.fMultipliers[_index]*data.fFSBSpeed;
                }
            }
        }
        private sealed class CoreMultiplierAccessor : IAccessor
        {
            CoreTempCpuProvider _provider;
            int _index;
            public CoreMultiplierAccessor(CoreTempCpuProvider provider, int index)
            {
                _provider= provider;
                _index = index;
            }
            public float Value
            {
                get
                {
                    if (_provider.State != HardwareInfoProviderState.Started) return float.NaN;
                    var data = Marshal.PtrToStructure<CoreTempSharedDataEx>(_provider._sharedPtr);
                    return data.fMultipliers[_index];
                }
            }
        }
      
        IntPtr _sharedHandle = IntPtr.Zero;
        IntPtr _sharedPtr = IntPtr.Zero;
        List<IAccessor> accessors = new List<IAccessor>();
        protected override HardwareInfoProviderState GetState()
        {
            return _sharedHandle != IntPtr.Zero ? HardwareInfoProviderState.Started : HardwareInfoProviderState.Stopped;
        }
        protected override string GetDisplayName()
        {
            return "CoreTemp CPU Provider";
        }
        protected override string GetIdentifier()
        {
            return "coretemp";
        }
        protected override void OnStart()
        {
            if (_sharedHandle!=IntPtr.Zero)
            {
                return;
            }
            _sharedHandle = OpenFileMapping(FILE_MAP_READ, true, "CoreTempMappingObjectEx");
            if(_sharedHandle!=IntPtr.Zero)
            {
                System.Diagnostics.Debug.Assert(4740 == Marshal.SizeOf<CoreTempSharedDataEx>(),"Something is wrong with your struct");
                _sharedPtr = MapViewOfFile(_sharedHandle,FILE_MAP_READ,0,0,Marshal.SizeOf<CoreTempSharedDataEx>());
            }
            if(_sharedPtr==IntPtr.Zero)
            {
                CloseHandle(_sharedHandle);
                _sharedHandle = IntPtr.Zero; 
                throw new SystemException("Unable to map view");
                
            }
            var data = Marshal.PtrToStructure<CoreTempSharedDataEx>(_sharedPtr);
            Publish($"/cpu/clock","MHz", new Func<float>(() => {
                if (State != HardwareInfoProviderState.Started) return float.NaN;
                var data = Marshal.PtrToStructure<CoreTempSharedDataEx>(_sharedPtr);
                return data.fCPUSpeed;
            }));
            Publish($"/bus/clock","MHz", new Func<float>(() => {
                if (State != HardwareInfoProviderState.Started) return float.NaN;
                var data = Marshal.PtrToStructure<CoreTempSharedDataEx>(_sharedPtr);
                return data.fFSBSpeed;
            }));
            Publish($"/cpu/multiplier","x", new Func<float>(() => {
                if (State != HardwareInfoProviderState.Started) return float.NaN;
                var data = Marshal.PtrToStructure<CoreTempSharedDataEx>(_sharedPtr);
                return data.fMultiplier;
            }));
            
            int coreIndex = 0;
            for (var i = 0; i < data.uiCPUCnt; i++) {
                var tjmaxAcc = new CpuTJMaxAccessor(this, i);
                Publish($"/cpu/{i}/tjmax", "°", new Func<float>(() => {
                    return tjmaxAcc.Value;
                }));
                for (int j = 0; j < data.uiCoreCnt; j++)
                {
                    var loadAcc = new CoreLoadAccessor(this, coreIndex);
                    Publish($"/cpu/{i}/core/{j}/load","%", new Func<float>(() => {
                        return loadAcc.Value;
                    }));
                    var multAcc = new CoreMultiplierAccessor(this, coreIndex);
                    Publish($"/cpu/{i}/core/{j}/multiplier","x", new Func<float>(() => {
                        return multAcc.Value;
                    }));
                    var tempAcc = new CoreTemperatureAccessor(this, i, coreIndex);
                    Publish($"/cpu/{i}/core/{j}/temperature", "°", new Func<float>(() => {
                        return tempAcc.Value;
                    }));
                    var freqAcc = new CoreFrequencyAccessor(this, coreIndex);
                    Publish($"/cpu/{i}/core/{j}/clock", "MHz",new Func<float>(() => {
                        return freqAcc.Value;
                    }));
                    ++coreIndex;
                }
            }
        }
        protected override void OnStop()
        {
            if (_sharedHandle==IntPtr.Zero)
            {
                return;
            }
            accessors.Clear();
            UnmapViewOfFile(_sharedPtr);
            _sharedPtr = IntPtr.Zero;
            CloseHandle(_sharedHandle);
            _sharedHandle = IntPtr.Zero;
        }
        private static readonly object _allCoreTempsKey = new object();
        private static readonly object _allCoreLoadsKey = new object();
        private static readonly object _allCoreClocksKey = new object();
        private static readonly object _maxCpuClockKey = new object(); 
        private static readonly object _maxSafeCpuTempsKey = new object();
        public override HardwareInfoSuggestion[] GetSuggestions(HardwareInfoSuggestionContext context)
        {
            if (context.Expression==null && context.ParseException==null)
            {
                HardwareInfoSuggestion[] result = [
                    new HardwareInfoSuggestion(_allCoreTempsKey,"All core temperatures","Retrieves the temperatures for every core across all CPUs in degrees Celsius"),
                    new HardwareInfoSuggestion(_allCoreLoadsKey,"All core loads","Retrieves the load percentage for every core across all CPUs as percentages"),
                    new HardwareInfoSuggestion(_allCoreClocksKey,"All core frequencies","Retrieves the clock frequencies for every core across all CPUs in MHz"),
                    new HardwareInfoSuggestion(_maxCpuClockKey,"Maximum CPU frequency","Retrieves the maximum frequency for the CPUs in MHz"),
                    new HardwareInfoSuggestion(_maxSafeCpuTempsKey,"Maximum safe CPU temperatures","Retrieves the maximum safe temperature across all CPUs in degrees Celsius"),
                ];
                return result;
            }
            return base.GetSuggestions(context);
        }
        public override HardwareInfoExpression? ApplySuggestion(HardwareInfoSuggestionContext context, object key)
        {
            if (context.Expression == null && context.ParseException == null)
            {
                if(key==_allCoreTempsKey) {
                    return HardwareInfoExpression.Parse("'^/coretemp/cpu/[0-9]+/core/[0-9]+/temperature$'");
                }
                if (key == _allCoreLoadsKey)
                {
                    return HardwareInfoExpression.Parse("'^/coretemp/cpu/[0-9]+/core/[0-9]+/load$'");
                }
                if (key == _allCoreClocksKey)
                {
                    return HardwareInfoExpression.Parse("'^/coretemp/cpu/[0-9]+/core/[0-9]+/clock$'");
                }
                if (key == _maxCpuClockKey)
                {
                    return HardwareInfoExpression.Parse("'^/coretemp/cpu/clock$'");
                }
                if (key == _maxSafeCpuTempsKey)
                {
                    return HardwareInfoExpression.Parse("'^/coretemp/cpu/[0-9]+/tjmax$'");
                }

            }
            return base.ApplySuggestion(context, key);
        }
    }
}
