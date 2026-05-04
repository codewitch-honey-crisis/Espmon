
using HWKit;

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

using static System.Collections.Specialized.BitVector32;

namespace Espmon;

public sealed class ScreenChangedEventArgs : EventArgs
{
    public int ScreenIndex { get; }
    public ScreenChangedEventArgs(int screenIndex) { ScreenIndex = screenIndex; }
}

public delegate void ScreenChangedEventHandler(object sender, ScreenChangedEventArgs args);

public delegate void ScreenClearedEventHandler(object sender, EventArgs args);
public delegate void SessionChangedEventHandler(object sender, EventArgs args);
public sealed class ScreenDataEventArgs : EventArgs
{
    public int ScreenIndex { get; }
    public float TopValue1 { get; }
    public float TopScaled1 { get; }
    public float TopValue2 { get; }
    public float TopScaled2 { get; }
    public float BottomValue1 { get; }
    public float BottomScaled1 { get; }
    public float BottomValue2 { get; }
    public float BottomScaled2 { get; }

    public ScreenDataEventArgs(int screenIndex, float topValue1, float topScaled1, float topValue2, float topScaled2, float bottomValue1, float bottomScaled1, float bottomValue2, float bottomScaled2)
    {
        ScreenIndex = screenIndex;
        TopValue1 = topValue1;
        TopScaled1 = topScaled1;
        TopValue2 = topValue2;
        TopScaled2 = topScaled2;
        BottomValue1 = bottomValue1;
        BottomScaled1 = bottomScaled1;
        BottomValue2 = bottomValue2;
        BottomScaled2 = bottomScaled2;
    }
}

public delegate void ScreenDataEventHandler(object sender, ScreenDataEventArgs args);
[SupportedOSPlatform("windows")]
public sealed partial class PortDispatcher : Component
{
    const int OpenInterval = 5 * 1000;
    readonly SynchronizationContext? _synchronizationContext;
    private readonly ConcurrentDictionary<Device, string> _deviceToFile = new();
    private readonly ConcurrentDictionary<string, Device> _serialToDevice = new();
    private readonly ConcurrentDictionary<string, Device> _macToDevice = new();
    private const string deviceFileSuffix = ".device.json";
    private const string deviceFilePattern = $"*{deviceFileSuffix}";
    private const string screenFileSuffix = ".screen.json";
    private FileSystemWatcher _fsw;
    private Timer _timer;
    private Timer _openTimer;
    private long _intervalMs=DefaultInterval;
    private string _path;
    public static int DefaultInterval => 100;
    public ObservableCollection<Session> Sessions { get; } = new ObservableCollection<Session>();
    public HardwareInfoCollection HardwareInfo { get; }
    public event ScreenChangedEventHandler? ScreenChanged;
    public event ScreenClearedEventHandler? ScreenCleared;
    public event ScreenDataEventHandler? ScreenData;
    internal void OnScreenScreenChanged(ScreenChangedEventArgs args)
    {
        ScreenChanged?.Invoke(this, args);
    }
    internal void OnScreenScreenCleared(EventArgs args)
    {
        ScreenCleared?.Invoke(this, args);
    }
    internal void OnScreenScreenData(ScreenDataEventArgs args)
    {
        ScreenData?.Invoke(this, args);
    }
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

   
    public string[] GetDeviceSerialNumbers()
    {
        var result = new List<string>(_serialToDevice.Count);
        foreach(var str in _serialToDevice.Keys)
        {
            result.Add(str);
        }
        return result.ToArray();
    }
    
    internal DevicePortEntry[] GetDevicePortEntries(PortEntry[] ports) 
    {
        var result = new List<DevicePortEntry>(_macToDevice.Count);
        var hashSerial = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _macToDevice)
        {
            var mac = Convert.FromHexString(kvp.Key);
            var serialNumbers = kvp.Value.SerialNumbers;
            for (int i = 0; i < ports.Length; ++i)
            {
                var port = ports[i];
                for (var j = 0; j < serialNumbers.Length; ++j)
                {
                    hashSerial.Add(serialNumbers[j]);
                    if (port.SerialNumber.Equals(serialNumbers[j], StringComparison.Ordinal))
                    {
                        Session? session = null;
                        foreach(var sess in Sessions)
                        {
                            if(sess.PortName.Equals(port.PortName, StringComparison.OrdinalIgnoreCase)) 
                            {
                                session = sess;
                                break; 
                            }
                        }
                        result.Add(new DevicePortEntry(port.PortName, serialNumbers, serialNumbers[j], mac,  session));
                        hashSerial.Add(port.PortName);
                        i = ports.Length; // break outer loop because we're done
                        break;
                    }
                }
            }
        }
        for(var i = 0; i < ports.Length;++i)
        {
            var port = ports[i];
            if(!hashPorts.Contains(port.PortName) && !hashSerial.Contains(port.SerialNumber))
            {
                var sess = FindSessionEntryBySerial(port.SerialNumber);
                result.Add(new DevicePortEntry(port.PortName, [port.SerialNumber],port.SerialNumber, sess?.MacAddess, sess));

            }
        }
        return result.ToArray();
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
        try
        {
            foreach (var session in Sessions)
            {
                session.Update();
            }
        }
        catch { }
    }
    private void LoadDevice(string path)
    {
        if (!_deviceToFile.Values.Contains(path))
        {
            Stream? stm = null;
            var i = 0;
            while(true) {
                try
                {
                    stm = File.OpenRead(path);
                    break;
                }
                catch
                {
                    if(i++>10)
                    {
                        throw;
                    }
                    Thread.Sleep(100);
                }
            }
            // Load the updated screen
            using var reader = new StreamReader(stm, Encoding.UTF8);
            try
            {
                var device = Device.ReadFrom(reader);
                device.HardwareInfo = HardwareInfo;
                _deviceToFile.TryAdd(device, path);
                for(i = 0;i< device.SerialNumbers.Length;++i)
                {
                    _serialToDevice.TryAdd(device.SerialNumbers[i], device);
                }
                _macToDevice.TryAdd(Convert.ToHexString(device.MacAddress,0,device.MacAddress.Length), device);
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
                    var kvp = _deviceToFile.FirstOrDefault(kvp => kvp.Value == e.FullPath);
                    device = kvp.Key;
                    if (device != null)
                    {
                        for(var i = 0;i<device.SerialNumbers.Length;++i)
                        {
                            _serialToDevice.TryRemove(new KeyValuePair<string, Device>(device.SerialNumbers[i], device));
                        }
                        _deviceToFile.TryRemove(device, out _);
                        _macToDevice.TryRemove(Convert.ToHexString(device.MacAddress, 0, device.MacAddress.Length), out _);
                        device.Dispose();                        
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
                        for (var i = 0; i < device.SerialNumbers.Length; ++i)
                        {
                            _serialToDevice.TryRemove(new KeyValuePair<string, Device>(device.SerialNumbers[i], device));
                        }
                        _deviceToFile.TryRemove(device, out _);
                        _macToDevice.TryRemove(Convert.ToHexString(device.MacAddress, 0, device.MacAddress.Length), out _);
                        var sess = FindSessionEntryByMac(device.MacAddress);
                        device.Dispose();
                        if (sess != null) {
                            Sessions.Remove(sess);
                        }
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
            for(var j = 0;j<session.SerialNumbers.Length; ++j)
            {
                var serialNo = session.SerialNumbers[j];
                if (serialNo.Equals(serial, StringComparison.OrdinalIgnoreCase))
                {
                    return session;
                }
            }
        }
        return null;
    }
    private Session? FindSessionEntryByMac(byte[] mac)
    {
        if (Sessions == null) return null;
        if (mac == null || mac.Length != 6) return null;
        for (var i = 0; i < Sessions.Count; ++i)
        {
            var session = Sessions[i];
            if(session.MacAddess!=null && session.MacAddess.Length==6)
            {
                var found = true;
                for(var j = 0;j<6;++j)
                {
                    if (mac[j] != session.MacAddess[j])
                    {
                        found = false; break;
                    }
                }
                if (found)
                {
                    return session;
                }
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
   
    internal Device? GetDeviceForMac(byte[] mac)
    {
        if(_macToDevice.TryGetValue(Convert.ToHexString(mac), out var result)) {
            return result;
        }
        return null;
    }
    internal string? GetNameForDevice(Device device)
    {
        if (_deviceToFile.TryGetValue(device, out var result))
        {
            return Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(result));
        }
        return null;
    }
    private void TryOpenPorts()
    {
        var serialPorts = EspSerialSession.GetPorts();
        var portEntries = GetDevicePortEntries(serialPorts);
        for (int i = 0; i < portEntries.Length; i++)
        {
            var portEntry = portEntries[i];
            var mac = portEntry.MacAddress;
            if (mac != null)
            {
                var se = FindSessionEntryByMac(mac);
                if (se != null)
                {
                    try
                    {
                        if (se.Status == SessionStatus.Closed)
                        {
                            se.Open();
                            se.Update();
                        }
                    }
                    catch { }
                }
                else if (_macToDevice.TryGetValue(Convert.ToHexString(mac), out var device))
                {
                    for (var j = 0; j < device.SerialNumbers.Length; j++)
                    {
                        var serialNo = device.SerialNumbers[j];
                        var se2 = FindSessionEntryBySerial(serialNo);
                        if (se2 != null)
                        {
                            try
                            {
                                if (se2.Status == SessionStatus.Closed)
                                {
                                    se2.Open();
                                    se2.Update();
                                }
                            }
                            catch { }
                        }
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
        var hashMac = new HashSet<string>(StringComparer.Ordinal);
        var toAdd = new List<DevicePortEntry>();
        var toRemove = new List<Session>(Sessions.Count);
        var serialPorts = EspSerialSession.GetPorts();
        var portEntries = GetDevicePortEntries(serialPorts);
        for(var i = 0;i<portEntries.Length;++i)
        {
            var portEntry = portEntries[i];
            if(portEntry.Session == null)
            {
                toAdd.Add(portEntry);
            }
            
        }
        foreach (var session in Sessions)
        {
            var found = false;
            for(var i = 0;i<portEntries.Length;++i)
            {
                if (portEntries[i].PortName.Equals(session.PortName,StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }
            if(!found)
            {
                toRemove.Add(session);
            }
        }
        for (var i = 0; i < toRemove.Count; ++i)
        {
            Sessions.Remove(toRemove[i]);
        }
        toRemove.Clear();
        for (int i = 0; i < toAdd.Count; i++)
        {
            var portEntry = toAdd[i];
            if (portEntry.MacAddress!=null && _macToDevice.TryGetValue(Convert.ToHexString(portEntry.MacAddress), out var device))
            {
                var se = FindSessionEntryByMac(portEntry.MacAddress);
                if (se != null)
                {
                    continue;
                }
                if (_macToDevice.TryGetValue(Convert.ToHexString(portEntry.MacAddress), out var device2))
                {
                    if (_deviceToFile.TryGetValue(device2, out var filename)) 
                    {
                        var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(filename));
                        var session = new Session(this,_path, name, portEntry.PortName, device2.SerialNumbers,portEntry.SerialNumber);
                        session.HardwareInfo = HardwareInfo;
                        Sessions.Add(session);
                    }
                }   
            } else
            {
                var session = new Session(this, _path, "", portEntry.PortName, portEntry.SerialNumbers,portEntry.SerialNumber);
                session.HardwareInfo = HardwareInfo;
                Sessions.Add(session);
            }
        }
        toAdd.Clear();
        
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
