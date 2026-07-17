using Ati.Adl;

using System.Runtime.InteropServices;
namespace HWKit
{
    public class AmdAdlGpuProvider : HardwareInfoProviderBase
    {
        protected override string GetDisplayName()
        {
            return "AMD ADL Provider";
        }
        protected override string GetIdentifier()
        {
            return "amd_adl";
        }
        class LogAccessor
        {
            // In ADLPMLogData, ulValues starts at byte offset 16:
            //   uint ulVersion (4) + uint ulActiveSampleRate (4) + ulong ulLastUpdated (8).
            // It is really uint[256][2], i.e. 256 interleaved [sensorId, value] pairs.
            const int ValuesOffset = 16;
            const int PairCount = 256;

            readonly IntPtr _loggingAddress;
            public LogAccessor(IntPtr loggingAddrss)
            {
                _loggingAddress = loggingAddrss;
            }

            // Reads straight from the driver's live buffer using reflection-free,
            // NativeAOT-safe Marshal.ReadInt32 calls -- no Marshal.PtrToStructure
            // and no per-read copy of the whole ~3 KB struct.
            float ReadSensor(ADL_PMLOG_SENSORS sensor)
            {
                for (int i = 0; i < PairCount; i++)
                {
                    int off = ValuesOffset + i * 2 * sizeof(uint);
                    uint id = (uint)Marshal.ReadInt32(_loggingAddress, off);
                    if (id == (uint)sensor)
                        return (uint)Marshal.ReadInt32(_loggingAddress, off + sizeof(uint));
                }
                return float.NaN; // not found / unsupported
            }

            public float Clock => ReadSensor(ADL_PMLOG_SENSORS.ADL_PMLOG_CLK_GFXCLK);
            public float Temperature => ReadSensor(ADL_PMLOG_SENSORS.ADL_PMLOG_TEMPERATURE_GFX);
            public float FanSpeed => ReadSensor(ADL_PMLOG_SENSORS.ADL_PMLOG_FAN_RPM);
            public float FanLoad => ReadSensor(ADL_PMLOG_SENSORS.ADL_PMLOG_FAN_PERCENTAGE);
            public float Load => ReadSensor(ADL_PMLOG_SENSORS.ADL_PMLOG_INFO_ACTIVITY_GFX);
            public float RamLoad => ReadSensor(ADL_PMLOG_SENSORS.ADL_PMLOG_INFO_ACTIVITY_MEM);
            public float CpuTemperature => ReadSensor(ADL_PMLOG_SENSORS.ADL_PMLOG_TEMPERATURE_CPU);
            public float RamTemperature => ReadSensor(ADL_PMLOG_SENSORS.ADL_PMLOG_TEMPERATURE_MEM);
            public float PackageTemperature => ReadSensor(ADL_PMLOG_SENSORS.ADL_PMLOG_TEMPERATURE_SOC);
            public float RamClock => ReadSensor(ADL_PMLOG_SENSORS.ADL_PMLOG_CLK_MEMCLK);
            public float SocClock => ReadSensor(ADL_PMLOG_SENSORS.ADL_PMLOG_CLK_SOCCLK);
            public float CpuClock => ReadSensor(ADL_PMLOG_SENSORS.ADL_PMLOG_CLK_CPUCLK);
        }
        void PublishPaths(int index, ref ADLPMLogSupportInfo support, IntPtr loggingAddress)
        {
            var accessor = new LogAccessor(loggingAddress);
            var isApu = false;
            var i = 0;
            while (support.usSensors[i] != 0)
            {
                if (support.usSensors[i] == (uint)ADL_PMLOG_SENSORS.ADL_PMLOG_CLK_CPUCLK)
                {
                    isApu = true; break;
                }
                ++i;
            }
            // Sticky across adapters: one APU anywhere in the machine is enough to
            // make the CPU rows meaningful and to switch suggestions to categorized.
            _isApu |= isApu;
            i = 0;
            while (support.usSensors[i] != 0)
            {
                switch ((ADL_PMLOG_SENSORS)support.usSensors[i])
                {
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_CLK_GFXCLK:
                        Publish($"/gpu/{index}/clock", "MHz", () => accessor.Clock);
                        break;
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_CLK_MEMCLK:
                        Publish($"/gpu/{index}/clock/ram", "MHz", () => accessor.RamClock);
                        if (isApu)
                        {
                            Publish($"/cpu/{index}/clock/ram", "MHz", () => accessor.RamClock);
                        }
                        break;
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_CLK_SOCCLK:
                        Publish($"/soc/{index}/clock", "MHz", () => accessor.SocClock);
                        break;
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_TEMPERATURE_MEM:
                        Publish($"/gpu/{index}/vram/temperature", "°", () => accessor.RamTemperature);
                        if (isApu)
                        {
                            Publish($"/cpu/{index}/ram/temperature", "°", () => accessor.RamTemperature);
                        }
                        break;
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_FAN_RPM:
                        Publish($"/gpu/{index}/fan/speed", "RPM", () => accessor.FanSpeed);
                        if (isApu)
                        {
                            Publish($"/cpu/{index}/fan/speed", "RPM", () => accessor.FanSpeed);
                        }
                        break;
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_FAN_PERCENTAGE:
                        Publish($"/gpu/{index}/fan/load", "%", () => accessor.FanLoad);
                        if (isApu)
                        {
                            Publish($"/cpu/{index}/fan/load", "%", () => accessor.FanLoad);
                        }
                        break;
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_INFO_ACTIVITY_GFX:
                        Publish($"/gpu/{index}/load", "%", () => accessor.Load);
                        break;
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_INFO_ACTIVITY_MEM:
                        Publish($"/gpu/{index}/vram/load", "%", () => accessor.RamLoad);
                        break;
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_TEMPERATURE_GFX:
                        Publish($"/gpu/{index}/temperature", "°", () => accessor.Temperature);
                        break;
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_TEMPERATURE_SOC:
                        Publish($"/soc/temperature", "°", () => accessor.PackageTemperature);
                        break;
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_TEMPERATURE_CPU:
                        Publish($"/cpu/temperature", "°", () => accessor.CpuTemperature);
                        break;
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_CLK_CPUCLK:
                        Publish($"/cpu/{index}/clock", "MHz", () => accessor.CpuClock);
                        break;

                }
                ++i;
            }
        }
        private bool _started;
        private bool _hasAmd = false;
        private volatile bool _isApu;
        private List<(int, uint)> _devices = new List<(int, uint)>();
        private bool DoStart()
        {
            int ADLRet = -1;
            int NumberOfAdapters = 0;
            _devices.Clear();
            _isApu = false;
            if (null != ADL.ADL_Main_Control_Create)
            {
                // Second parameter is 1: Get only the present adapters
                ADLRet = ADL.ADL_Main_Control_Create(ADL.ADL_Main_Memory_Alloc, 1);
            }
            if (ADL.ADL_SUCCESS == ADLRet)
            {
                if (null != ADL.ADL_Adapter_NumberOfAdapters_Get)
                {
                    ADL.ADL_Adapter_NumberOfAdapters_Get(ref NumberOfAdapters);
                }

                if (0 < NumberOfAdapters)
                {
                    // Get OS adpater info from ADL
                    ADLAdapterInfoArray OSAdapterInfoData;
                    OSAdapterInfoData = new ADLAdapterInfoArray();

                    if (null != ADL.ADL_Adapter_AdapterInfo_Get)
                    {
                        IntPtr AdapterBuffer = IntPtr.Zero;
                        // SizeOf<T>() / PtrToStructure<T>() are NativeAOT-safe here
                        // because ADLAdapterInfoArray is now blittable. The driver
                        // fills the buffer, so no StructureToPtr pre-init is needed.
                        int size = Marshal.SizeOf<ADLAdapterInfoArray>();
                        AdapterBuffer = Marshal.AllocCoTaskMem(size);

                        if (null != ADL.ADL_Adapter_AdapterInfo_Get)
                        {
                            ADLRet = ADL.ADL_Adapter_AdapterInfo_Get(AdapterBuffer, size);
                            if (ADL.ADL_SUCCESS == ADLRet)
                            {
                                OSAdapterInfoData = Marshal.PtrToStructure<ADLAdapterInfoArray>(AdapterBuffer);

                                for (int i = 0; i < NumberOfAdapters; i++)
                                {
                                    ADLPMLogStartInput logStartInput = default;
                                    ADLPMLogStartOutput logStartOutput = default;
                                    uint hDevice = 0;
                                    ADLPMLogSupportInfo support = default;
                                    ADLRet = -1;
                                    if (null != ADL.ADL2_Device_PMLog_Device_Create)
                                    {
                                        ADLRet = ADL.ADL2_Device_PMLog_Device_Create(IntPtr.Zero, OSAdapterInfoData.ADLAdapterInfo[i].AdapterIndex, out hDevice);
                                    }
                                    if (ADL.ADL_SUCCESS == ADLRet)
                                    {
                                        _devices.Add((OSAdapterInfoData.ADLAdapterInfo[i].AdapterIndex, hDevice));
                                        ADLRet = -1;

                                        if (null != ADL.ADL2_Adapter_PMLog_Support_Get)
                                        {
                                            ADLRet = ADL.ADL2_Adapter_PMLog_Support_Get(IntPtr.Zero, OSAdapterInfoData.ADLAdapterInfo[i].AdapterIndex, out support);
                                        }
                                        if (ADL.ADL_SUCCESS == ADLRet)
                                        {
                                            int j = 0;
                                            while (support.usSensors[j] != 0)
                                            {
                                                logStartInput.usSensors[j] = support.usSensors[j];
                                                j++;
                                            }
                                            logStartInput.usSensors[j] = 0;
                                            logStartInput.ulSampleRate = 100;
                                            ADLRet = -1;
                                            if (null != ADL.ADL2_Adapter_PMLog_Start)
                                            {
                                                ADLRet = ADL.ADL2_Adapter_PMLog_Start(IntPtr.Zero, OSAdapterInfoData.ADLAdapterInfo[i].AdapterIndex, ref logStartInput, out logStartOutput, hDevice);
                                            }
                                            if (ADL.ADL_SUCCESS != ADLRet)
                                            {
                                                throw new SystemException(ADLRet.ToString());
                                            }
                                            PublishPaths(OSAdapterInfoData.ADLAdapterInfo[i].AdapterIndex, ref support, logStartOutput.pLoggingAddress);
                                        }
                                        else
                                        {
                                            throw new SystemException(ADLRet.ToString());
                                        }
                                    }
                                    else
                                    {
                                        throw new SystemException(ADLRet.ToString());
                                    }
                                }
                            }
                            else
                            {
                                throw new SystemException(ADLRet.ToString());
                            }
                        }
                        // Release the memory for the AdapterInfo structure
                        if (IntPtr.Zero != AdapterBuffer)
                            Marshal.FreeCoTaskMem(AdapterBuffer);
                    }
                }
            }
            else
            {
                // Console.WriteLine("ADL_Main_Control_Create() returned error code " + ADLRet.ToString());
                // Console.WriteLine("\nCheck if ADL is properly installed!\n");
                return false;
            }
            return true;
        }
        protected override HardwareInfoProviderStatus GetState()
        {

            return _started ? HardwareInfoProviderStatus.Started : HardwareInfoProviderStatus.Stopped;
        }
        protected override void OnStart()
        {
            if (_started)
            {
                return;
            }
            _hasAmd = DoStart();
            _started = true;
        }
        protected override void OnStop()
        {
            if (!_started)
            {
                return;
            }
            for (var i = 0; i < _devices.Count; ++i)
            {
                var t = _devices[i];
                if (null != ADL.ADL2_Adapter_PMLog_Stop)
                {
                    ADL.ADL2_Adapter_PMLog_Stop(IntPtr.Zero, t.Item1, t.Item2);
                }
                if (null != ADL.ADL2_Device_PMLog_Device_Destroy)
                {
                    ADL.ADL2_Device_PMLog_Device_Destroy(IntPtr.Zero, t.Item2);
                }
            }
            _devices.Clear();
            _isApu = false;
            _started = false; // even on error this should go false.
            if (null != ADL.ADL_Main_Control_Destroy)
            {
                ADL.ADL_Main_Control_Destroy();
            }

        }
        protected override string GetDescription()
        {
            return "Provides information for AMD APUs or installed AMD GPUs";
        }

        // Suggestion table. Each row gets a fresh identity object as its key; the
        // expression is looked up by that identity in ApplySuggestion. Add a row here
        // and both overrides pick it up -- there is no second place to edit.
        //
        // Category is authored here but is *advisory*: it only survives on an APU
        // system, where GPU/CPU/SOC is a meaningful split. On a discrete-only board
        // every row is presented uncategorized (null), matching the Nvidia provider.
        // Rows tagged "CPU" describe paths that only exist behind an APU's
        // ADL_PMLOG_CLK_CPUCLK / TEMPERATURE_CPU sensors, so they are dropped
        // entirely when no APU is present. GPU and SOC rows are always offered.
        sealed record SuggestionDef(object Key, string Title, string Description, string Expression, string? Category);

        static readonly SuggestionDef[] _suggestionDefs = BuildSuggestions();

        // Two prebuilt public views over the same defs; the only difference is whether
        // Category is carried through and whether the CPU rows survive the filter.
        static readonly HardwareInfoSuggestion[] _apuSuggestionList =
            Array.ConvertAll(_suggestionDefs, d => new HardwareInfoSuggestion(d.Key, d.Title, d.Description, d.Category));
        static readonly HardwareInfoSuggestion[] _gpuOnlySuggestionList =
            Array.ConvertAll(
                Array.FindAll(_suggestionDefs, d => d.Category != "CPU"),
                d => new HardwareInfoSuggestion(d.Key, d.Title, d.Description, null));

        static SuggestionDef[] BuildSuggestions()
        {
            static SuggestionDef D(string? cat, string title, string desc, string expr)
                => new(new object(), title, desc, expr, cat);
            return
            [
                // ---- GPU ----
                D("GPU","All GPU temperatures",
                  "Retrieves the temperatures for every AMD GPU in degrees Celsius",
                  "'^/amd_adl/gpu/[0-9]+/temperature$'"),
                D("GPU","All GPU loads",
                  "Retrieves the load for every AMD GPU as percentages",
                  "'^/amd_adl/gpu/[0-9]+/load$'"),
                D("GPU","All GPU frequencies",
                  "Retrieves the graphics clock frequencies for every AMD GPU in MHz",
                  "'^/amd_adl/gpu/[0-9]+/clock$'"),
                D("GPU","All VRAM loads",
                  "Retrieves the load on the VRAM for all AMD GPUs as percentages",
                  "'^/amd_adl/gpu/[0-9]+/vram/load$'"),
                D("GPU","All VRAM clocks",
                  "Retrieves the VRAM clocks for all AMD GPUs in MHz",
                  "'^/amd_adl/gpu/[0-9]+/clock/ram$'"),
                D("GPU","All VRAM temperatures",
                  "Retrieves the VRAM temperatures for all AMD GPUs in degrees Celsius",
                  "'^/amd_adl/gpu/[0-9]+/vram/temperature$'"),
                D("GPU","All fan loads",
                  "Retrieves the fan load for every AMD GPU as percentages",
                  "'^/amd_adl/gpu/[0-9]+/fan/load$'"),
                D("GPU","All fan speeds",
                  "Retrieves the fan speed for every AMD GPU in RPM",
                  "'^/amd_adl/gpu/[0-9]+/fan/speed$'"),
                D("GPU","Hottest GPU",
                  "Retrieves the temperature of the hottest AMD GPU in degrees Celsius",
                  "max('^/amd_adl/gpu/[0-9]+/temperature$')"),
                D("GPU","Busiest GPU",
                  "Retrieves the load of the most heavily loaded AMD GPU as a percentage",
                  "max('^/amd_adl/gpu/[0-9]+/load$')"),

                // ---- SOC ----
                // Published on discrete boards as well as APUs, so never filtered out.
                D("SOC","All SOC clocks",
                  "Retrieves the SOC clocks for all AMD devices in MHz",
                  "'^/amd_adl/soc/[0-9]+/clock$'"),
                D("SOC","SOC temperature",
                  "Retrieves the temperature of the SOC in degrees Celsius",
                  "/amd_adl/soc/temperature"),

                // ---- CPU (APU only) ----
                D("CPU","CPU temperature",
                  "Retrieves the temperature of the APU's CPU in degrees Celsius",
                  "/amd_adl/cpu/temperature"),
                D("CPU","All CPU frequencies",
                  "Retrieves the core clock frequencies for every AMD APU in MHz",
                  "'^/amd_adl/cpu/[0-9]+/clock$'"),
                D("CPU","All CPU memory clocks",
                  "Retrieves the memory clocks for every AMD APU in MHz",
                  "'^/amd_adl/cpu/[0-9]+/clock/ram$'"),
                D("CPU","All CPU memory temperatures",
                  "Retrieves the memory temperatures for every AMD APU in degrees Celsius",
                  "'^/amd_adl/cpu/[0-9]+/ram/temperature$'"),
                D("CPU","All CPU fan loads",
                  "Retrieves the fan load for every AMD APU as percentages",
                  "'^/amd_adl/cpu/[0-9]+/fan/load$'"),
                D("CPU","All CPU fan speeds",
                  "Retrieves the fan speed for every AMD APU in RPM",
                  "'^/amd_adl/cpu/[0-9]+/fan/speed$'"),
            ];
        }

        public override HardwareInfoSuggestion[] GetSuggestions(HardwareInfoSuggestionContext context)
        {

            if ((context.Expression == null || context.Expression.IsEmpty) && context.ParseException == null)
            {
                if (_hasAmd)
                {
                    return _isApu ? _apuSuggestionList : _gpuOnlySuggestionList;
                }
            }
            return base.GetSuggestions(context);
        }

        public override HardwareInfoExpression? ApplySuggestion(HardwareInfoSuggestionContext context, object key)
        {
            if ((context.Expression == null || context.Expression.IsEmpty) && context.ParseException == null)
            {
                if (_hasAmd)
                {
                    foreach (var d in _suggestionDefs)
                        if (ReferenceEquals(d.Key, key))
                            return HardwareInfoExpression.Parse(d.Expression);
                }
            }
            return base.ApplySuggestion(context, key);
        }
    }
}