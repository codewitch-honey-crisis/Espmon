using System.Runtime.InteropServices;


namespace Nvidia.Nvml
{
    [StructLayout(LayoutKind.Sequential)]
    public struct NvmlAccountingStats
    {
        public uint gpuUtilization;                //!< Percent of time over the process's lifetime during which one or more kernels was executing on the GPU.
                                                   //! Utilization stats just like returned by \ref nvmlDeviceGetUtilizationRates but for the life time of a
                                                   //! process (not just the last sample period).
                                                   //! Set to NVML_VALUE_NOT_AVAILABLE if nvmlDeviceGetUtilizationRates is not supported

        public uint memoryUtilization;             //!< Percent of time over the process's lifetime during which global (device) memory was being read or written.
                                                    //! Set to NVML_VALUE_NOT_AVAILABLE if nvmlDeviceGetUtilizationRates is not supported

        ulong maxMemoryUsage;          //!< Maximum total memory in bytes that was ever allocated by the process.
                                                    //! Set to NVML_VALUE_NOT_AVAILABLE if nvmlProcessInfo_t->usedGpuMemory is not supported


        ulong time;                    //!< Amount of time in ms during which the compute context was active. The time is reported as 0 if
                                                    //!< the process is not terminated

        ulong startTime;               //!< CPU Timestamp in usec representing start time for the process

        uint isRunning;                     //!< Flag to represent if the process is running (1 for running, 0 for terminated)

        uint reserved1;                   //!< Reserved for future use
        uint reserved2;                   //!< Reserved for future use
        uint reserved3;                   //!< Reserved for future use
        uint reserved4;                   //!< Reserved for future use
        uint reserved5;                   //!< Reserved for future use
    }
}
