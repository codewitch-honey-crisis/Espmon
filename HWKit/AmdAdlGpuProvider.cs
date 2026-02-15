using Ati.Adl;

using System.Runtime.InteropServices;
namespace HWKit
{
    public class AmdAdlGpuProvider: HardwareInfoProviderBase
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
            static float ReadSensor(uint[] ulValues, ADL_PMLOG_SENSORS sensor)
            {
                // ulValues is interleaved: [sensorId, value, sensorId, value, ...]
                for (int i = 0; i < ulValues.Length - 1; i += 2)
                {
                    if (ulValues[i] == (uint)sensor)
                        return ulValues[i + 1];
                }
                return float.NaN; // not found / unsupported
            }
            IntPtr _loggingAddress;
            public LogAccessor(IntPtr loggingAddrss)
            {
                _loggingAddress = loggingAddrss;

            }
            ADLPMLogData GetData()
            {
                return Marshal.PtrToStructure<ADLPMLogData>(_loggingAddress);
            }
            public float Clock
            {
                get
                {
                    var data = GetData();
                    return ReadSensor(data.ulValues, ADL_PMLOG_SENSORS.ADL_PMLOG_CLK_GFXCLK);
                }
            }
            public float Temperature
            {
                get
                {
                    var data = GetData();
                    return ReadSensor(data.ulValues, ADL_PMLOG_SENSORS.ADL_PMLOG_TEMPERATURE_GFX);
                }
            }
            public float FanSpeed
            {
                get
                {
                    var data = GetData();
                    return ReadSensor(data.ulValues, ADL_PMLOG_SENSORS.ADL_PMLOG_FAN_RPM);
                }
            }

            public float FanLoad
            {
                get
                {
                    var data = GetData();
                    return ReadSensor(data.ulValues, ADL_PMLOG_SENSORS.ADL_PMLOG_FAN_PERCENTAGE);
                }
            }
            public float Load
            {
                get
                {
                    var data = GetData();
                    return ReadSensor(data.ulValues, ADL_PMLOG_SENSORS.ADL_PMLOG_INFO_ACTIVITY_GFX);
                }
            }
            public float RamLoad
            {
                get
                {
                    var data = GetData();
                    return ReadSensor(data.ulValues, ADL_PMLOG_SENSORS.ADL_PMLOG_INFO_ACTIVITY_MEM);
                }
            }
            public float CpuTemperature
            {
                get
                {
                    var data = GetData();
                    return ReadSensor(data.ulValues, ADL_PMLOG_SENSORS.ADL_PMLOG_TEMPERATURE_CPU);
                }
            }
            public float RamTemperature
            {
                get
                {
                    var data = GetData();
                    return ReadSensor(data.ulValues, ADL_PMLOG_SENSORS.ADL_PMLOG_TEMPERATURE_MEM);
                }
            }
            public float PackageTemperature
            {
                get
                {
                    var data = GetData();
                    return ReadSensor(data.ulValues, ADL_PMLOG_SENSORS.ADL_PMLOG_TEMPERATURE_SOC);
                }
            }
            public float RamClock
            {
                get
                {
                    var data = GetData();
                    return ReadSensor(data.ulValues, ADL_PMLOG_SENSORS.ADL_PMLOG_CLK_MEMCLK);
                }
            }
            public float SocClock
            {
                get
                {
                    var data = GetData();
                    return ReadSensor(data.ulValues, ADL_PMLOG_SENSORS.ADL_PMLOG_CLK_SOCCLK);
                }
            }
            public float CpuClock
            {
                get
                {
                    var data = GetData();
                    return ReadSensor(data.ulValues, ADL_PMLOG_SENSORS.ADL_PMLOG_CLK_CPUCLK);
                }
            }
        }
        void PublishPaths(int index,ref ADLPMLogSupportInfo support, IntPtr loggingAddress)
        {
            var accessor = new LogAccessor(loggingAddress);
            var isApu = false;
            var i = 0;
            while (support.usSensors[i]!=0)
            {
                if (support.usSensors[i]==(uint)ADL_PMLOG_SENSORS.ADL_PMLOG_CLK_CPUCLK)
                {
                    isApu = true; break;
                }
                ++i;
            }
            i = 0;
            while (support.usSensors[i] != 0)
            {
                switch ((ADL_PMLOG_SENSORS)support.usSensors[i])
                {
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_CLK_GFXCLK:
                        Publish($"/gpu/{index}/clock","MHz", () => accessor.Clock);
                        break;
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_CLK_MEMCLK:
                        Publish($"/gpu/{index}/clock/ram", "MHz", () => accessor.RamClock);
                        if(isApu)
                        {
                            Publish($"/cpu/{index}/clock/ram", "MHz", () => accessor.RamClock);
                        }
                        break;
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_CLK_SOCCLK:
                        Publish($"/soc/{index}/clock", "MHz", () => accessor.SocClock);
                        break;
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_TEMPERATURE_MEM:
                        Publish($"/gpu/{index}/temperature/ram", "°",() => accessor.RamTemperature);
                        if (isApu)
                        {
                            Publish($"/cpu/{index}/temperature/ram", "°",() => accessor.RamTemperature);
                        }
                        break;
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_FAN_RPM:
                        Publish($"/gpu/{index}/speed/fan", "RPM",() => accessor.FanSpeed);
                        if (isApu)
                        {
                            Publish($"/cpu/{index}/speed/fan","RPM", () => accessor.FanSpeed);
                        }
                        break;
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_FAN_PERCENTAGE:
                        Publish($"/gpu/{index}/load/fan","%", () => accessor.FanLoad);
                        if (isApu)
                        {
                            Publish($"/cpu/{index}/load/fan","%", () => accessor.FanLoad);
                        }
                        break;
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_INFO_ACTIVITY_GFX:
                        Publish($"/gpu/{index}/load","%", () => accessor.Load);
                        break;
                    case ADL_PMLOG_SENSORS.ADL_PMLOG_INFO_ACTIVITY_MEM:
                        Publish($"/gpu/{index}/load/ram","%", () => accessor.RamLoad);
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
        private List<(int, uint)> _devices = new List<(int, uint)>();
        private bool DoStart()
        {
            int ADLRet = -1;
            int NumberOfAdapters = 0;
            _devices.Clear();
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
                        int size = Marshal.SizeOf(OSAdapterInfoData);
                        AdapterBuffer = Marshal.AllocCoTaskMem((int)size);
                        Marshal.StructureToPtr(OSAdapterInfoData, AdapterBuffer, false);

                        if (null != ADL.ADL_Adapter_AdapterInfo_Get)
                        {
                            ADLRet = ADL.ADL_Adapter_AdapterInfo_Get(AdapterBuffer, size);
                            if (ADL.ADL_SUCCESS == ADLRet)
                            {
                                OSAdapterInfoData = (ADLAdapterInfoArray)Marshal.PtrToStructure(AdapterBuffer, OSAdapterInfoData.GetType())!;
                                
                                for (int i = 0; i < NumberOfAdapters; i++)
                                {
                                    ADLPMLogStartInput logStartInput = default;
                                    logStartInput.usSensors = new ushort[256];
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
                                            while (support.usSensors![j] != 0)
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
                                            if(ADL.ADL_SUCCESS != ADLRet)
                                            {
                                                throw new SystemException(ADLRet.ToString());
                                            }
                                            PublishPaths(OSAdapterInfoData.ADLAdapterInfo[i].AdapterIndex, ref support, logStartOutput.pLoggingAddress);
                                        }
                                        else
                                        {
                                            throw new SystemException(ADLRet.ToString());
                                        }
                                    } else
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
        protected override HardwareInfoProviderState GetState()
        {

            return _started ? HardwareInfoProviderState.Started : HardwareInfoProviderState.Stopped;
        }
        protected override void OnStart()
        {
            if (_started)
            {
                return;
            }
            DoStart();
            _started = true;
        }
        protected override void OnStop()
        {
            if (!_started)
            {
                return;
            }
            for(var i = 0;i<_devices.Count;++i)
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
            _started = false; // even on error this should go false.
            if (null != ADL.ADL_Main_Control_Destroy)
            {
                ADL.ADL_Main_Control_Destroy();
            }

        }
    }
}
