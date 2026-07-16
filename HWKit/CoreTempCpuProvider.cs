using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HWKit
{
    public class CoreTempCpuProvider : HardwareInfoProviderBase
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr OpenFileMapping(uint dwDesiredAccess, bool bInheritHandle, string lpName);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr MapViewOfFile(IntPtr handle, uint dwDesiredAccess, uint offsetHigh, uint offsetLow, int size);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool UnmapViewOfFile(IntPtr pointer);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr handle);
        const uint FILE_MAP_READ = 0x04;

        // Inline (blittable) fixed-size buffers. These replace the previous
        // [MarshalAs(ByValArray)] managed arrays and the [MarshalAs(ByValTStr)]
        // string, both of which made the struct NON-blittable and forced
        // reflection-based marshalling (Marshal.SizeOf / Marshal.PtrToStructure).
        // NativeAOT mishandles that path for this struct, which is why the
        // provider silently returned NaN under AOT. With InlineArray the struct
        // is fully blittable, so it can be read with a plain memory copy.
        [InlineArray(256)] private struct UInt256 { private uint _e0; }
        [InlineArray(128)] private struct UInt128 { private uint _e0; }
        [InlineArray(256)] private struct Float256 { private float _e0; }
        [InlineArray(128)] private struct Float128 { private float _e0; }
        [InlineArray(100)] private struct Byte100 { private byte _e0; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CoreTempSharedDataEx
        {
            public UInt256 uiLoad;
            public UInt128 uiTjMax;
            public uint uiCoreCnt;
            public uint uiCPUCnt;
            public Float256 fTemp;
            public float fVID;
            public float fCPUSpeed;
            public float fFSBSpeed;
            public float fMultiplier;
            public Byte100 sCPUName;
            public byte ucFahrenheit;
            public byte ucDeltaToTjMax;
            // uiStructVersion = 2
            public byte ucTdpSupported;
            public byte ucPowerSupported;
            public uint uiStructVersion;
            public UInt128 uiTdp;
            public Float128 fPower;
            public Float256 fMultipliers;

            // The CPU name is not used by any accessor below, but it's decoded
            // here (safe, AOT-friendly) in case a caller wants it later.
            public readonly string GetCpuName()
            {
                Span<char> chars = stackalloc char[100];
                int n = 0;
                for (; n < 100; n++)
                {
                    byte b = sCPUName[n];
                    if (b == 0) break;
                    chars[n] = (char)b;
                }
                return new string(chars[..n]);
            }
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
            public float Value
            {
                get
                {
                    if (_provider.Status != HardwareInfoProviderStatus.Started) return float.NaN;
                    try
                    {
                        _provider.EnsureFileMapping();
                    }
                    catch { return float.NaN; }
                    var data = _provider.ReadData();
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
                _provider = provider;
                _cpu = cpu;
                _index = index;
            }
            public float Value
            {
                get
                {
                    if (_provider.Status != HardwareInfoProviderStatus.Started) return float.NaN;
                    try
                    {
                        _provider.EnsureFileMapping();
                    }
                    catch { return float.NaN; }
                    var data = _provider.ReadData();
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
                    if (_provider.Status != HardwareInfoProviderStatus.Started) return float.NaN;
                    try
                    {
                        _provider.EnsureFileMapping();
                    }
                    catch { return float.NaN; }
                    var data = _provider.ReadData();
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
                    if (_provider.Status != HardwareInfoProviderStatus.Started) return float.NaN;
                    try
                    {
                        _provider.EnsureFileMapping();
                    }
                    catch { return float.NaN; }
                    var data = _provider.ReadData();
                    return data.fMultipliers[_index] * data.fFSBSpeed;
                }
            }
        }
        private sealed class CoreMultiplierAccessor : IAccessor
        {
            CoreTempCpuProvider _provider;
            int _index;
            public CoreMultiplierAccessor(CoreTempCpuProvider provider, int index)
            {
                _provider = provider;
                _index = index;
            }
            public float Value
            {
                get
                {
                    if (_provider.Status != HardwareInfoProviderStatus.Started) return float.NaN;
                    try
                    {
                        _provider.EnsureFileMapping();
                    }
                    catch { return float.NaN; }
                    var data = _provider.ReadData();
                    return data.fMultipliers[_index];
                }
            }
        }
        bool _started = false;
        IntPtr _sharedHandle = IntPtr.Zero;
        IntPtr _sharedPtr = IntPtr.Zero;
        List<IAccessor> accessors = new List<IAccessor>();

        private unsafe CoreTempSharedDataEx ReadData()
        {
            return Unsafe.Read<CoreTempSharedDataEx>((void*)_sharedPtr);
        }

        protected override HardwareInfoProviderStatus GetState()
        {
            return _sharedHandle != IntPtr.Zero ? HardwareInfoProviderStatus.Started : HardwareInfoProviderStatus.Stopped;
        }
        protected override string GetDisplayName()
        {
            return "CoreTemp CPU Provider";
        }
        protected override string GetIdentifier()
        {
            return "coretemp";
        }
        internal void EnsureFileMapping()
        {
            if (_sharedHandle != IntPtr.Zero)
            {
                return;
            }
            _sharedHandle = OpenFileMapping(FILE_MAP_READ, true, "CoreTempMappingObjectEx");
            if (_sharedHandle != IntPtr.Zero)
            {
                int size = Unsafe.SizeOf<CoreTempSharedDataEx>();
                // Real runtime guard (the old Debug.Assert compiled out under
                // release/AOT, so a layout regression would have been silent).
                if (size != 4740)
                {
                    throw new InvalidOperationException(
                        $"CoreTempSharedDataEx is {size} bytes, expected 4740 - struct layout is wrong.");
                }
                _sharedPtr = MapViewOfFile(_sharedHandle, FILE_MAP_READ, 0, 0, size);
            }
            if (_sharedPtr == IntPtr.Zero)
            {
                CloseHandle(_sharedHandle);
                _sharedHandle = IntPtr.Zero;
                throw new SystemException("Unable to map view");

            }
            var data = ReadData();
            Publish($"/cpu/clock", "MHz", new Func<float>(() => {
                if (Status != HardwareInfoProviderStatus.Started) return float.NaN;
                var data = ReadData();
                return data.fCPUSpeed;
            }));
            Publish($"/bus/clock", "MHz", new Func<float>(() => {
                if (Status != HardwareInfoProviderStatus.Started) return float.NaN;
                var data = ReadData();
                return data.fFSBSpeed;
            }));
            Publish($"/cpu/multiplier", "x", new Func<float>(() => {
                if (Status != HardwareInfoProviderStatus.Started) return float.NaN;
                var data = ReadData();
                return data.fMultiplier;
            }));

            int coreIndex = 0;
            for (var i = 0; i < data.uiCPUCnt; i++)
            {
                var tjmaxAcc = new CpuTJMaxAccessor(this, i);
                Publish($"/cpu/{i}/tjmax", "°", new Func<float>(() => {
                    return tjmaxAcc.Value;
                }));
                for (int j = 0; j < data.uiCoreCnt; j++)
                {
                    var loadAcc = new CoreLoadAccessor(this, coreIndex);
                    Publish($"/cpu/{i}/core/{j}/load", "%", new Func<float>(() => {
                        return loadAcc.Value;
                    }));
                    var multAcc = new CoreMultiplierAccessor(this, coreIndex);
                    Publish($"/cpu/{i}/core/{j}/multiplier", "x", new Func<float>(() => {
                        return multAcc.Value;
                    }));
                    var tempAcc = new CoreTemperatureAccessor(this, i, coreIndex);
                    Publish($"/cpu/{i}/core/{j}/temperature", "°", new Func<float>(() => {
                        return tempAcc.Value;
                    }));
                    var freqAcc = new CoreFrequencyAccessor(this, coreIndex);
                    Publish($"/cpu/{i}/core/{j}/clock", "MHz", new Func<float>(() => {
                        return freqAcc.Value;
                    }));
                    ++coreIndex;
                }
            }
        }
        protected override void OnStart()
        {
            if (_started) return;
            _started = true;
            try
            {
                EnsureFileMapping();
            }
            catch { return; }
        }
        protected override void OnStop()
        {
            _started = false;
            if (_sharedHandle == IntPtr.Zero)
            {
                return;
            }
            accessors.Clear();
            UnmapViewOfFile(_sharedPtr);
            _sharedPtr = IntPtr.Zero;
            CloseHandle(_sharedHandle);
            _sharedHandle = IntPtr.Zero;
        }
        protected override string GetDescription()
        {
            return "Provides CPU core temperature, load and frequency information via the the CoreTemp application. https://www.alcpu.com/CoreTemp/";
        }
        private static readonly object _allCoreTempsKey = new object();
        private static readonly object _allCoreLoadsKey = new object();
        private static readonly object _allCoreClocksKey = new object();
        private static readonly object _maxCpuClockKey = new object();
        private static readonly object _maxSafeCpuTempsKey = new object();
        public override HardwareInfoSuggestion[] GetSuggestions(HardwareInfoSuggestionContext context)
        {
            if (context.Expression == null && context.ParseException == null)
            {
                HardwareInfoSuggestion[] result = [
                    new HardwareInfoSuggestion(_allCoreTempsKey,"All core temperatures","Retrieves the temperatures for every core across all CPUs in degrees Celsius","Core"),
                    new HardwareInfoSuggestion(_allCoreLoadsKey,"All core loads","Retrieves the load percentage for every core across all CPUs as percentages","Core"),
                    new HardwareInfoSuggestion(_allCoreClocksKey,"All core frequencies","Retrieves the clock frequencies for every core across all CPUs in MHz","Core"),
                    new HardwareInfoSuggestion(_maxCpuClockKey,"Maximum CPU frequency","Retrieves the maximum frequency for the CPUs in MHz",null),
                    new HardwareInfoSuggestion(_maxSafeCpuTempsKey,"Maximum safe CPU temperatures","Retrieves the maximum safe temperature across all CPUs in degrees Celsius",null),
                ];
                return result;
            }
            return base.GetSuggestions(context);
        }
        public override HardwareInfoExpression? ApplySuggestion(HardwareInfoSuggestionContext context, object key)
        {
            if (context.Expression == null && context.ParseException == null)
            {
                if (key == _allCoreTempsKey)
                {
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
                    return HardwareInfoExpression.Parse("/coretemp/cpu/clock");
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