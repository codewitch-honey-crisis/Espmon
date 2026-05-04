using Nvidia.Nvml;
namespace HWKit
{
    public class NvidiaNvmlGpuProvider : HardwareInfoProviderBase
    {
        private sealed class DeviceAccessor
        {
            IntPtr _handle;
            public DeviceAccessor(IntPtr handle)
            {
                _handle = handle;
            }
            public float Temperature => NvGpu.NvmlDeviceGetTemperature(_handle, NvmlTemperatureSensor.NVML_TEMPERATURE_GPU);
            public float Load => NvGpu.NvmlDeviceGetUtilizationRates(_handle).gpuUtilization;
            public float Frequency => NvGpu.NvmlDeviceGetClock(_handle, NvmlClockType.NVML_CLOCK_GRAPHICS, NvmlClockId.NVML_CLOCK_ID_CURRENT);
            public float VramLoad => NvGpu.NvmlDeviceGetUtilizationRates(_handle).memoryUtilization;
            public float VramFrequency => NvGpu.NvmlDeviceGetClock(_handle, NvmlClockType.NVML_CLOCK_MEM, NvmlClockId.NVML_CLOCK_ID_CURRENT);
            public float SMFrequency => NvGpu.NvmlDeviceGetClock(_handle, NvmlClockType.NVML_CLOCK_SM, NvmlClockId.NVML_CLOCK_ID_CURRENT);
            public float FanLoad => NvGpu.NvmlDeviceGetFanSpeed(_handle);
        }
      
        private bool _started;
        protected override string GetDisplayName()
        {
            return "NVidia NVML GPU Provider";
        }
        protected override string GetIdentifier()
        {
            return "nvidia_nvml";
        }
        protected override void OnStart()
        {
            if(_started)
            {
                return;
            }
            NvGpu.NvmlInitV2();
            var deviceCount = (int)NvGpu.NvmlDeviceGetCountV2();

            for (var i = 0; i < deviceCount; i++)
            {
                var handle = NvGpu.NvmlDeviceGetHandleByIndex((uint)i);
                var accessor = new DeviceAccessor(handle);
                Publish($"/gpu/{i}/temperature", "°", () => _started?accessor.Temperature:float.NaN);
                Publish($"/gpu/{i}/load","%", () => _started?accessor.Load:float.NaN);
                Publish($"/gpu/{i}/clock","MHz", () => _started? accessor.Frequency : float.NaN);
                Publish($"/gpu/{i}/load/vram","%", () => _started ? accessor.VramLoad : float.NaN);
                Publish($"/gpu/{i}/clock/vram","MHz", () => _started ? accessor.VramFrequency : float.NaN);
                Publish($"/gpu/{i}/clock/sm", "MHz", () => _started ? accessor.SMFrequency : float.NaN);
                Publish($"/gpu/{i}/load/fan", "%",() => _started ? accessor.FanLoad : float.NaN);
                
            }
            _started = true;
        }
        protected override HardwareInfoProviderStatus GetState()
        {

            return _started ? HardwareInfoProviderStatus.Started : HardwareInfoProviderStatus.Stopped;
        }
        protected override void OnStop()
        {
            if (!_started)
            {
                return;
            }
            _started = false; // even on error this should go false.
            NvGpu.NvmlShutdown();
            
        }
        protected override string GetDescription()
        {
            return "Provides various information about installed NVidia GPUs";
        }
    }
}
