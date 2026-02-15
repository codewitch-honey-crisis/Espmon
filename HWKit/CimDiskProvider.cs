
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
        
        sealed class DiskAccessor
        {
            Mutex _mutex;
            int _index;
            PhysicalDiskInfo[] _entries;
            public DiskAccessor(Mutex mutex, int index, PhysicalDiskInfo[] entries) { ArgumentNullException.ThrowIfNull(mutex, nameof(mutex)); _mutex = mutex; _index = index; ArgumentNullException.ThrowIfNull(entries, nameof(entries)); _entries = entries; }
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
            public float Load
            {
                get
                {
                    try
                    {
                        _mutex.WaitOne();
                        double totalSize=0;
                        double totalRemaining = 0;
                        var d = _entries[_index];
                        for(var i = 0;i<d.Volumes.Length;++i)
                        {
                            var v = d.Volumes[i];
                            if (v == null) continue;
                            totalSize += v.Size;
                            totalRemaining += v.SizeRemaining;
                        }
                        return (float)((1.0-(totalRemaining / totalSize)) * 100.0f);
                    }
                    finally
                    {
                        _mutex.ReleaseMutex();
                    }
                }
            }
            public float SlotNumber
            {
                get {
                    try
                    {
                        _mutex.WaitOne();
                        return ValueOrNaN(_entries[_index].SlotNumber);
                    }
                    finally
                    {
                        _mutex.ReleaseMutex();
                    }
                }
            }
            public float Health 
            { 
                get
                {
                    try
                    {
                        _mutex.WaitOne();
                        return ValueOrNaN(_entries[_index].HealthStatus);
                    }
                    finally
                    {
                        _mutex.ReleaseMutex();
                    }                    
                }  
            }
            public float Type
            {
                get
                {
                    try
                    {
                        _mutex.WaitOne();
                        return ValueOrNaN(_entries[_index].MediaType);
                    }
                    finally
                    {
                        _mutex.ReleaseMutex();
                    }
                }
            }
            public float Size
            {
                get
                {
                    try
                    {
                        _mutex.WaitOne();
                        return ValueOrZero(_entries[_index].Size)/(1024.0f*1024.0f);
                    }
                    finally
                    {
                        _mutex.ReleaseMutex();
                    }
                }
            }
            public float AllocatedSize
            {
                get
                {
                    try
                    {
                        _mutex.WaitOne();
                        return ValueOrZero(_entries[_index].AllocatedSize) / (1024.0f * 1024.0f);
                    }
                    finally
                    {
                        _mutex.ReleaseMutex();
                    }
                }
            }
            public float SpindleRpm
            {
                get
                {
                    try
                    {
                        _mutex.WaitOne();
                        return ValueOrNaN(_entries[_index].SpindleSpeed);
                    }
                    finally
                    {
                        _mutex.ReleaseMutex();
                    }
                }
            }
            public float PhysicalSectorSize
            {
                get
                {
                    try
                    {
                        _mutex.WaitOne();
                        return ValueOrZero(_entries[_index].PhysicalSectorSize) / (1024.0f * 1024.0f);
                    }
                    finally
                    {
                        _mutex.ReleaseMutex();
                    }
                }
            }
            public float LogicalSectorSize
            {
                get
                {
                    try
                    {
                        _mutex.WaitOne();
                        return ValueOrZero(_entries[_index].LogicalSectorSize) / (1024.0f * 1024.0f);
                    }
                    finally
                    {
                        _mutex.ReleaseMutex();
                    }
                }
            }
        }
        sealed class VolumeAccessor
        {
            Mutex _mutex;
            int _diskIndex;
            int _volumeIndex;
            PhysicalDiskInfo[] _entries;
            public VolumeAccessor(Mutex mutex, int diskIndex, int volumeIndex, PhysicalDiskInfo[] entries)
            {
                _mutex = mutex;
                _diskIndex = diskIndex;
                _volumeIndex = volumeIndex;
                _entries = entries;
            }
            public float Load
            {
                get
                {
                    try
                    {
                        _mutex.WaitOne();
                        var v = _entries[_diskIndex].Volumes[_volumeIndex];
                        return (v.SizeRemaining / v.Size)*100.0f;
                    }
                    catch
                    {
                        return float.NaN;
                    }
                    finally
                    {
                        _mutex.ReleaseMutex();
                    }
                }
            }
            public float Total
            {
                get
                {
                    try
                    {
                        _mutex.WaitOne();
                        var v = _entries[_diskIndex].Volumes[_volumeIndex];
                        return (v.Size / (1024.0f * 1024.0f));
                    }
                    catch
                    {
                        return float.NaN;
                    }
                    finally
                    {
                        _mutex.ReleaseMutex();
                    }
                }
            }
            public float Free
            {
                get
                {
                    try
                    {
                        _mutex.WaitOne();
                        var v = _entries[_diskIndex].Volumes[_volumeIndex];
                        return (v.SizeRemaining / (1024.0f * 1024.0f));
                    }
                    catch
                    {
                        return float.NaN;
                    }
                    finally
                    {
                        _mutex.ReleaseMutex();
                    }
                }
            }
        }
        Mutex? _mutex;
        bool _started = false;
        PhysicalDiskInfo[]? _entries = null;
        protected override string GetDisplayName()
        {
            return "Windows CIM Disk Provider";
        }
        protected override string GetIdentifier()
        {
            return "cim_disk";
        }
        protected override HardwareInfoProviderState GetState()
        {

            return _started ? HardwareInfoProviderState.Started : HardwareInfoProviderState.Stopped;
        }
        private static T? ValueOrDef<T>(object? value, T? def = default)
        {
            if(value is T val)
            {
                if(val!=null)
                {
                    return val;
                }
            }
            return def;
        }
        
        void RunQuery()
        {
            if (_mutex == null) return;
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
                        AllocatedSize=ValueOrDef<ulong>(physDisk.CimInstanceProperties["AllocatedSize"]?.Value)
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

                                    volumesNew[info.Volumes.Length]=new VolumeInfo
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
                if (_mutex != null)
                {
                    _mutex.WaitOne();
                    try
                    {
                        _entries = physicalDiskInfos.ToArray();
                    }
                    finally
                    {
                        _mutex.ReleaseMutex();
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
            _mutex = new Mutex();

            var thread = new Thread(() =>
            {
                _mutex.WaitOne();
                var started = _started;
                _mutex.ReleaseMutex();
                while (started && _mutex != null)
                {
                    if(_mutex==null)
                    {
                        break;
                    }
                    _mutex.WaitOne();
                    started = _started;
                    _mutex.ReleaseMutex();
                    if (started)
                    {
                        RunQuery();
                    }
                    if (_mutex == null)
                    {
                        break;
                    }
                    Thread.Sleep(100);
                }
            });
            RunQuery();

            if (_entries != null)
            {
                for (var i = 0; i < _entries.Length; i++)
                {
                    var acc = new DiskAccessor(_mutex, i, _entries);
                    Publish($"/disk/{i}/slot", "", new Func<float>(() => acc.SlotNumber));
                    Publish($"/disk/{i}/health", "STAT", new Func<float>(() => acc.Health));
                    Publish($"/disk/{i}/type", "", new Func<float>(() => acc.Type));
                    Publish($"/disk/{i}/load", "%", new Func<float>(() => acc.Load));
                    Publish($"/disk/{i}/total", "MB", new Func<float>(() => acc.Size));
                    Publish($"/disk/{i}/allocated", "MB", new Func<float>(() => acc.AllocatedSize));
                    Publish($"/disk/{i}/speed", "RPM", new Func<float>(() => acc.SpindleRpm));
                    Publish($"/disk/{i}/sector_size", "MB", new Func<float>(() => acc.PhysicalSectorSize));
                    Publish($"/disk/{i}/logical_sector_size", "MB", new Func<float>(() => acc.LogicalSectorSize));
                    var vols = _entries[i].Volumes;
                    for(var j = 0; j < vols.Length; j++)
                    {
                        var vacc = new VolumeAccessor(_mutex, i, j, _entries);
                        Publish($"/disk/{i}/volume/{j}/total", "MB", new Func<float>(() => vacc.Total));
                        Publish($"/disk/{i}/volume/{j}/free", "MB", new Func<float>(() => vacc.Free));
                        Publish($"/disk/{i}/volume/{j}/load", "%", new Func<float>(() => vacc.Load));
                    }
                }
            }


            _mutex.WaitOne();
            _started = true;
            _mutex.ReleaseMutex();
            thread.Start();

        }
        protected override void OnStop()
        {
            if (_mutex == null) return;
            _mutex.WaitOne();
            _started = false;
            
            _mutex.ReleaseMutex();
            _mutex?.Dispose();
            _mutex = null;
            

        }

    }

}
