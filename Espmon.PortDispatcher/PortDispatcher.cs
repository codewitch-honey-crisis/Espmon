
using HWKit;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;

namespace Espmon;

[SupportedOSPlatform("windows")]
public partial class PortDispatcher : Component
{
    const int OpenInterval = 5 * 1000;
    readonly SynchronizationContext? _synchronizationContext;
    private readonly Dictionary<Device, string> _deviceToFile = new();
    private const string deviceFileSuffix = ".device.json";
    private const string deviceFilePattern = $"*{deviceFileSuffix}";
    private const string screenFileSuffix = ".screen.json";
    private bool _attemptedOpenDevices = false;
    private long _intervalMs=DefaultInterval;
    private string _path;
    public static int DefaultInterval => 100;
    public ObservableCollection<Session> Sessions { get; } = new ObservableCollection<Session>();
    public HardwareInfoCollection HardwareInfo { get; }
    private FileSystemWatcher _fsw;
    private Timer _timer;
    private Timer _openTimer;
    private void Post(Action action)
    {
        if (_synchronizationContext != null)
        {
            _synchronizationContext.Post(_ => action(), null);
        }
        else
        {
            action();
        }
    }
    
    public static int BaudRate => 115200;

    public static FirmwareEntry[] GetFirmwareEntries()
    {
        using var stm = Assembly.GetExecutingAssembly().GetManifestResourceStream("Espmon.firmware.boards.json");
        if (stm == null) throw new InvalidProgramException("The boards resource could not be found");
        var reader = new StreamReader(stm, Encoding.UTF8);
        var doc = (JsonObject?)JsonObject.ReadFrom(reader);
        if (doc == null) throw new InvalidProgramException("The boards resource is invalid");
        if (!doc.TryGetValue("boards", out var boards) || !(boards is JsonArray boardsArray))
        {
            throw new InvalidProgramException("The boards resource is invalid");
        }
        var firmwareEntrys = new List<FirmwareEntry>();
        foreach (var board in boardsArray)
        {

            if (!(board is JsonObject boardObj)) { throw new InvalidProgramException("The boards resource is invalid"); }
            if (!boardObj.TryGetValue("name", out var name) || !(name is string displayName)) { throw new InvalidProgramException("The boards resource is invalid"); }
            if (!boardObj.TryGetValue("slug", out var sluggo) || !(sluggo is string slug)) { throw new InvalidProgramException("The boards resource is invalid"); }
            if (!boardObj.TryGetValue("offsets", out var offsetso) || !(offsetso is JsonObject offsetsObj)) { throw new InvalidProgramException("The boards resource is invalid"); }
            if (!offsetsObj.TryGetValue("bootloader", out var bootloader) || !(bootloader is double bootloaderObj)) { throw new InvalidProgramException("The boards resource is invalid"); }
            if (!offsetsObj.TryGetValue("partitions", out var partitions) || !(partitions is double partitionsObj)) { throw new InvalidProgramException("The boards resource is invalid"); }
            if (!offsetsObj.TryGetValue("firmware", out var firmware) || !(firmware is double firmwareObj)) { throw new InvalidProgramException("The boards resource is invalid"); }
            var entry = new FirmwareEntry(displayName, slug, new FirmwareOffsets((uint)bootloaderObj, (uint)partitionsObj, (uint)firmwareObj));
            firmwareEntrys.Add(entry);
        }
        return firmwareEntrys.ToArray();
    }
    public string[] GetDeviceSerialNumbers()
    {
        var result = new List<string>(_deviceToFile.Count);
        foreach(var str in _deviceToFile.Values)
        {
            var name = GetSerialNumber(str);
            if(name!=null)
            {
                result.Add(name);
            }
        }
        return result.ToArray();
    }
    
    private string? GetSerialNumber(string? path)
    {
        if (path != null)
        {
            if (path.EndsWith(deviceFileSuffix))
            {
                return Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path)));
            }
        }
        return null;
    }
    private string? GetScreenName(string? path)
    {
        if (path != null)
        {
            if (path.EndsWith(screenFileSuffix))
            {
                return Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
            }
        }
        return null;
    }

    private void LoadAllDevices()
    {
        var deviceFiles = Directory.GetFiles(_path, deviceFilePattern);
        foreach (var filePath in deviceFiles)
        {
            LoadDevice(filePath);
        }
    }
    public void Refresh()
    {
        foreach (var session in Sessions)
        {
            session.Update();
        }
    }
    private void LoadDevice(string path)
    {
        if (!_deviceToFile.Values.Contains(path))
        {
            // Load the updated screen
            using var reader = new StreamReader(File.OpenRead(path), Encoding.UTF8);
            try
            {
                var device = Device.ReadFrom(reader);
                device.HardwareInfo = HardwareInfo;
                _deviceToFile.Add(device, path);
            }
            catch
            {
                reader.Close();
                File.Delete(path);
            }
        }
    }
    public TimeSpan Interval
    {
        get
        {
            return TimeSpan.FromMilliseconds(_intervalMs);
        } set
        {
            var ms = value.TotalMilliseconds;
            if (ms != _intervalMs)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(ms, 100);
                _intervalMs = (int)ms;
                _timer.Dispose();
                _timer = new Timer(_timer_Tick, this, Interval, Interval);
            }
        }
    }
    private void _fsw_FileCreated(object sender, FileSystemEventArgs e)
    {
        if (e.Name != null)
        {
            if (e.Name.EndsWith(deviceFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                for (var i = 0; i < 10; ++i)
                {
                    try
                    {
                        LoadDevice(e.FullPath);
                    }
                    catch (IOException)
                    {
                        Task.Delay(1000).Wait();
                        continue;
                    }
                    RefreshAllSessions();
                    break;
                }
            }
        }
    }
    private void RefreshSessionScreens(string? name)
    {
        foreach (var session in Sessions)
        {
            switch (session.Status)
            {
                case SessionStatus.Closed:
                case SessionStatus.RequiresFlash:
                    continue;
                default:
                    break;
            }
            if (name != null)
            {
                session.RefreshScreen(name);
            }
        }
    }
    private void _fsw_FileChanged(object sender, FileSystemEventArgs e)
    {
        if (e.Name != null) {
            if (e.Name.EndsWith(deviceFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                // Find and remove the old device associated with this file
                Device? device = null;
                Post(() =>
                {
                    device = _deviceToFile.FirstOrDefault(kvp => kvp.Value == e.FullPath).Key;
                    if (device != null)
                    {
                        device.Dispose();
                        _deviceToFile.Remove(device);
                        device = null;
                    }
                });

                LoadDevice(e.FullPath);
                RefreshAllSessions();
            }
            if (e.Name.EndsWith(screenFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                Post(() =>
                {
                    RefreshSessionScreens(GetScreenName(e.Name));
                });
            }
        }
    }

    private void _fsw_FileDeleted(object sender, FileSystemEventArgs e)
    {
        if (e.Name != null)
        {
            if (e.Name.EndsWith(deviceFileSuffix, StringComparison.OrdinalIgnoreCase))
            {

                Post(() =>
                {
                    var device = _deviceToFile.FirstOrDefault(kvp => kvp.Value == e.FullPath).Key;
                    if (device != null)
                    {
                        device.Dispose();
                        _deviceToFile.Remove(device);
                        RefreshAllSessions();
                    }
                });
            }
            if (e.Name.EndsWith(screenFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                Post(() =>
                {
                    RefreshSessionScreens(GetScreenName(e.Name));
                });
            }
        }
    }

    private void _fsw_FileRenamed(object sender, RenamedEventArgs e)
    {
        if (e.Name != null)
        {
            if (e.Name.EndsWith(deviceFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                // Check if the new name matches the pattern
                // Update the file path in the dictionary
                Post(() =>
                {
                    var device = _deviceToFile.FirstOrDefault(kvp => kvp.Value == e.OldFullPath).Key;
                    if (device != null)
                    {
                        _deviceToFile[device] = e.FullPath;
                        RefreshAllSessions();
                    }
                });
            }
            if (e.Name.EndsWith(screenFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                // Update the file path in the dictionary
                Post(() =>
                {
                    RefreshSessionScreens(GetScreenName(e.OldName));
                });
            }
        }
    }
    private void InitFileSystemWatcher()
    {
        _fsw.EnableRaisingEvents = false;
        _fsw.Path = _path;
        _fsw.Filter = "*.json";
        _fsw.Changed += _fsw_FileChanged;
        _fsw.Created += _fsw_FileCreated;
        _fsw.Deleted += _fsw_FileDeleted;
        _fsw.Renamed += _fsw_FileRenamed;
    }
    private static void _timer_Tick(object? state)
    {
        if(state is PortDispatcher pd) {
            pd.Post(() => {
                if (pd._fsw.EnableRaisingEvents)
                {
                    pd.Refresh();
                }
            });
        }
    }
    private static void _openTimer_Tick(object? state)
    {
        if (state is PortDispatcher pd)
        {
            pd.Post(() => {
                if (pd._fsw.EnableRaisingEvents)
                {
                    pd.TryOpenPorts();
                }
            });
        }
    }
    public PortDispatcher(string path,SynchronizationContext? syncContext = null)
    {
        _path = path;
        if (syncContext == null) syncContext = SynchronizationContext.Current;
        _fsw = new FileSystemWatcher();
        InitFileSystemWatcher();
        HardwareInfo = new();
        InitializeComponent();
        _synchronizationContext = syncContext;        
        LoadAllDevices();
        RefreshAllSessions();
        _timer = new Timer(_timer_Tick, this, Interval, Interval);
        _openTimer = new Timer(_openTimer_Tick, this, OpenInterval, OpenInterval);
    }

    public PortDispatcher(IContainer container, string path, SynchronizationContext? syncContext = null)
    {
        _path = path;
        if (syncContext == null) syncContext = SynchronizationContext.Current;
        _fsw = new FileSystemWatcher();
        InitFileSystemWatcher();
        HardwareInfo = new();
        container.Add(this);
        InitializeComponent();
        _synchronizationContext = syncContext;
        LoadAllDevices();
        RefreshAllSessions();
        _timer = new Timer(_timer_Tick, this, Interval, Interval);
        _openTimer = new Timer(_openTimer_Tick, this, OpenInterval, OpenInterval);
    }
    private Session? FindSessionEntryBySerial(string serial)
    {
        if (Sessions == null) return null;
        for (var i = 0; i < Sessions.Count; ++i)
        {
            var session = Sessions[i];
            if (session.SerialNumber.Equals(serial, StringComparison.OrdinalIgnoreCase))
            {
                return session;
            }
        }
        return null;
    }
    public bool IsStarted
    {
        get { return _fsw.EnableRaisingEvents; }
    }
    public void Start()
    {
        if(_fsw.EnableRaisingEvents) { return; }
        HardwareInfo.StartAll();
        _fsw.EnableRaisingEvents = true;
        TryOpenPorts();
    }
    static string? FindPortNameBySerial(PortEntry[] entries,string serialNumber)
    {
        for(var i = 0;i<entries.Length;++i)
        {
            if (entries[i].SerialNumber.Equals(serialNumber,StringComparison.OrdinalIgnoreCase))
            {
                return entries[i].PortName;
            }
        }
        return null;
    }
    private void TryOpenPorts()
    {
        var serialPorts = EspSerialSession.GetPorts();
        var savedSerialNumbers = GetDeviceSerialNumbers();
        for (int i = 0; i < savedSerialNumbers.Length; i++)
        {
            var serialNo = savedSerialNumbers[i];
            var se = FindSessionEntryBySerial(serialNo);
            if (se != null)
            {
                var pn = FindPortNameBySerial(serialPorts, serialNo);
                if (pn!=null)
                {
                    try
                    {
                        se.Open();
                    }
                    catch
                    {

                    }
                }
            }
        }
    }
    public void Stop()
    {
        if (!_fsw.EnableRaisingEvents) { return; }
        _fsw.EnableRaisingEvents = false;
        for (var i = 0; i < Sessions.Count;++i)
        {
            Sessions[i].Close();
        }
        HardwareInfo.StopAll();
    }
    public void RefreshAllSessions()
    {
        var hash = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Session>();
        var serialPorts = EspSerialSession.GetPorts();
        var savedSerials = GetDeviceSerialNumbers();
        for (int i = 0; i < savedSerials.Length; i++)
        {
            var serialNo = savedSerials[i];

            if (hash.Add(serialNo))
            {
                var se = FindSessionEntryBySerial(serialNo);
                if (se != null)
                {
                    result.Add(se);
                }
                else
                {
                    var pn = FindPortNameBySerial(serialPorts,serialNo);
                    if (pn != null)
                    {
                        var session = new Session(_path,pn, serialNo);
                        session.HardwareInfo = HardwareInfo;
                        result.Add(session);
                    }
                }
            }
            else if (_attemptedOpenDevices)
            {
                var idx = Array.IndexOf(serialPorts, serialNo);
                var se = result[idx];
                try
                {
                    if (se.Status == SessionStatus.Closed) 
                    {
                        se.Open();
                    }
                }
                catch { }
            }
        }
        for (var i = 0; i < serialPorts.Length; ++i)
        {
            if (hash.Add(serialPorts[i].SerialNumber))
            {
                var se = FindSessionEntryBySerial(serialPorts[i].SerialNumber);

                if (se != null)
                {
                    result.Add(se);
                }
                else
                {
                    var session = new Session(_path, serialPorts[i].PortName, serialPorts[i].SerialNumber);
                    session.HardwareInfo = HardwareInfo;
                    result.Add(session);
                }
            }
        }
        var toAdd = new List<Session>(Sessions.Count);
        var toRemove = new List<Session>(Sessions.Count);
        for (var i = 0;i<result.Count;++i)
        {
            var idx = Sessions.IndexOf(result[i]);
            if (idx > -1)
            {
                Sessions[idx] = result[i];
            }
            else
            {
                toAdd.Add(result[i]);
            }
        }
        for(var i = 0;i<Sessions.Count;++i)
        {
            var se = Sessions[i];
            if(!result.Contains(Sessions[i]))
            {
                toRemove.Add(se);
            }
        }
        for (var i = 0; i < toRemove.Count; ++i)
        {
            Sessions.Remove(toRemove[i]);
        }
        for (var i = 0; i < toAdd.Count; ++i)
        {
            Sessions.Add(toAdd[i]);
        }
        
    }
    // required because HardwareInfoCollection does not implement IComponent
    sealed class Disposer : IComponent
    {
        PortDispatcher _parent;
        public Disposer(PortDispatcher parent)
        {
            _parent = parent;
        }

        ISite? IComponent.Site { get; set; } = null;

        public event EventHandler? Disposed;

        public void Dispose()
        {
            _parent._timer.Dispose();
            _parent._openTimer.Dispose();
            foreach(var session in _parent.Sessions)
            {
                session.Close();
                session.Update();
                session.Dispose();
            }
            _parent.Stop();
            foreach (var device in this._parent._deviceToFile.Keys)
            {
                device.Dispose();
            }
            if (_parent.HardwareInfo != null)
            {
                _parent.HardwareInfo?.Dispose();
            }
            Disposed?.Invoke(this, EventArgs.Empty);
        }

    };

}
