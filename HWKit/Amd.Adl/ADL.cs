
/*******************************************************************************
 Copyright(c) 2008 - 2022 Advanced Micro Devices, Inc. All Rights Reserved.
 Copyright (c) 2002 - 2006  ATI Technologies Inc. All Rights Reserved.
 
 THIS CODE AND INFORMATION IS PROVIDED "AS IS" WITHOUT WARRANTY OF
 ANY KIND, EITHER EXPRESSED OR IMPLIED, INCLUDED BUT NOT LIMITED TO
 THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A 
 PARTICULAR PURPOSE.
 
 File:        ADL.cs
 
 Purpose:     Implements ADL interface 
 
 Description: Implements some of the methods defined in ADL interface.
              
 ********************************************************************************/

using Ati.Adl;

using System.Runtime.InteropServices;

using FARPROC = System.IntPtr;
using HMODULE = System.IntPtr;

namespace Ati.Adl
{
    enum ADL_PMLOG_SENSORS
    {
        ADL_SENSOR_MAXTYPES = 0,
        ADL_PMLOG_CLK_GFXCLK = 1,
        ADL_PMLOG_CLK_MEMCLK = 2,
        ADL_PMLOG_CLK_SOCCLK = 3,
        ADL_PMLOG_CLK_UVDCLK1 = 4,
        ADL_PMLOG_CLK_UVDCLK2 = 5,
        ADL_PMLOG_CLK_VCECLK = 6,
        ADL_PMLOG_CLK_VCNCLK = 7,
        ADL_PMLOG_TEMPERATURE_EDGE = 8,
        ADL_PMLOG_TEMPERATURE_MEM = 9,
        ADL_PMLOG_TEMPERATURE_VRVDDC = 10,
        ADL_PMLOG_TEMPERATURE_VRMVDD = 11,
        ADL_PMLOG_TEMPERATURE_LIQUID = 12,
        ADL_PMLOG_TEMPERATURE_PLX = 13,
        ADL_PMLOG_FAN_RPM = 14,
        ADL_PMLOG_FAN_PERCENTAGE = 15,
        ADL_PMLOG_SOC_VOLTAGE = 16,
        ADL_PMLOG_SOC_POWER = 17,
        ADL_PMLOG_SOC_CURRENT = 18,
        ADL_PMLOG_INFO_ACTIVITY_GFX = 19,
        ADL_PMLOG_INFO_ACTIVITY_MEM = 20,
        ADL_PMLOG_GFX_VOLTAGE = 21,
        ADL_PMLOG_MEM_VOLTAGE = 22,
        ADL_PMLOG_ASIC_POWER = 23,
        ADL_PMLOG_TEMPERATURE_VRSOC = 24,
        ADL_PMLOG_TEMPERATURE_VRMVDD0 = 25,
        ADL_PMLOG_TEMPERATURE_VRMVDD1 = 26,
        ADL_PMLOG_TEMPERATURE_HOTSPOT = 27,
        ADL_PMLOG_TEMPERATURE_GFX = 28,
        ADL_PMLOG_TEMPERATURE_SOC = 29,
        ADL_PMLOG_GFX_POWER = 30,
        ADL_PMLOG_GFX_CURRENT = 31,
        ADL_PMLOG_TEMPERATURE_CPU = 32,
        ADL_PMLOG_CPU_POWER = 33,
        ADL_PMLOG_CLK_CPUCLK = 34,
        ADL_PMLOG_THROTTLER_STATUS = 35,   // GFX
        ADL_PMLOG_CLK_VCN1CLK1 = 36,
        ADL_PMLOG_CLK_VCN1CLK2 = 37,
        ADL_PMLOG_SMART_POWERSHIFT_CPU = 38,
        ADL_PMLOG_SMART_POWERSHIFT_DGPU = 39,
        ADL_PMLOG_BUS_SPEED = 40,
        ADL_PMLOG_BUS_LANES = 41,
        ADL_PMLOG_TEMPERATURE_LIQUID0 = 42,
        ADL_PMLOG_TEMPERATURE_LIQUID1 = 43,
        ADL_PMLOG_CLK_FCLK = 44,
        ADL_PMLOG_THROTTLER_STATUS_CPU = 45,
        ADL_PMLOG_SSPAIRED_ASICPOWER = 46, // apuPower
        ADL_PMLOG_SSTOTAL_POWERLIMIT = 47, // Total Power limit
        ADL_PMLOG_SSAPU_POWERLIMIT = 48, // APU Power limit
        ADL_PMLOG_SSDGPU_POWERLIMIT = 49, // DGPU Power limit
        ADL_PMLOG_TEMPERATURE_HOTSPOT_GCD = 50,
        ADL_PMLOG_TEMPERATURE_HOTSPOT_MCD = 51,
        ADL_PMLOG_THROTTLE_PERCENTAGE_TEMP_GFX = 52,
        ADL_PMLOG_THROTTLE_PERCENTAGE_TEMP_MEM = 53,
        ADL_PMLOG_THROTTLE_PERCENTAGE_TEMP_VR = 54,
        ADL_PMLOG_THROTTLE_PERCENTAGE_POWER = 55,
        ADL_PMLOG_THROTTLE_PERCENTAGE_TDC = 56,
        ADL_PMLOG_THROTTLE_PERCENTAGE_VMAX = 57,
        ADL_PMLOG_BUS_CURR_MAX_SPEED = 58,
        ADL_PMLOG_RESERVED_1 = 59, //Currently Unused
        ADL_PMLOG_RESERVED_2 = 60, //Currently Unused
        ADL_PMLOG_RESERVED_3 = 61, //Currently Unused
        ADL_PMLOG_RESERVED_4 = 62, //Currently Unused
        ADL_PMLOG_RESERVED_5 = 63, //Currently Unused
        ADL_PMLOG_RESERVED_6 = 64, //Currently Unused
        ADL_PMLOG_RESERVED_7 = 65, //Currently Unused
        ADL_PMLOG_RESERVED_8 = 66, //Currently Unused
        ADL_PMLOG_RESERVED_9 = 67, //Currently Unused
        ADL_PMLOG_RESERVED_10 = 68, //Currently Unused
        ADL_PMLOG_RESERVED_11 = 69, //Currently Unused
        ADL_PMLOG_RESERVED_12 = 70, //Currently Unused
        ADL_PMLOG_CLK_NPUCLK = 71,      //NPU frequency
        ADL_PMLOG_NPU_BUSY_AVG = 72,    //NPU busy
        ADL_PMLOG_BOARD_POWER = 73,
        ADL_PMLOG_TEMPERATURE_INTAKE = 74,
        ADL_PMLOG_MAX_SENSORS_REAL
    }

    /// <summary> ADL Memory allocation function allows ADL to callback for memory allocation</summary>
    /// <param name="size">input size</param>
    /// <returns> retrun ADL Error Code</returns>
    public delegate IntPtr ADL_Main_Memory_Alloc (int size);

    // ///// <summary> ADL Create Function to create ADL Data</summary>
    /// <param name="callback">Call back functin pointer which is ised to allocate memeory </param>
    /// <param name="enumConnectedAdapters">If it is 1, then ADL will only retuen the physical exist adapters </param>
    ///// <returns> retrun ADL Error Code</returns>
    public delegate int ADL_Main_Control_Create(ADL_Main_Memory_Alloc callback, int enumConnectedAdapters);

    /// <summary> ADL Destroy Function to free up ADL Data</summary>
    /// <returns> retrun ADL Error Code</returns>
    public delegate int ADL_Main_Control_Destroy ();

    /// <summary> ADL Function to get the number of adapters</summary>
    /// <param name="numAdapters">return number of adapters</param>
    /// <returns> retrun ADL Error Code</returns>
    public delegate int ADL_Adapter_NumberOfAdapters_Get (ref int numAdapters);

    /// <summary> ADL Function to get the GPU adapter information</summary>
    /// <param name="info">return GPU adapter information</param>
    /// <param name="inputSize">the size of the GPU adapter struct</param>
    /// <returns> retrun ADL Error Code</returns>
    public delegate int ADL_Adapter_AdapterInfo_Get (IntPtr info, int inputSize);

    /// <summary> Function to determine if the adapter is active or not.</summary>
    /// <remarks>The function is used to check if the adapter associated with iAdapterIndex is active</remarks>  
    /// <param name="adapterIndex"> Adapter Index.</param>
    /// <param name="status"> Status of the adapter. True: Active; False: Dsiabled</param>
    /// <returns>Non zero is successfull</returns> 
    public delegate int ADL_Adapter_Active_Get(int adapterIndex, ref int status);

    /// <summary>Get display information based on adapter index</summary>
    /// <param name="adapterIndex">Adapter Index</param>
    /// <param name="numDisplays">return the total number of supported displays</param>
    /// <param name="displayInfoArray">return ADLDisplayInfo Array for supported displays' information</param>
    /// <param name="forceDetect">force detect or not</param>
    /// <returns>return ADL Error Code</returns>
    public delegate int ADL_Display_DisplayInfo_Get(int adapterIndex, ref int numDisplays, out IntPtr displayInfoArray, int forceDetect);

    public delegate int ADL_Flush_Driver_Data(int flush);
    
    public delegate int ADL2_Adapter_Active_Get(IntPtr handle, int something, out int adapterid);
    public delegate int ADL2_Adapter_PMLog_Support_Get(IntPtr context, int iAdapterIndex, out ADLPMLogSupportInfo pPMLogSupportInfo);
    public delegate int ADL2_Adapter_PMLog_Support_Start(IntPtr context, int iAdapterIndex, ref ADLPMLogStartInput pPMLogStartInput, out ADLPMLogStartOutput pPMLogStartOutput, uint pDevice);
    public delegate int ADL2_Adapter_PMLog_Support_Stop(IntPtr context, int iAdapterIndex, uint pDevice);
    public delegate int ADL2_Device_PMLog_Device_Create(IntPtr context, int iAdapterIndex, out uint pDevice);
    public delegate int ADL2_Device_PMLog_Device_Destroy(IntPtr context, uint hDevice);

    [StructLayout(LayoutKind.Sequential)]
    public struct ADLPMLogStartInput
    {
        /// list of sensors defined by ADL_PMLOG_SENSORS
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] usSensors;
        /// Sample rate in milliseconds
        public uint ulSampleRate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst =15)]
        /// Reserved
        public uint[] ulReserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct ADLPMLogStartOutput
    {
        public IntPtr pLoggingAddress;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 14)]
        public uint[] ulReserved;
    };
    [StructLayout(LayoutKind.Sequential)]
    public struct ADLPMLogSupportInfo
    {
        /// list of sensors defined by ADL_PMLOG_SENSORS
        [MarshalAs(UnmanagedType.ByValArray, SizeConst =256)]
        public ushort[] usSensors;
        /// Reserved
        [MarshalAs( UnmanagedType.ByValArray, SizeConst = 16)]
        public int[] ulReserved;
    }
    public struct ADLPMLogData
    {
        /// Structure version
        public uint ulVersion;
        /// Current driver sample rate
        public uint ulActiveSampleRate;
        /// Timestamp of last update
        public ulong ulLastUpdated;
        /// 2D array of senesor and values
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=512)]
        public uint[] ulValues;
        /// Reserved
        [MarshalAs( UnmanagedType.ByValArray, SizeConst=256)]
        public uint[] ulReserved;
    }
    
    /// <summary> ADLAdapterInfo Structure</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ADLAdapterInfo
    {
        /// <summary>The size of the structure</summary>
        int Size;
        /// <summary> Adapter Index</summary>
        public int AdapterIndex;
        /// <summary> Adapter UDID</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = (int)ADL.ADL_MAX_PATH)]
        public string UDID;
        /// <summary> Adapter Bus Number</summary>
        public int BusNumber;
        /// <summary> Adapter Driver Number</summary>
        public int DriverNumber;
        /// <summary> Adapter Function Number</summary>
        public int FunctionNumber;
        /// <summary> Adapter Vendor ID</summary>
        public int VendorID;
        /// <summary> Adapter Adapter name</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = (int)ADL.ADL_MAX_PATH)]
        public string AdapterName;
        /// <summary> Adapter Display name</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = (int)ADL.ADL_MAX_PATH)]
        public string DisplayName;
        /// <summary> Adapter Present status</summary>
        public int Present;
        /// <summary> Adapter Exist status</summary>
        public int Exist;
        /// <summary> Adapter Driver Path</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = (int)ADL.ADL_MAX_PATH)]
        public string DriverPath;
        /// <summary> Adapter Driver Ext Path</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = (int)ADL.ADL_MAX_PATH)]
        public string DriverPathExt;
        /// <summary> Adapter PNP String</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = (int)ADL.ADL_MAX_PATH)]
        public string PNPString;
        /// <summary> OS Display Index</summary>
        public int OSDisplayIndex;
    }


    /// <summary> ADLAdapterInfo Array</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ADLAdapterInfoArray
    {
        /// <summary> ADLAdapterInfo Array </summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)ADL.ADL_MAX_ADAPTERS)]
        public ADLAdapterInfo[] ADLAdapterInfo;
    }

    /// <summary> ADLDisplayID Structure</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ADLDisplayID
    {
        /// <summary> Display Logical Index </summary>
        public int DisplayLogicalIndex;
        /// <summary> Display Physical Index </summary>
        public int DisplayPhysicalIndex;
        /// <summary> Adapter Logical Index </summary>
        public int DisplayLogicalAdapterIndex;
        /// <summary> Adapter Physical Index </summary>
        public int DisplayPhysicalAdapterIndex;
    }

    /// <summary> ADLDisplayInfo Structure</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ADLDisplayInfo
    {
        /// <summary> Display Index </summary>
        public ADLDisplayID DisplayID;
        /// <summary> Display Controller Index </summary>
        public int DisplayControllerIndex;
        /// <summary> Display Name </summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = (int)ADL.ADL_MAX_PATH)]
        public string DisplayName;
        /// <summary> Display Manufacturer Name </summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = (int)ADL.ADL_MAX_PATH)]
        public string DisplayManufacturerName;
        /// <summary> Display Type : < The Display type. CRT, TV,CV,DFP are some of display types,</summary>
        public int DisplayType;
        /// <summary> Display output type </summary>
        public int DisplayOutputType;
        /// <summary> Connector type</summary>
        public int DisplayConnector;
        ///<summary> Indicating the display info bits' mask.<summary>
        public int DisplayInfoMask;
        ///<summary> Indicating the display info value.<summary>
        public int DisplayInfoValue;
    }
    /// <summary> ADL Class</summary>
    public static class ADL
    {
        /// <summary> Define the maximum path</summary>
        public const int ADL_MAX_PATH = 256;
        /// <summary> Define the maximum adapters</summary>
        public const int ADL_MAX_ADAPTERS = 40 /* 150 */;
        /// <summary> Define the maximum displays</summary>
        public const int ADL_MAX_DISPLAYS = 40 /* 150 */;
        /// <summary> Define the maximum device name length</summary>
        public const int ADL_MAX_DEVICENAME = 32;
        /// <summary> Define the successful</summary>
        public const int ADL_SUCCESS = 0;
        /// <summary> Define the failure</summary>
        public const int ADL_FAIL = -1;
        /// <summary> Define the driver ok</summary>
        public const int ADL_DRIVER_OK = 0;
        /// <summary> Maximum number of GL-Sync ports on the GL-Sync module </summary>
        public const int ADL_MAX_GLSYNC_PORTS = 8;
        /// <summary> Maximum number of GL-Sync ports on the GL-Sync module </summary>
        public const int ADL_MAX_GLSYNC_PORT_LEDS = 8;
        /// <summary> Maximum number of ADLMOdes for the adapter </summary>
        public const int ADL_MAX_NUM_DISPLAYMODES = 1024; 

        /// <summary> ADLImport class</summary>
        private static class ADLImport
        {
            /// <summary> Atiadlxx_FileName </summary>
            internal const string Atiadlxx_FileName = "atiadlxx.dll";
            /// <summary> Kernel32_FileName </summary>
            internal const string Kernel32_FileName = "kernel32.dll";
            
            [DllImport(Kernel32_FileName)]
            internal static extern HMODULE GetModuleHandle (string moduleName);

            [DllImport(Atiadlxx_FileName)]
            internal static extern int ADL_Main_Control_Create (ADL_Main_Memory_Alloc callback, int enumConnectedAdapters);

            [DllImport(Atiadlxx_FileName)]
            internal static extern int ADL_Main_Control_Destroy ();
            [DllImport(Atiadlxx_FileName)]
            internal static extern int ADL_Flush_Driver_Data(int flush);
            
            [DllImport(Atiadlxx_FileName)]
            internal static extern int ADL_Main_Control_IsFunctionValid (HMODULE module, string procName);

            [DllImport(Atiadlxx_FileName)]
            internal static extern FARPROC ADL_Main_Control_GetProcAddress (HMODULE module, string procName);

            [DllImport(Atiadlxx_FileName)]
            internal static extern int ADL_Adapter_NumberOfAdapters_Get (ref int numAdapters);

            [DllImport(Atiadlxx_FileName)]
            internal static extern int ADL_Adapter_AdapterInfo_Get (IntPtr info, int inputSize);

            [DllImport(Atiadlxx_FileName)]
            internal static extern int ADL_Adapter_Active_Get(int adapterIndex, ref int status);

            [DllImport(Atiadlxx_FileName)]
            internal static extern int ADL_Display_DisplayInfo_Get(int adapterIndex, ref int numDisplays, out IntPtr displayInfoArray, int forceDetect);
            [DllImport(Atiadlxx_FileName)]
            internal static extern int ADL2_Adapter_Active_Get(IntPtr handle, int something, out int adapterid);

            [DllImport(Atiadlxx_FileName)]
            internal static extern int ADL2_Adapter_PMLog_Support_Get(IntPtr context, int iAdapterIndex, out ADLPMLogSupportInfo pPMLogSupportInfo);
            [DllImport(Atiadlxx_FileName)]
            internal static extern int ADL2_Adapter_PMLog_Start(IntPtr context, int iAdapterIndex, ref ADLPMLogStartInput pPMLogStartInput, out ADLPMLogStartOutput pPMLogStartOutput, uint pDevice);
            [DllImport(Atiadlxx_FileName)]
            internal static extern int ADL2_Adapter_PMLog_Stop(IntPtr context, int iAdapterIndex, uint pDevice);
            [DllImport(Atiadlxx_FileName)]
            internal static extern int ADL2_Device_PMLog_Device_Create(IntPtr context, int iAdapterIndex, out uint pDevice);
            [DllImport(Atiadlxx_FileName)]
            internal static extern int ADL2_Device_PMLog_Device_Destroy (IntPtr context, uint hDevice);
        }

        /// <summary> ADLCheckLibrary class</summary>
        private class ADLCheckLibrary : IDisposable
        {
            private HMODULE ADLLibrary = System.IntPtr.Zero;
            
            /// <summary> new a private instance</summary>
            private static ADLCheckLibrary ADLCheckLibrary_ = new ADLCheckLibrary();
            private bool _isDisposed;

            /// <summary> Constructor</summary>
            private ADLCheckLibrary ()
            {
                try
                {
                    if (1 == ADLImport.ADL_Main_Control_IsFunctionValid(IntPtr.Zero, "ADL_Main_Control_Create"))
                    {
                        ADLLibrary = ADLImport.GetModuleHandle(ADLImport.Atiadlxx_FileName);
                    }
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
                catch (Exception) { }
            }
            
            /// <summary> Destructor to force calling ADL Destroy function before free up the ADL library</summary>
            ~ADLCheckLibrary ()
            {
                Dispose(disposing: false);
            }
            /// <summary> Check the import function to see it exists or not</summary>
            /// <param name="functionName"> function name</param>
            /// <returns>return true, if function exists</returns>
            public static bool IsFunctionValid (string functionName)
            {
                bool result = false;
                if (System.IntPtr.Zero != ADLCheckLibrary_.ADLLibrary)
                {
                    if (1 == ADLImport.ADL_Main_Control_IsFunctionValid(ADLCheckLibrary_.ADLLibrary, functionName))
                    {
                        result = true;
                    }
                }
                return result;
            }
   
            /// <summary> Get the unmanaged function pointer </summary>
            /// <param name="functionName"> function name</param>
            /// <returns>return function pointer, if function exists</returns>
            public static FARPROC GetProcAddress (string functionName)
            {
                FARPROC result = System.IntPtr.Zero;
                if (System.IntPtr.Zero != ADLCheckLibrary_.ADLLibrary)
                {
                    result = ADLImport.ADL_Main_Control_GetProcAddress(ADLCheckLibrary_.ADLLibrary, functionName);
                }
                return result;
            }

            protected virtual void Dispose(bool disposing)
            {
                if (!_isDisposed)
                {
                    if (disposing)
                    {
                        // TODO: dispose managed state (managed objects)
                    }

                    if (System.IntPtr.Zero != ADLCheckLibrary_.ADLLibrary)
                    {
                        ADLImport.ADL_Main_Control_Destroy();
                    }
                    _isDisposed = true;
                }
            }

            public void Dispose()
            {
                // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
   
        }
        
        
        /// <summary> Build in memory allocation function</summary>
        public static ADL_Main_Memory_Alloc ADL_Main_Memory_Alloc = ADL_Main_Memory_Alloc_;
        /// <summary> Build in memory allocation function</summary>
        /// <param name="size">input size</param>
        /// <returns>return the memory buffer</returns>
        private static IntPtr ADL_Main_Memory_Alloc_ (int size)
        {
            IntPtr result = Marshal.AllocCoTaskMem(size);
            return result;
        }
        
        /// <summary> Build in memory free function</summary>
        /// <param name="buffer">input buffer</param>
        public static void ADL_Main_Memory_Free (IntPtr buffer)
        {
            if (IntPtr.Zero != buffer)
            {
                Marshal.FreeCoTaskMem(buffer);
            }
        }
       
        /// <summary> ADL_Main_Control_Create Delegates</summary>
        public static ADL_Main_Control_Create? ADL_Main_Control_Create
        {
            get
            {
                if (!ADL_Main_Control_Create_Check && null == ADL_Main_Control_Create_)
                {
                    ADL_Main_Control_Create_Check = true;
                    if (ADLCheckLibrary.IsFunctionValid("ADL_Main_Control_Create"))
                    {
                        ADL_Main_Control_Create_ = ADLImport.ADL_Main_Control_Create;
                    }
                }
                return ADL_Main_Control_Create_;
            }
        }
        /// <summary> Private Delegate</summary>
        private static ADL_Main_Control_Create? ADL_Main_Control_Create_ = null;
        /// <summary> check flag to indicate the delegate has been checked</summary>
        private static bool ADL_Main_Control_Create_Check = false;
        
        /// <summary> ADL_Main_Control_Destroy Delegates</summary>
        public static ADL_Main_Control_Destroy? ADL_Main_Control_Destroy
        {
            get
            {
                if (!ADL_Main_Control_Destroy_Check && null == ADL_Main_Control_Destroy_)
                {
                    ADL_Main_Control_Destroy_Check = true;
                    if (ADLCheckLibrary.IsFunctionValid("ADL_Main_Control_Destroy"))
                    {
                        ADL_Main_Control_Destroy_ = ADLImport.ADL_Main_Control_Destroy;
                    }
                }
                return ADL_Main_Control_Destroy_;
            }
        }
        /// <summary> Private Delegate</summary>
        private static ADL_Main_Control_Destroy? ADL_Main_Control_Destroy_ = null;
        /// <summary> check flag to indicate the delegate has been checked</summary>
        private static bool ADL_Main_Control_Destroy_Check = false;
        public static ADL_Flush_Driver_Data? ADL_Flush_Driver_Data
        {
            get
            {
                if(!ADL_Flush_Driver_Data_Check && null == ADL_Flush_Driver_Data_)
                {
                    ADL_Flush_Driver_Data_Check = true;
                    if(ADLCheckLibrary.IsFunctionValid("ADL_Flush_Driver_Data_Check"))
                    {
                        ADL_Flush_Driver_Data_ = ADLImport.ADL_Flush_Driver_Data;
                    }
                }
                return ADL_Flush_Driver_Data_;
            }
        }
        private static ADL_Flush_Driver_Data? ADL_Flush_Driver_Data_ = null;
        private static bool ADL_Flush_Driver_Data_Check = false;

        /// <summary> ADL_Adapter_NumberOfAdapters_Get Delegates</summary>
        public static ADL_Adapter_NumberOfAdapters_Get? ADL_Adapter_NumberOfAdapters_Get
        {
            get
            {
                if (!ADL_Adapter_NumberOfAdapters_Get_Check && null == ADL_Adapter_NumberOfAdapters_Get_)
                {
                    ADL_Adapter_NumberOfAdapters_Get_Check = true;
                    if (ADLCheckLibrary.IsFunctionValid("ADL_Adapter_NumberOfAdapters_Get"))
                    {
                        ADL_Adapter_NumberOfAdapters_Get_ = ADLImport.ADL_Adapter_NumberOfAdapters_Get;
                    }
                }
                return ADL_Adapter_NumberOfAdapters_Get_;
            }
        }
        /// <summary> Private Delegate</summary>
        private static ADL_Adapter_NumberOfAdapters_Get? ADL_Adapter_NumberOfAdapters_Get_ = null;
        /// <summary> check flag to indicate the delegate has been checked</summary>
        private static bool ADL_Adapter_NumberOfAdapters_Get_Check = false;
        
        /// <summary> ADL_Adapter_AdapterInfo_Get Delegates</summary>
        public static ADL_Adapter_AdapterInfo_Get? ADL_Adapter_AdapterInfo_Get
        {
            get
            {
                if (!ADL_Adapter_AdapterInfo_Get_Check && null == ADL_Adapter_AdapterInfo_Get_)
                {
                    ADL_Adapter_AdapterInfo_Get_Check = true;
                    if (ADLCheckLibrary.IsFunctionValid("ADL_Adapter_AdapterInfo_Get"))
                    {
                        ADL_Adapter_AdapterInfo_Get_ = ADLImport.ADL_Adapter_AdapterInfo_Get;
                    }
                }
                return ADL_Adapter_AdapterInfo_Get_;
            }
        }
        /// <summary> Private Delegate</summary>
        private static ADL_Adapter_AdapterInfo_Get? ADL_Adapter_AdapterInfo_Get_ = null;
        /// <summary> check flag to indicate the delegate has been checked</summary>
        private static bool ADL_Adapter_AdapterInfo_Get_Check = false;
        
        /// <summary> ADL_Adapter_Active_Get Delegates</summary>
        public static ADL_Adapter_Active_Get? ADL_Adapter_Active_Get
        {
            get
            {
                if (!ADL_Adapter_Active_Get_Check && null == ADL_Adapter_Active_Get_)
                {
                    ADL_Adapter_Active_Get_Check = true;
                    if (ADLCheckLibrary.IsFunctionValid("ADL_Adapter_Active_Get"))
                    {
                        ADL_Adapter_Active_Get_ = ADLImport.ADL_Adapter_Active_Get;
                    }
                }
                return ADL_Adapter_Active_Get_;
            }
        }
        /// <summary> Private Delegate</summary>
        private static ADL_Adapter_Active_Get? ADL_Adapter_Active_Get_ = null;
        /// <summary> check flag to indicate the delegate has been checked</summary>
        private static bool ADL_Adapter_Active_Get_Check = false;
        
        /// <summary> ADL_Display_DisplayInfo_Get Delegates</summary>
        public static ADL_Display_DisplayInfo_Get? ADL_Display_DisplayInfo_Get
        {
            get
            {
                if (!ADL_Display_DisplayInfo_Get_Check && null == ADL_Display_DisplayInfo_Get_)
                {
                    ADL_Display_DisplayInfo_Get_Check = true;
                    if (ADLCheckLibrary.IsFunctionValid("ADL_Display_DisplayInfo_Get"))
                    {
                        ADL_Display_DisplayInfo_Get_ = ADLImport.ADL_Display_DisplayInfo_Get;
                    }
                }
                return ADL_Display_DisplayInfo_Get_;
            }
        }
        private static ADL_Display_DisplayInfo_Get? ADL_Display_DisplayInfo_Get_ = null;
        /// <summary> check flag to indicate the delegate has been checked</summary>
        private static bool ADL_Display_DisplayInfo_Get_Check = false;

        public static ADL2_Adapter_Active_Get? ADL2_Adapter_Active_Get
        {
            get
            {
                if (!ADL2_Adapter_Active_Get_Check && null == ADL2_Adapter_Active_Get_)
                {
                    ADL2_Adapter_Active_Get_Check = true;
                    if (ADLCheckLibrary.IsFunctionValid("ADL2_Adapter_Active_Get"))
                    {
                        ADL2_Adapter_Active_Get_ = ADLImport.ADL2_Adapter_Active_Get;
                    }
                }
                return ADL2_Adapter_Active_Get_;
            }
        }
        private static ADL2_Adapter_Active_Get? ADL2_Adapter_Active_Get_ = null;
        private static bool ADL2_Adapter_Active_Get_Check = false;

        public static ADL2_Adapter_PMLog_Support_Get? ADL2_Adapter_PMLog_Support_Get
        {
            get
            {
                if (!ADL2_Adapter_PMLog_Support_Get_Check && null == ADL2_Adapter_PMLog_Support_Get_)
                {
                    ADL2_Adapter_PMLog_Support_Get_Check = true;
                    if (ADLCheckLibrary.IsFunctionValid("ADL2_Adapter_PMLog_Support_Get"))
                    {
                        ADL2_Adapter_PMLog_Support_Get_ = ADLImport.ADL2_Adapter_PMLog_Support_Get;
                    }
                }
                return ADL2_Adapter_PMLog_Support_Get_;
            }
        }
        private static ADL2_Adapter_PMLog_Support_Get? ADL2_Adapter_PMLog_Support_Get_ = null;
        private static bool ADL2_Adapter_PMLog_Support_Get_Check = false;

        public static ADL2_Adapter_PMLog_Support_Start? ADL2_Adapter_PMLog_Start
        {
            get
            {
                if (!ADL2_Adapter_PMLog_Start_Check && null == ADL2_Adapter_PMLog_Start_)
                {
                    ADL2_Adapter_PMLog_Start_Check = true;
                    if (ADLCheckLibrary.IsFunctionValid("ADL2_Adapter_PMLog_Start"))
                    {
                        ADL2_Adapter_PMLog_Start_ = ADLImport.ADL2_Adapter_PMLog_Start;
                    }
                }
                return ADL2_Adapter_PMLog_Start_;
            }
        }
        private static ADL2_Adapter_PMLog_Support_Start? ADL2_Adapter_PMLog_Start_ = null;
        private static bool ADL2_Adapter_PMLog_Start_Check = false;

        public static ADL2_Adapter_PMLog_Support_Stop? ADL2_Adapter_PMLog_Stop
        {
            get
            {
                if (!ADL2_Adapter_PMLog_Stop_Check && null == ADL2_Adapter_PMLog_Stop_)
                {
                    ADL2_Adapter_PMLog_Stop_Check = true;
                    if (ADLCheckLibrary.IsFunctionValid("ADL2_Adapter_PMLog_Stop"))
                    {
                        ADL2_Adapter_PMLog_Stop_ = ADLImport.ADL2_Adapter_PMLog_Stop;
                    }
                }
                return ADL2_Adapter_PMLog_Stop_;
            }
        }
        private static ADL2_Adapter_PMLog_Support_Stop? ADL2_Adapter_PMLog_Stop_ = null;
        private static bool ADL2_Adapter_PMLog_Stop_Check = false;

        public static ADL2_Device_PMLog_Device_Create? ADL2_Device_PMLog_Device_Create
        {
            get
            {
                if (!ADL2_Device_PMLog_Device_Create_Check && null == ADL2_Device_PMLog_Device_Create_)
                {
                    ADL2_Device_PMLog_Device_Create_Check = true;
                    if (ADLCheckLibrary.IsFunctionValid("ADL2_Device_PMLog_Device_Create"))
                    {
                        ADL2_Device_PMLog_Device_Create_ = ADLImport.ADL2_Device_PMLog_Device_Create;
                    }
                }
                return ADL2_Device_PMLog_Device_Create_;
            }
        }

        private static ADL2_Device_PMLog_Device_Create? ADL2_Device_PMLog_Device_Create_ = null;
        private static bool ADL2_Device_PMLog_Device_Create_Check = false;

        public static ADL2_Device_PMLog_Device_Destroy? ADL2_Device_PMLog_Device_Destroy
        {
            get
            {
                if (!ADL2_Device_PMLog_Device_Destroy_Check && null == ADL2_Device_PMLog_Device_Destroy_)
                {
                    ADL2_Device_PMLog_Device_Destroy_Check = true;
                    if (ADLCheckLibrary.IsFunctionValid("ADL2_Device_PMLog_Device_Destroy"))
                    {
                        ADL2_Device_PMLog_Device_Destroy_ = ADLImport.ADL2_Device_PMLog_Device_Destroy;
                    }
                }
                return ADL2_Device_PMLog_Device_Destroy_;
            }
        }

        private static ADL2_Device_PMLog_Device_Destroy? ADL2_Device_PMLog_Device_Destroy_ = null;
        private static bool ADL2_Device_PMLog_Device_Destroy_Check = false;

    }
}
