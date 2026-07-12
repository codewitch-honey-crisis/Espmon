using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace HWKit
{
    // Single native hardware provider covering /cpu, /ram, and /disk. Replaces the
    // separate CimCpuProvider / CimRamProvider / CimDiskProvider. No CIM/WMI.
    //
    // One background sampler serves CPU and disk; each domain parks on its own idle
    // timer, so accessing one doesn't force refreshes of the other. RAM is read inline
    // (GlobalMemoryStatusEx is sub-microsecond) and needs no worker.
    //
    // Metric conventions:
    //   * "load" means USED percent, consistently for /ram, /disk/{i}, and each volume.
    //   * ram and each disk volume expose the same set: total / free / used / load.
    //   * Attributes with no native equivalent are omitted entirely (physical-disk slot,
    //     spindle speed, and Storage-Spaces allocated size).
    [SupportedOSPlatform("windows")]
    public partial class Win32Provider : HardwareInfoProviderBase
    {
        const long IdleMs = 5000;
        const int SeedIntervalMs = 120; // gap between the two startup CPU samples that seed load

        // RAM scaling kept identical to the former CimRamProvider (values held in KB).
        const float _multiplicand = 1000f;
        const float _divisor = (1024 * 1024);

        readonly object _gate = new();
        readonly AutoResetEvent _wake = new(false);
        readonly ManualResetEventSlim _stop = new(false);

        Thread? _worker;
        volatile bool _started;

        // Per-domain activity timestamps; the worker refreshes a domain only while it's
        // been accessed within IdleMs, and parks when neither has.
        long _cpuLastAccess;
        long _diskLastAccess;

        // CPU state
        int _procCount;
        CpuEntry[]? _cpuEntries;
        CpuCoreEntry[]? _coreEntries;
        long[]? _prevIdle, _prevKernel, _prevUser;

        // Disk state
        PhysicalDiskInfo[]? _disks;

        protected override string GetDisplayName() => "Windows Native Hardware Provider";
        protected override string GetIdentifier() => "win32";
        protected override string GetDescription()
            => "Provides CPU, RAM, and disk metrics via native Win32/NT APIs";
        protected override HardwareInfoProviderStatus GetState()
            => _started ? HardwareInfoProviderStatus.Started : HardwareInfoProviderStatus.Stopped;

        void TouchCpu()
        {
            if (!_started) return;
            Volatile.Write(ref _cpuLastAccess, Environment.TickCount64);
            _wake.Set();
        }
        void TouchDisk()
        {
            if (!_started) return;
            Volatile.Write(ref _diskLastAccess, Environment.TickCount64);
            _wake.Set();
        }

        // ========================================================================
        // CPU
        // ========================================================================
        private struct CpuEntry
        {
            public float MaxFrequency;
            public CpuEntry(float maxFrequency) { MaxFrequency = maxFrequency; }
        }

        private struct CpuCoreEntry
        {
            public int CpuIndex;
            public int ThreadIndex;
            public float Frequency;
            public float Load;
            public CpuCoreEntry(int cpuIndex, int threadIndex, float frequency, float load)
            {
                CpuIndex = cpuIndex;
                ThreadIndex = threadIndex;
                Frequency = frequency;
                Load = load;
            }
        }

        sealed class CoreEntryAccessor
        {
            readonly Win32Provider _owner;
            readonly int _index;
            public CoreEntryAccessor(Win32Provider owner, int index)
            {
                ArgumentNullException.ThrowIfNull(owner, nameof(owner));
                _owner = owner;
                _index = index;
            }
            float Read(Func<CpuCoreEntry, float> selector)
            {
                _owner.TouchCpu();
                lock (_owner._gate)
                {
                    var entries = _owner._coreEntries;
                    if (entries == null || _index < 0 || _index >= entries.Length)
                        return float.NaN;
                    return selector(entries[_index]);
                }
            }
            public float Frequency => Read(e => e.Frequency);
            public float Load => Read(e => e.Load);
        }

        sealed class CpuEntryAccessor
        {
            readonly Win32Provider _owner;
            readonly int _index;
            public CpuEntryAccessor(Win32Provider owner, int index)
            {
                ArgumentNullException.ThrowIfNull(owner, nameof(owner));
                _owner = owner;
                _index = index;
            }
            float Read(Func<CpuEntry, float> selector)
            {
                _owner.TouchCpu();
                lock (_owner._gate)
                {
                    var entries = _owner._cpuEntries;
                    if (entries == null || _index < 0 || _index >= entries.Length)
                        return float.NaN;
                    return selector(entries[_index]);
                }
            }
            public float MaxFrequency => Read(e => e.MaxFrequency);
        }

        const int ProcessorInformation = 11;                   // POWER_INFORMATION_LEVEL
        const int SystemProcessorPerformanceInformation = 8;   // SYSTEM_INFORMATION_CLASS

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESSOR_POWER_INFORMATION
        {
            public uint Number;
            public uint MaxMhz;
            public uint CurrentMhz;
            public uint MhzLimit;
            public uint MaxIdleState;
            public uint CurrentIdleState;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
        {
            public long IdleTime;      // 100ns; KernelTime already includes IdleTime
            public long KernelTime;
            public long UserTime;
            public long DpcTime;
            public long InterruptTime;
            public uint InterruptCount;
        }

        [LibraryImport("powrprof.dll")]
        private static unsafe partial uint CallNtPowerInformation(
            int informationLevel, void* inputBuffer, uint inputBufferLength,
            void* outputBuffer, uint outputBufferLength);

        [LibraryImport("ntdll.dll")]
        private static unsafe partial int NtQuerySystemInformation(
            int systemInformationClass, void* systemInformation,
            uint systemInformationLength, uint* returnLength);

        static unsafe PROCESSOR_POWER_INFORMATION[] ReadPowerInfo(int n)
        {
            if (n <= 0) return Array.Empty<PROCESSOR_POWER_INFORMATION>();
            var arr = new PROCESSOR_POWER_INFORMATION[n];
            fixed (PROCESSOR_POWER_INFORMATION* p = arr)
            {
                uint size = (uint)(n * sizeof(PROCESSOR_POWER_INFORMATION));
                uint status = CallNtPowerInformation(ProcessorInformation, null, 0, p, size);
                if (status != 0) return Array.Empty<PROCESSOR_POWER_INFORMATION>();
            }
            return arr;
        }

        static unsafe int ReadPerf(int n, long[] idle, long[] kernel, long[] user)
        {
            if (n <= 0) return 0;
            int stride = sizeof(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION);
            var raw = new byte[n * stride];
            fixed (byte* b = raw)
            {
                uint ret = 0;
                int status = NtQuerySystemInformation(
                    SystemProcessorPerformanceInformation, b, (uint)raw.Length, &ret);
                if (status != 0) return 0;
                int count = (int)(ret / (uint)stride);
                var sp = (SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION*)b;
                int m = Math.Min(n, count);
                for (int i = 0; i < m; i++)
                {
                    idle[i] = sp[i].IdleTime;
                    kernel[i] = sp[i].KernelTime;
                    user[i] = sp[i].UserTime;
                }
                return m;
            }
        }

        static int FirstProcessorOf(CpuTopology.Package pkg)
        {
            int best = int.MaxValue;
            foreach (var a in pkg.Affinities)
            {
                if (a.Group != 0 || a.Mask == 0) continue;
                int bit = System.Numerics.BitOperations.TrailingZeroCount(a.Mask);
                if (bit < best) best = bit;
            }
            return best == int.MaxValue ? -1 : best;
        }

        void SeedCpuStatic()
        {
            _procCount = Environment.ProcessorCount;

            var packages = CpuTopology.GetPackages();
            var power = ReadPowerInfo(_procCount);

            var cpuEntries = new CpuEntry[packages.Count];
            for (int pi = 0; pi < packages.Count; pi++)
            {
                int firstProc = FirstProcessorOf(packages[pi]);
                float maxMhz = (firstProc >= 0 && firstProc < power.Length)
                    ? power[firstProc].MaxMhz : float.NaN;
                cpuEntries[pi] = new CpuEntry(maxMhz);
            }

            _prevIdle = new long[_procCount];
            _prevKernel = new long[_procCount];
            _prevUser = new long[_procCount];
            ReadPerf(_procCount, _prevIdle, _prevKernel, _prevUser);

            lock (_gate) { _cpuEntries = cpuEntries; }
        }

        void RunCpuSample()
        {
            int n = _procCount;
            var power = ReadPowerInfo(n);

            var idle = new long[n];
            var kernel = new long[n];
            var user = new long[n];
            int reported = ReadPerf(n, idle, kernel, user);

            var list = new CpuCoreEntry[Math.Min(n, reported == 0 ? n : reported)];
            for (int p = 0; p < list.Length; p++)
            {
                float freq = (p < power.Length) ? power[p].CurrentMhz : float.NaN;

                float load = float.NaN;
                if (_prevIdle != null && _prevKernel != null && _prevUser != null &&
                    p < _prevIdle.Length && reported > 0)
                {
                    long dIdle = idle[p] - _prevIdle[p];
                    long dKernel = kernel[p] - _prevKernel[p];
                    long dUser = user[p] - _prevUser[p];
                    long total = dKernel + dUser;   // KernelTime includes idle
                    if (total > 0)
                    {
                        long busy = total - dIdle;
                        if (busy < 0) busy = 0;
                        load = 100f * busy / total;
                    }
                    else load = 0f;
                }

                list[p] = new CpuCoreEntry(0, p, freq, load); // group 0 keying
            }

            if (reported > 0)
            {
                _prevIdle = idle;
                _prevKernel = kernel;
                _prevUser = user;
            }

            lock (_gate)
            {
                if (_coreEntries == null || _coreEntries.Length != list.Length)
                    _coreEntries = new CpuCoreEntry[list.Length];
                Array.Copy(list, _coreEntries, list.Length);
            }
        }

        // ========================================================================
        // RAM (inline, no worker)
        // ========================================================================
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        // KB values so the published-value arithmetic matches the former CimRamProvider.
        private static bool ReadKb(out float totalKb, out float freeKb, out float freeVirtualKb)
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref m))
            {
                totalKb = freeKb = freeVirtualKb = float.NaN;
                return false;
            }
            totalKb = m.ullTotalPhys / 1024f;
            freeKb = m.ullAvailPhys / 1024f;
            freeVirtualKb = m.ullAvailPageFile / 1024f;
            return true;
        }

        private float SafeTotal
        {
            get
            {
                if (!_started || !ReadKb(out var total, out _, out _)) return float.NaN;
                return total * _multiplicand / _divisor;
            }
        }
        private float SafeFree
        {
            get
            {
                if (!_started || !ReadKb(out _, out var free, out _)) return float.NaN;
                return free * _multiplicand / _divisor;
            }
        }
        private float SafeUsed
        {
            get
            {
                if (!_started || !ReadKb(out var total, out var free, out _)) return float.NaN;
                return (total - free) * _multiplicand / _divisor;
            }
        }
        private float SafeFreeVirtual
        {
            get
            {
                if (!_started || !ReadKb(out _, out _, out var freeVirtual)) return float.NaN;
                return freeVirtual * _multiplicand / _divisor;
            }
        }
        private float SafeLoad // used %
        {
            get
            {
                if (!_started || !ReadKb(out var total, out var free, out _)) return float.NaN;
                if (total <= 0) return float.NaN;
                return 100 - ((float)Math.Round(free * 100 / total));
            }
        }

        // ========================================================================
        // DISK
        // ========================================================================
        class PhysicalDiskInfo
        {
            public ulong Size { get; set; }
            public ushort? HealthStatus { get; set; }
            public ushort? MediaType { get; set; }
            public ulong? PhysicalSectorSize { get; set; }
            public ulong? LogicalSectorSize { get; set; }
            public VolumeInfo[] Volumes { get; set; } = Array.Empty<VolumeInfo>();
        }

        class VolumeInfo
        {
            public ulong Size { get; set; }
            public ulong SizeRemaining { get; set; }
        }

        sealed class DiskAccessor
        {
            readonly Win32Provider _owner;
            readonly int _index;
            public DiskAccessor(Win32Provider owner, int index)
            {
                ArgumentNullException.ThrowIfNull(owner, nameof(owner));
                _owner = owner;
                _index = index;
            }
            static float ValueOrNaN<T>(T? value)
            {
                if (value == null) return float.NaN;
                try { return Convert.ToSingle(value); } catch { return float.NaN; }
            }
            static float ValueOrZero<T>(T? value)
            {
                if (value == null) return 0f;
                try { return Convert.ToSingle(value); } catch { return 0f; }
            }
            float Read(Func<PhysicalDiskInfo, float> selector)
            {
                _owner.TouchDisk();
                lock (_owner._gate)
                {
                    var entries = _owner._disks;
                    if (entries == null || _index < 0 || _index >= entries.Length)
                        return float.NaN;
                    try { return selector(entries[_index]); } catch { return float.NaN; }
                }
            }
            public float Load => Read(d => // used %
            {
                double totalSize = 0, totalRemaining = 0;
                for (int i = 0; i < d.Volumes.Length; i++)
                {
                    var v = d.Volumes[i];
                    if (v == null) continue;
                    totalSize += v.Size;
                    totalRemaining += v.SizeRemaining;
                }
                if (totalSize <= 0) return float.NaN;
                return (float)((1.0 - (totalRemaining / totalSize)) * 100.0);
            });
            public float Health => Read(d => ValueOrNaN(d.HealthStatus));
            public float Type => Read(d => ValueOrNaN(d.MediaType));
            public float Size => Read(d => ValueOrZero(d.Size) / (1024.0f * 1024.0f));
            public float PhysicalSectorSize => Read(d => ValueOrZero(d.PhysicalSectorSize) / (1024.0f * 1024.0f));
            public float LogicalSectorSize => Read(d => ValueOrZero(d.LogicalSectorSize) / (1024.0f * 1024.0f));
        }

        sealed class VolumeAccessor
        {
            readonly Win32Provider _owner;
            readonly int _diskIndex;
            readonly int _volumeIndex;
            public VolumeAccessor(Win32Provider owner, int diskIndex, int volumeIndex)
            {
                ArgumentNullException.ThrowIfNull(owner, nameof(owner));
                _owner = owner;
                _diskIndex = diskIndex;
                _volumeIndex = volumeIndex;
            }
            float Read(Func<VolumeInfo, float> selector)
            {
                _owner.TouchDisk();
                lock (_owner._gate)
                {
                    var entries = _owner._disks;
                    if (entries == null || _diskIndex < 0 || _diskIndex >= entries.Length)
                        return float.NaN;
                    var vols = entries[_diskIndex].Volumes;
                    if (vols == null || _volumeIndex < 0 || _volumeIndex >= vols.Length)
                        return float.NaN;
                    var v = vols[_volumeIndex];
                    if (v == null) return float.NaN;
                    try { return selector(v); } catch { return float.NaN; }
                }
            }
            public float Total => Read(v => v.Size / (1024.0f * 1024.0f));
            public float Free => Read(v => v.SizeRemaining / (1024.0f * 1024.0f));
            public float Used => Read(v => (v.Size - v.SizeRemaining) / (1024.0f * 1024.0f));
            public float Load => Read(v => // used %
                v.Size > 0 ? (1f - (float)v.SizeRemaining / v.Size) * 100f : float.NaN);
        }

        const uint FILE_SHARE_READ = 0x1;
        const uint FILE_SHARE_WRITE = 0x2;
        const uint OPEN_EXISTING = 3;
        const int MAX_PHYSICAL_DRIVE = 64;

        const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C;
        const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
        const uint IOCTL_STORAGE_PREDICT_FAILURE = 0x002D1100;
        const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;

        const int StorageAccessAlignmentProperty = 6;
        const int StorageDeviceSeekPenaltyProperty = 7;
        const int PropertyStandardQuery = 0;

        [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        private static partial SafeFileHandle CreateFileW(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
            uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool DeviceIoControl(
            SafeFileHandle hDevice, uint dwIoControlCode,
            void* lpInBuffer, uint nInBufferSize, void* lpOutBuffer, uint nOutBufferSize,
            uint* lpBytesReturned, IntPtr lpOverlapped);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static unsafe partial IntPtr FindFirstVolumeW(char* lpszVolumeName, uint cchBufferLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool FindNextVolumeW(IntPtr hFindVolume, char* lpszVolumeName, uint cchBufferLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool FindVolumeClose(IntPtr hFindVolume);

        [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool GetVolumePathNamesForVolumeNameW(
            string lpszVolumeName, char* lpszVolumePathNames, uint cchBufferLength, uint* lpcchReturnLength);

        [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool GetDiskFreeSpaceExW(
            string lpDirectoryName, ulong* lpFreeBytesAvailableToCaller,
            ulong* lpTotalNumberOfBytes, ulong* lpTotalNumberOfFreeBytes);

        [StructLayout(LayoutKind.Sequential)]
        private struct STORAGE_PROPERTY_QUERY
        {
            public int PropertyId;
            public int QueryType;
            public byte AdditionalParameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STORAGE_ACCESS_ALIGNMENT_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            public uint BytesPerCacheLine;
            public uint BytesOffsetForCacheAlignment;
            public uint BytesPerLogicalSector;
            public uint BytesPerPhysicalSector;
        }

        static SafeFileHandle OpenDevice(string path)
            => CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

        static unsafe bool TryGetDiskLength(SafeFileHandle h, out ulong length)
        {
            length = 0;
            long len;
            uint ret;
            bool ok = DeviceIoControl(h, IOCTL_DISK_GET_LENGTH_INFO, null, 0, &len, 8, &ret, IntPtr.Zero);
            if (ok) length = (ulong)len;
            return ok;
        }

        static unsafe bool TryGetSectorSizes(SafeFileHandle h, out ulong logical, out ulong physical)
        {
            logical = physical = 0;
            var q = new STORAGE_PROPERTY_QUERY
            {
                PropertyId = StorageAccessAlignmentProperty,
                QueryType = PropertyStandardQuery
            };
            STORAGE_ACCESS_ALIGNMENT_DESCRIPTOR d;
            uint ret;
            bool ok = DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY,
                &q, (uint)sizeof(STORAGE_PROPERTY_QUERY),
                &d, (uint)sizeof(STORAGE_ACCESS_ALIGNMENT_DESCRIPTOR), &ret, IntPtr.Zero);
            if (!ok) return false;
            logical = d.BytesPerLogicalSector;
            physical = d.BytesPerPhysicalSector;
            return true;
        }

        // MediaType: 3 = HDD, 4 = SSD, derived from seek penalty (NVMe reports as SSD).
        static unsafe bool TryGetMediaType(SafeFileHandle h, out ushort mediaType)
        {
            mediaType = 0;
            var q = new STORAGE_PROPERTY_QUERY
            {
                PropertyId = StorageDeviceSeekPenaltyProperty,
                QueryType = PropertyStandardQuery
            };
            byte* outBuf = stackalloc byte[16]; // Version(4) Size(4) IncursSeekPenalty(BOOLEAN @8)
            uint ret;
            bool ok = DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY,
                &q, (uint)sizeof(STORAGE_PROPERTY_QUERY), outBuf, 16, &ret, IntPtr.Zero);
            if (!ok || ret < 9) return false;
            bool incursSeekPenalty = outBuf[8] != 0;
            mediaType = (ushort)(incursSeekPenalty ? 3 : 4);
            return true;
        }

        // Binary health via predict-failure: 0 = Healthy, 2 = Unhealthy; false (-> NaN)
        // when the device doesn't support the IOCTL.
        static unsafe bool TryGetHealth(SafeFileHandle h, out ushort health)
        {
            health = 0;
            byte* outBuf = stackalloc byte[516]; // DWORD PredictFailure; BYTE VendorSpecific[512]
            uint ret;
            bool ok = DeviceIoControl(h, IOCTL_STORAGE_PREDICT_FAILURE,
                null, 0, outBuf, 516, &ret, IntPtr.Zero);
            if (!ok || ret < 4) return false;
            uint predictFailure = *(uint*)outBuf;
            health = (ushort)(predictFailure != 0 ? 2 : 0);
            return true;
        }

        static unsafe uint[] GetVolumeDiskNumbers(string volumeGuidPath)
        {
            string dev = volumeGuidPath.TrimEnd('\\');
            using SafeFileHandle h = OpenDevice(dev);
            if (h.IsInvalid) return Array.Empty<uint>();

            byte* buf = stackalloc byte[8 + 24 * 32]; // up to 32 DISK_EXTENTs (24 bytes each)
            uint ret;
            bool ok = DeviceIoControl(h, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS,
                null, 0, buf, (uint)(8 + 24 * 32), &ret, IntPtr.Zero);
            if (!ok || ret < 8) return Array.Empty<uint>();

            uint count = *(uint*)buf;
            if (count == 0) return Array.Empty<uint>();
            if (count > 32) count = 32;

            var nums = new uint[count];
            for (int e = 0; e < count; e++)
                nums[e] = *(uint*)(buf + 8 + e * 24); // DiskNumber is first field of each extent
            return nums;
        }

        static unsafe string ReadVolumeName(char* buf) => new string(buf);

        private struct VolumeRec
        {
            public uint[] DiskNumbers;
            public ulong Size;
            public ulong SizeRemaining;
        }

        static unsafe List<VolumeRec> EnumerateVolumes()
        {
            var vols = new List<VolumeRec>();

            // Buffers allocated once and reused; stackalloc lives for the whole method frame.
            const int cap = 260;
            char* nameBuf = stackalloc char[cap];
            char* pathBuf = stackalloc char[cap];

            IntPtr find = FindFirstVolumeW(nameBuf, cap);
            if (find == new IntPtr(-1)) return vols;

            try
            {
                do
                {
                    string volumeGuidPath = ReadVolumeName(nameBuf);

                    uint pathLen = 0;
                    if (!GetVolumePathNamesForVolumeNameW(volumeGuidPath, pathBuf, cap, &pathLen))
                        continue;

                    string root = new string(pathBuf); // first mount point
                    if (string.IsNullOrEmpty(root))
                        continue;

                    ulong total = 0, free = 0;
                    if (!GetDiskFreeSpaceExW(root, null, &total, &free) || total == 0)
                        continue;

                    var diskNumbers = GetVolumeDiskNumbers(volumeGuidPath);
                    if (diskNumbers.Length == 0)
                        continue;

                    vols.Add(new VolumeRec { DiskNumbers = diskNumbers, Size = total, SizeRemaining = free });
                }
                while (FindNextVolumeW(find, nameBuf, cap));
            }
            finally
            {
                FindVolumeClose(find);
            }

            return vols;
        }

        void RunDiskQuery()
        {
            var volumes = EnumerateVolumes();

            var result = new List<PhysicalDiskInfo>();
            var diskNumberToIndex = new Dictionary<uint, int>();

            for (uint n = 0; n < MAX_PHYSICAL_DRIVE; n++)
            {
                using SafeFileHandle h = OpenDevice($@"\\.\PhysicalDrive{n}");
                if (h.IsInvalid) continue;

                var info = new PhysicalDiskInfo();

                if (TryGetDiskLength(h, out var size))
                    info.Size = size;
                if (TryGetSectorSizes(h, out var logical, out var physical))
                {
                    info.LogicalSectorSize = logical;
                    info.PhysicalSectorSize = physical;
                }
                if (TryGetMediaType(h, out var media))
                    info.MediaType = media;
                if (TryGetHealth(h, out var health))
                    info.HealthStatus = health;

                diskNumberToIndex[n] = result.Count;
                result.Add(info);
            }

            foreach (var vr in volumes)
            {
                foreach (var dn in vr.DiskNumbers)
                {
                    if (!diskNumberToIndex.TryGetValue(dn, out int idx)) continue;
                    var disk = result[idx];
                    var grown = new VolumeInfo[disk.Volumes.Length + 1];
                    disk.Volumes.CopyTo(grown, 0);
                    grown[disk.Volumes.Length] = new VolumeInfo { Size = vr.Size, SizeRemaining = vr.SizeRemaining };
                    disk.Volumes = grown;
                }
            }

            var arr = result.ToArray();
            lock (_gate) { _disks = arr; }
        }

        // ========================================================================
        // Shared lifecycle
        // ========================================================================
        void Worker()
        {
            while (_started)
            {
                _wake.WaitOne();
                while (_started)
                {
                    long now = Environment.TickCount64;
                    bool cpuActive = now - Volatile.Read(ref _cpuLastAccess) < IdleMs;
                    bool diskActive = now - Volatile.Read(ref _diskLastAccess) < IdleMs;
                    if (!cpuActive && !diskActive) break; // park

                    if (cpuActive) { try { RunCpuSample(); } catch { } }
                    if (diskActive) { try { RunDiskQuery(); } catch { } }

                    if (_stop.Wait(1000)) break;
                }
            }
        }

        protected override void OnStart()
        {
            if (_started) return;

            _stop.Reset();
            _started = true;

            // CPU: baseline + second sample after a short interval so load is real at once.
            SeedCpuStatic();
            Thread.Sleep(SeedIntervalMs);
            RunCpuSample();

            // Disk: initial enumeration.
            RunDiskQuery();

            // No accessor activity yet -> stay parked until something reads.
            Volatile.Write(ref _cpuLastAccess, 0);
            Volatile.Write(ref _diskLastAccess, 0);

            // ---- publish CPU ----
            var cpuSnapshot = _cpuEntries;
            if (cpuSnapshot != null)
            {
                for (int i = 0; i < cpuSnapshot.Length; i++)
                {
                    var acc = new CpuEntryAccessor(this, i);
                    Publish($"/cpu/{i}/maxclock", "MHz", new Func<float>(() => acc.MaxFrequency));
                }
            }
            var coreSnapshot = _coreEntries;
            if (coreSnapshot != null)
            {
                for (int i = 0; i < coreSnapshot.Length; i++)
                {
                    CpuCoreEntry entry = coreSnapshot[i];
                    var acc = new CoreEntryAccessor(this, i);
                    Publish($"/cpu/{entry.CpuIndex}/thread/{entry.ThreadIndex}/clock", "MHz",
                        new Func<float>(() => acc.Frequency));
                    Publish($"/cpu/{entry.CpuIndex}/thread/{entry.ThreadIndex}/load", "%",
                        new Func<float>(() => acc.Load));
                }
            }

            // ---- publish RAM ----
            Publish("/ram/total", "MB", new Func<float>(() => SafeTotal));
            Publish("/ram/free", "MB", new Func<float>(() => SafeFree));
            Publish("/ram/used", "MB", new Func<float>(() => SafeUsed));
            Publish("/ram/free/virtual", "MB", new Func<float>(() => SafeFreeVirtual));
            Publish("/ram/load", "%", new Func<float>(() => SafeLoad));

            // ---- publish DISK ----
            var disks = _disks;
            if (disks != null)
            {
                for (int i = 0; i < disks.Length; i++)
                {
                    var acc = new DiskAccessor(this, i);
                    Publish($"/disk/{i}/health", "STAT", new Func<float>(() => acc.Health));
                    Publish($"/disk/{i}/type", "", new Func<float>(() => acc.Type));
                    Publish($"/disk/{i}/load", "%", new Func<float>(() => acc.Load));
                    Publish($"/disk/{i}/total", "MB", new Func<float>(() => acc.Size));
                    Publish($"/disk/{i}/sector_size", "MB", new Func<float>(() => acc.PhysicalSectorSize));
                    Publish($"/disk/{i}/logical_sector_size", "MB", new Func<float>(() => acc.LogicalSectorSize));

                    var vols = disks[i].Volumes;
                    for (int j = 0; j < vols.Length; j++)
                    {
                        var vacc = new VolumeAccessor(this, i, j);
                        Publish($"/disk/{i}/volume/{j}/total", "MB", new Func<float>(() => vacc.Total));
                        Publish($"/disk/{i}/volume/{j}/free", "MB", new Func<float>(() => vacc.Free));
                        Publish($"/disk/{i}/volume/{j}/used", "MB", new Func<float>(() => vacc.Used));
                        Publish($"/disk/{i}/volume/{j}/load", "%", new Func<float>(() => vacc.Load));
                    }
                }
            }

            _worker = new Thread(Worker) { IsBackground = true, Name = "win32_hw_refresh" };
            _worker.Start();
        }

        protected override void OnStop()
        {
            if (!_started) return;
            _started = false;
            _stop.Set();
            _wake.Set();
            _worker?.Join();
            _worker = null;

            lock (_gate)
            {
                _cpuEntries = null;
                _coreEntries = null;
                _disks = null;
            }
        }
    }

    static class CpuTopology
    {
        private const int RelationProcessorPackage = 3;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetLogicalProcessorInformationEx(
            int RelationshipType, IntPtr Buffer, ref int ReturnedLength);

        public readonly struct Package
        {
            public readonly int Index;
            public readonly (ushort Group, ulong Mask)[] Affinities;
            public Package(int index, (ushort, ulong)[] aff) { Index = index; Affinities = aff; }

            public bool Contains(ushort group, int processor)
            {
                foreach (var a in Affinities)
                    if (a.Group == group && (a.Mask & (1UL << processor)) != 0) return true;
                return false;
            }
        }

        public static List<Package> GetPackages()
        {
            int len = 0;
            if (GetLogicalProcessorInformationEx(RelationProcessorPackage, IntPtr.Zero, ref len))
                throw new InvalidOperationException("Unexpected success probing length.");
            if (Marshal.GetLastWin32Error() != ERROR_INSUFFICIENT_BUFFER)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            IntPtr buffer = Marshal.AllocHGlobal(len);
            try
            {
                if (!GetLogicalProcessorInformationEx(RelationProcessorPackage, buffer, ref len))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

                var packages = new List<Package>();
                int offset = 0, index = 0;

                while (offset < len)
                {
                    IntPtr rec = IntPtr.Add(buffer, offset);
                    int size = Marshal.ReadInt32(rec, 4);
                    ushort groupCount = (ushort)Marshal.ReadInt16(rec, 30);

                    var aff = new (ushort, ulong)[groupCount];
                    for (int g = 0; g < groupCount; g++)
                    {
                        int b = 32 + g * 16;
                        ulong mask = (ulong)(long)Marshal.ReadIntPtr(rec, b);
                        ushort grp = (ushort)Marshal.ReadInt16(rec, b + 8);
                        aff[g] = (grp, mask);
                    }
                    packages.Add(new Package(index++, aff));
                    offset += size;
                }
                return packages;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }
}
