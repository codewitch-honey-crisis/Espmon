using EL;

using HWKit;

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Espmon;

public partial class PortDispatcher : Component
{
    readonly SynchronizationContext _synchronizationContext;
    private readonly Dictionary<Device, string> _deviceToFile = new();
    private const string deviceFileSuffix = ".device.json";
    private const string deviceFilePattern = $"*{deviceFileSuffix}";
    private const string screenFileSuffix = ".screen.json";
    private bool _attemptedOpenDevices = false;
    public ObservableCollection<Session> Sessions { get; } = new ObservableCollection<Session>();
    public HardwareInfoCollection HardwareInfo { get; }
    private FileSystemWatcher _fsw;

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
    public const int BaudRate = 115200;

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
    public string[] GetDevicePortNames()
    {
        var result = new List<string>(_deviceToFile.Count);
        foreach(var str in _deviceToFile.Values)
        {
            var name = GetPortName(str);
            if(name!=null)
            {
                result.Add(name);
            }
        }
        return result.ToArray();
    }
    
    private string? GetPortName(string? path)
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
        var path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Espmon");
        var deviceFiles = Directory.GetFiles(path, deviceFilePattern);
        foreach (var filePath in deviceFiles)
        {
            LoadDevice(filePath);
        }
    }
    public void Refresh()
    {
        foreach(var session in Sessions)
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
        var path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Espmon");
        _fsw.EnableRaisingEvents = false;
        _fsw.Path = path;
        _fsw.Filter = "*.json";
        _fsw.Changed += _fsw_FileChanged;
        _fsw.Created += _fsw_FileCreated;
        _fsw.Deleted += _fsw_FileDeleted;
        _fsw.Renamed += _fsw_FileRenamed;
    }
    public PortDispatcher(SynchronizationContext? syncContext = null)
    {
        if (syncContext == null) syncContext = SynchronizationContext.Current;
        ArgumentNullException.ThrowIfNull(syncContext, nameof(syncContext));
        _fsw = new FileSystemWatcher();
        InitFileSystemWatcher();
        HardwareInfo = new();
        InitializeComponent();
        _synchronizationContext = syncContext;        
        LoadAllDevices();
        RefreshAllSessions();
    }

    public PortDispatcher(IContainer container, SynchronizationContext? syncContext = null)
    {
        if (syncContext == null) syncContext = SynchronizationContext.Current;
        ArgumentNullException.ThrowIfNull(syncContext, nameof(syncContext));
        _fsw = new FileSystemWatcher();
        InitFileSystemWatcher();
        HardwareInfo = new();
        container.Add(this);
        InitializeComponent();
        _synchronizationContext = syncContext;
        LoadAllDevices();
        RefreshAllSessions();
    }
    private Session? FindSessionEntryByPort(string port)
    {
        if (Sessions == null) return null;
        for (var i = 0; i < Sessions.Count; ++i)
        {
            var session = Sessions[i];
            if (session.PortName.Equals(port, StringComparison.OrdinalIgnoreCase))
            {
                return session;
            }
        }
        return null;
    }
    public void Start()
    {
        if(_fsw.EnableRaisingEvents) { return; }
        HardwareInfo.StartAll();
        var serialPorts = EspLink.GetPorts();
        var savedPorts = GetDevicePortNames();
        for (int i = 0; i < savedPorts.Length; i++)
        {
            var port = savedPorts[i];
            var se = FindSessionEntryByPort(port);
            if (se != null)
            {
                if (serialPorts.Contains(port, StringComparer.OrdinalIgnoreCase))
                {
                    se.Open();
                }
            }
        }
        _fsw.EnableRaisingEvents = true;
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
        var serialPorts = EspLink.GetPorts();
        var savedPorts = GetDevicePortNames();
        for (int i = 0; i < savedPorts.Length; i++)
        {
            var port = savedPorts[i];

            if (hash.Add(port))
            {
                var se = FindSessionEntryByPort(port);
                if (se != null)
                {
                    result.Add(se);
                }
                else
                {
                    var session = new Session(new SerialPort(port, BaudRate));
                    session.HardwareInfo = HardwareInfo;
                    result.Add(session);
                }
            }
            else if (_attemptedOpenDevices)
            {
                var idx = Array.IndexOf(serialPorts, port);
                var se = result[idx];
                se.Open();
            }
        }
        for (var i = 0; i < serialPorts.Length; ++i)
        {
            if (hash.Add(serialPorts[i]))
            {
                var se = FindSessionEntryByPort(serialPorts[i]);

                if (se != null)
                {
                    result.Add(se);
                }
                else
                {
                    var session = new Session(new SerialPort(serialPorts[i], BaudRate));
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
