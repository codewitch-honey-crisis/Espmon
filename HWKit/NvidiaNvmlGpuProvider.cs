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
            return "Nvidia NVML GPU Provider";
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
            try
            {
                NvGpu.NvmlInitV2();
                var deviceCount = (int)NvGpu.NvmlDeviceGetCountV2();

                for (var i = 0; i < deviceCount; i++)
                {
                    var handle = NvGpu.NvmlDeviceGetHandleByIndex((uint)i);
                    var accessor = new DeviceAccessor(handle);
                    Publish($"/gpu/{i}/temperature", "°", () => _started ? accessor.Temperature : float.NaN);
                    Publish($"/gpu/{i}/load", "%", () => _started ? accessor.Load : float.NaN);
                    Publish($"/gpu/{i}/clock", "MHz", () => _started ? accessor.Frequency : float.NaN);
                    Publish($"/gpu/{i}/vram/load/", "%", () => _started ? accessor.VramLoad : float.NaN);
                    Publish($"/gpu/{i}/vram/clock", "MHz", () => _started ? accessor.VramFrequency : float.NaN);
                    Publish($"/gpu/{i}/sm/clock", "MHz", () => _started ? accessor.SMFrequency : float.NaN);
                    Publish($"/gpu/{i}/fan/load", "%", () => _started ? accessor.FanLoad : float.NaN);

                }
            }
            catch
            {
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
            try
            {
                NvGpu.NvmlShutdown();
            }
            catch
            {

            }
            
        }
        protected override string GetDescription()
        {
            return "Provides various information about installed Nvidia GPUs";
        }
        private static readonly object _allGpuTempsKey = new object();
        private static readonly object _allGpuLoadsKey = new object();
        private static readonly object _allGpuClocksKey = new object();
        private static readonly object _allVramLoadsKey = new object();
        private static readonly object _allVramClocksKey = new object();
        private static readonly object _allGpuSMClocksKey = new object();
        private static readonly object _allGpuFanLoadsKey = new object();
        public override HardwareInfoSuggestion[] GetSuggestions(HardwareInfoSuggestionContext context)
        {
            if ((context.Expression == null || context.Expression.IsEmpty) && context.ParseException == null)
            {
                HardwareInfoSuggestion[] result = [
                    new HardwareInfoSuggestion(_allGpuTempsKey,"All GPU temperatures","Retrieves the temperatures for every Nvidia GPU in degrees Celsius",null),
                    new HardwareInfoSuggestion(_allGpuLoadsKey,"All GPU loads","Retrieves the load for every Nvidia GPU as percentages",null),
                    new HardwareInfoSuggestion(_allGpuClocksKey,"All GPU frequencies","Retrieves the clock frequencies for every Nvidia GPU in MHz",null),
                    new HardwareInfoSuggestion(_allVramLoadsKey,"All VRAM loads","Retrieves the load on the VRAM for all Nvidia GPUs as percentages",null),
                    new HardwareInfoSuggestion(_allVramClocksKey,"All VRAM clocks","Retrieves the VRAM clocks for all Nvidia GPUs in MHz",null),
                    new HardwareInfoSuggestion(_allGpuSMClocksKey,"All SM clocks","Retrieves the SM clocks for all Nvidia GPUs in MHz",null),
                    new HardwareInfoSuggestion(_allGpuFanLoadsKey,"All fan loads","Retrieves the fan load for every Nvidia GPU as percentages",null)
                ];
                return result;
            }
            return base.GetSuggestions(context);
        }
        public override HardwareInfoExpression? ApplySuggestion(HardwareInfoSuggestionContext context, object key)
        {
            if ((context.Expression == null || context.Expression.IsEmpty) && context.ParseException == null)
            {
                if (key == _allGpuTempsKey)
                {
                    return HardwareInfoExpression.Parse("'^/nvidia_nvml/gpu/[0-9]+/temperature$'");
                }
                if (key == _allGpuLoadsKey)
                {
                    return HardwareInfoExpression.Parse("'^/nvidia_nvml/gpu/[0-9]+/load$'");
                }
                if (key == _allGpuClocksKey)
                {
                    return HardwareInfoExpression.Parse("'^/nvidia_nvml/gpu/[0-9]+/clock$'");
                }
                if (key == _allVramLoadsKey)
                {
                    return HardwareInfoExpression.Parse("'^/nvidia_nvml/gpu/[0-9]+/vram/load$'");
                }
                if (key == _allVramClocksKey)
                {
                    return HardwareInfoExpression.Parse("'^/nvidia_nvml/gpu/[0-9]+/vram/clock$'");
                }
                if (key == _allGpuSMClocksKey)
                {
                    return HardwareInfoExpression.Parse("'^/nvidia_nvml/gpu/[0-9]+/sm/clock$'");
                }
                if (key == _allGpuFanLoadsKey)
                {
                    return HardwareInfoExpression.Parse("'^/nvidia_nvml/gpu/[0-9]+/fan/load$'");
                }

            }
            return base.ApplySuggestion(context, key);
        }
    }
}
