using System.Runtime.InteropServices;
namespace Nvidia.Nvml
{
    internal enum NvmlValueType
    {
        NVML_VALUE_TYPE_DOUBLE = 0,
        NVML_VALUE_TYPE_UNSIGNED_INT = 1,
        NVML_VALUE_TYPE_UNSIGNED_LONG = 2,
        NVML_VALUE_TYPE_UNSIGNED_LONG_LONG = 3,
        NVML_VALUE_TYPE_SIGNED_LONG_LONG = 4,
        NVML_VALUE_TYPE_SIGNED_INT = 5,
        NVML_VALUE_TYPE_UNSIGNED_SHORT = 6

        // Keep this last
        // NVML_VALUE_TYPE_COUNT
    }
    [StructLayout(LayoutKind.Explicit)]
    internal struct NvmlValue
    {
        [FieldOffset(0)] public double dVal;                    //!< If the value is double
        [FieldOffset(0)] public int siVal;                      //!< If the value is signed int
        [FieldOffset(0)] public uint uiVal;             //!< If the value is unsigned int
        [FieldOffset(0)] public uint ulVal;            //!< If the value is unsigned long
        [FieldOffset(0)] public ulong ullVal;      //!< If the value is unsigned long long
        [FieldOffset(0)] public long sllVal;        //!< If the value is signed long long
        [FieldOffset(0)] public ushort usVal;           //!< If the value is unsigned short
    }

}
