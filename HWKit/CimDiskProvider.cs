using Microsoft.Management.Infrastructure;

using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;


namespace HWKit
{
    [SupportedOSPlatform("windows")]
    public class CimDiskProvider : HardwareInfoProviderBase
    {
        class PhysicalDiskInfo
        {
            public ulong Size { get; set; }
            public ushort? SlotNumber { get; set; }
            public ushort? HealthStatus { get; set; }
            public ushort? MediaType { get; set; }
            public ushort? Usage { get; set; }
            public ulong? AllocatedSize { get; set; }
            public uint? SpindleSpeed { get; set; }
            public ulong? PhysicalSectorSize { get; set; }
            public ulong? LogicalSectorSize { get; set; }

            public VolumeInfo[] Volumes { get; set; } = Array.Empty<VolumeInfo>();
        }

        class VolumeInfo
        {
            public ulong Size { get; set; }
            public ulong SizeRemaining { get; set; }
        }

        // Reads the owning provider's current entry array live (under its lock), so it
        // always reflects the latest refresh and records accessor activity via Touch().
        sealed class DiskAccessor
        {
            readonly CimDiskProvider _owner;
            readonly int _index;
            public DiskAccessor(CimDiskProvider owner, int index)
            {
                ArgumentNullException.ThrowIfNull(owner, nameof(owner));
                _owner = owner;
                _index = index;
            }
            static float ValueOrNaN<T>(T? value)
            {
                if (value == null) return float.NaN;
                try
                {
                    return Convert.ToSingle(value);
                }
                catch
                {
                    return float.NaN;
                }
            }
            static float ValueOrZero<T>(T? value)
            {
                if (value == null) return 0f;
                try
                {
                    return Convert.ToSingle(value);
                }
                catch
                {
                    return 0f;
                }
            }
            float Read(Func<PhysicalDiskInfo, float> selector)
            {
                _owner.Touch();
                _owner._dataMutex.WaitOne();
                try
                {
                    var entries = _owner._entries;
                    if (entries == null || _index < 0 || _index >= entries.Length)
                    {
                        return float.NaN;
                    }
                    return selector(entries[_index]);
                }
                catch
                {
                    return float.NaN;
                }
                finally
                {
                    _owner._dataMutex.ReleaseMutex();
                }
            }
            public float Load => Read(d =>
            {
                double totalSize = 0;
                double totalRemaining = 0;
                for (var i = 0; i < d.Volumes.Length; ++i)
                {
                    var v = d.Volumes[i];
                    if (v == null) continue;
                    totalSize += v.Size;
                    totalRemaining += v.SizeRemaining;
                }
                if (totalSize <= 0) return float.NaN;
                return (float)((1.0 - (totalRemaining / totalSize)) * 100.0f);
            });
            public float SlotNumber => Read(d => ValueOrNaN(d.SlotNumber));
            public float Health => Read(d => ValueOrNaN(d.HealthStatus));
            public float Type => Read(d => ValueOrNaN(d.MediaType));
            public float Size => Read(d => ValueOrZero(d.Size) / (1024.0f * 1024.0f));
            public float AllocatedSize => Read(d => ValueOrZero(d.AllocatedSize) / (1024.0f * 1024.0f));
            public float SpindleRpm => Read(d => ValueOrNaN(d.SpindleSpeed));
            public float PhysicalSectorSize => Read(d => ValueOrZero(d.PhysicalSectorSize) / (1024.0f * 1024.0f));
            public float LogicalSectorSize => Read(d => ValueOrZero(d.LogicalSectorSize) / (1024.0f * 1024.0f));
        }
        sealed class VolumeAccessor
        {
            readonly CimDiskProvider _owner;
            readonly int _diskIndex;
            readonly int _volumeIndex;
            public VolumeAccessor(CimDiskProvider owner, int diskIndex, int volumeIndex)
            {
                ArgumentNullException.ThrowIfNull(owner, nameof(owner));
                _owner = owner;
                _diskIndex = diskIndex;
                _volumeIndex = volumeIndex;
            }
            float Read(Func<VolumeInfo, float> selector)
            {
                _owner.Touch();
                _owner._dataMutex.WaitOne();
                try
                {
                    var entries = _owner._entries;
                    if (entries == null || _diskIndex < 0 || _diskIndex >= entries.Length)
                    {
                        return float.NaN;
                    }
                    var vols = entries[_diskIndex].Volumes;
                    if (vols == null || _volumeIndex < 0 || _volumeIndex >= vols.Length)
                    {
                        return float.NaN;
                    }
                    var v = vols[_volumeIndex];
                    if (v == null) return float.NaN;
                    return selector(v);
                }
                catch
                {
                    return float.NaN;
                }
                finally
                {
                    _owner._dataMutex.ReleaseMutex();
                }
            }
            // NOTE: original semantics preserved (integer division of the two ulongs).
            public float Load => Read(v => (v.SizeRemaining / v.Size) * 100.0f);
            public float Total => Read(v => v.Size / (1024.0f * 1024.0f));
            public float Free => Read(v => v.SizeRemaining / (1024.0f * 1024.0f));
        }

        // How fresh the cache must be before an accessor triggers a refresh.
        const long FreshnessMs = 1000;
        // How long with no accessor activity before the refresh loop parks itself.
        const long IdleMs = 5000;

        readonly Mutex _dataMutex = new Mutex();
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);

        Thread? _worker;
        volatile bool _started = false;

        long _lastQuery = 0;
        long _lastAccess = 0;

        PhysicalDiskInfo[]? _entries = null;

        protected override string GetDisplayName()
        {
            return "Windows CIM Disk Provider";
        }
        protected override string GetIdentifier()
        {
            return "cim_disk";
        }
        protected override HardwareInfoProviderStatus GetState()
        {
            return _started ? HardwareInfoProviderStatus.Started : HardwareInfoProviderStatus.Stopped;
        }

        // Called by every accessor. Records activity and nudges the worker if the cache
        // has gone stale. Never blocks on a query.
        void Touch()
        {
            if (!_started) return;
            long now = Environment.TickCount64;
            Volatile.Write(ref _lastAccess, now);
            if (now - Volatile.Read(ref _lastQuery) >= FreshnessMs)
            {
                _wake.Set();
            }
        }

        private static T? ValueOrDef<T>(object? value, T? def = default)
        {
            if (value is T val)
            {
                if (val != null)
                {
                    return val;
                }
            }
            return def;
        }

        void RunQuery()
        {
            PhysicalDiskInfo[] result;
            using (CimSession session = CimSession.Create(null))
            {
                var physicalDisks = session.QueryInstances(@"root\Microsoft\Windows\Storage", "WQL", "SELECT * FROM MSFT_PhysicalDisk").ToList();
                var disks = session.QueryInstances(@"root\Microsoft\Windows\Storage", "WQL", "SELECT * FROM MSFT_Disk").ToList();
                var partitions = session.QueryInstances(@"root\Microsoft\Windows\Storage", "WQL", "SELECT * FROM MSFT_Partition").ToList();
                var volumes = session.QueryInstances(@"root\Microsoft\Windows\Storage", "WQL", "SELECT * FROM MSFT_Volume").ToList();

                // 2. Build the hierarchy
                var physicalDiskInfos = new List<PhysicalDiskInfo>();

                foreach (var physDisk in physicalDisks)
                {
                    var serialNumber = physDisk.CimInstanceProperties["SerialNumber"]?.Value?.ToString();

                    var info = new PhysicalDiskInfo
                    {
                        HealthStatus = ValueOrDef<ushort>(physDisk.CimInstanceProperties["HealthStatus"]?.Value),
                        MediaType = ValueOrDef<ushort>(physDisk.CimInstanceProperties["MediaType"]?.Value),
                        SlotNumber = ValueOrDef<ushort>(physDisk.CimInstanceProperties["SlotNumber"]?.Value),
                        Usage = ValueOrDef<ushort>(physDisk.CimInstanceProperties["Usage"]?.Value),
                        LogicalSectorSize = ValueOrDef<ulong>(physDisk.CimInstanceProperties["LogicalSectorSize"]?.Value),
                        PhysicalSectorSize = ValueOrDef<ulong>(physDisk.CimInstanceProperties["PhysicalSectorSize"]?.Value),
                        Size = ValueOrDef<ulong>(physDisk.CimInstanceProperties["Size"]?.Value),
                        AllocatedSize = ValueOrDef<ulong>(physDisk.CimInstanceProperties["AllocatedSize"]?.Value)
                    };

                    // Find matching MSFT_Disk by SerialNumber
                    var matchingDisk = disks.FirstOrDefault(d =>
                        d.CimInstanceProperties["SerialNumber"]?.Value?.ToString() == serialNumber);

                    if (matchingDisk != null)
                    {
                        var diskNumber = (uint)(matchingDisk.CimInstanceProperties["Number"]?.Value ?? 0);

                        // Find partitions for this disk
                        var diskPartitions = partitions.Where(p =>
                            (uint)(p.CimInstanceProperties["DiskNumber"]?.Value ?? uint.MaxValue) == diskNumber).ToList();

                        foreach (var partition in diskPartitions)
                        {
                            var driveLetter = partition.CimInstanceProperties["DriveLetter"]?.Value as char?;

                            if (driveLetter.HasValue)
                            {
                                // Find volume by drive letter
                                var volume = volumes.FirstOrDefault(v =>
                                    v.CimInstanceProperties["DriveLetter"]?.Value as char? == driveLetter);

                                if (volume != null)
                                {
                                    var volumesNew = new VolumeInfo[info.Volumes.Length + 1];
                                    info.Volumes.CopyTo(volumesNew, 0);

                                    volumesNew[info.Volumes.Length] = new VolumeInfo
                                    {
                                        Size = ValueOrDef<ulong>(volume.CimInstanceProperties["Size"]?.Value),
                                        SizeRemaining = ValueOrDef<ulong>(volume.CimInstanceProperties["SizeRemaining"]?.Value),
                                    };
                                    info.Volumes = volumesNew;
                                }
                            }
                        }
                    }

                    physicalDiskInfos.Add(info);
                }

                result = physicalDiskInfos.ToArray();
            }

            _dataMutex.WaitOne();
            try
            {
                _entries = result;
            }
            finally
            {
                _dataMutex.ReleaseMutex();
            }
            Volatile.Write(ref _lastQuery, Environment.TickCount64);
        }

        void Worker()
        {
            while (_started)
            {
                _wake.WaitOne();
                while (_started)
                {
                    try
                    {
                        RunQuery();
                    }
                    catch
                    {
                        // Best-effort: keep the last good values on a transient failure.
                    }

                    if (Environment.TickCount64 - Volatile.Read(ref _lastAccess) >= IdleMs)
                    {
                        break;
                    }
                    if (_stop.Wait(1000))
                    {
                        break;
                    }
                }
            }
        }

        protected override void OnStart()
        {
            if (_started)
            {
                return;
            }

            _stop.Reset();
            _started = true;

            // Populate once up front, both to publish valid values immediately and so we
            // know how many disks/volumes to enumerate.
            RunQuery();
            Volatile.Write(ref _lastAccess, 0);

            PhysicalDiskInfo[]? snapshot = _entries;
            if (snapshot != null)
            {
                for (var i = 0; i < snapshot.Length; i++)
                {
                    var acc = new DiskAccessor(this, i);
                    Publish($"/disk/{i}/slot", "", new Func<float>(() => acc.SlotNumber));
                    Publish($"/disk/{i}/health", "STAT", new Func<float>(() => acc.Health));
                    Publish($"/disk/{i}/type", "", new Func<float>(() => acc.Type));
                    Publish($"/disk/{i}/load", "%", new Func<float>(() => acc.Load));
                    Publish($"/disk/{i}/total", "MB", new Func<float>(() => acc.Size));
                    Publish($"/disk/{i}/allocated", "MB", new Func<float>(() => acc.AllocatedSize));
                    Publish($"/disk/{i}/speed", "RPM", new Func<float>(() => acc.SpindleRpm));
                    Publish($"/disk/{i}/sector_size", "MB", new Func<float>(() => acc.PhysicalSectorSize));
                    Publish($"/disk/{i}/logical_sector_size", "MB", new Func<float>(() => acc.LogicalSectorSize));
                    var vols = snapshot[i].Volumes;
                    for (var j = 0; j < vols.Length; j++)
                    {
                        var vacc = new VolumeAccessor(this, i, j);
                        Publish($"/disk/{i}/volume/{j}/total", "MB", new Func<float>(() => vacc.Total));
                        Publish($"/disk/{i}/volume/{j}/free", "MB", new Func<float>(() => vacc.Free));
                        Publish($"/disk/{i}/volume/{j}/load", "%", new Func<float>(() => vacc.Load));
                    }
                }
            }

            _worker = new Thread(Worker)
            {
                IsBackground = true,
                Name = "cim_disk_refresh"
            };
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

            _dataMutex.WaitOne();
            try
            {
                _entries = null;
            }
            finally
            {
                _dataMutex.ReleaseMutex();
            }
        }
        protected override string GetDescription()
        {
            return "Provides disk usage information via the Windows CIM subsystem";
        }
    }

}