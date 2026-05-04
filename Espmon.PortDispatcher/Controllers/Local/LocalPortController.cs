using HWKit;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Xml.Linq;

namespace Espmon;

[SupportedOSPlatform("windows")]
public class LocalPortController : PortController
{
    HardwareInfoCollection _hardwareInfo;
    public string Path { get; }
    Dictionary<string, DeviceController> _deviceFilesBySerial = new();
    Dictionary<string, DeviceController> _deviceFilesByMac = new();
    Dictionary<string, ScreenController> _screenFiles = new();
    List<DeviceController> _hookedDevices = new();
    List<ScreenController> _hookedScreens = new();
    private bool _hooksEnabled = true;
    public LocalPortController(string path, SynchronizationContext? syncContext = null) : base(syncContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if(!Directory.Exists(path))
        {
            throw new ArgumentException("The specified path does not exist", nameof(path));
        }
        Path = path;
        _hardwareInfo = new HardwareInfoCollection();
    }
    protected override ProviderController[] CreateProviders()
    {
        EnsureDefaultProviders();
        var filePath = System.IO.Path.Join(Path, "providers.json");
        JsonArray? arr;
        using (var reader = new StreamReader(filePath, Encoding.UTF8))
        {
            arr = (JsonArray?)JsonArray.ReadFrom(reader);
        }
        if (arr == null) throw new NullReferenceException(); // shouldn't happen
        var entries = LocalProviderController.FromJson(this, arr);
        var result = new ProviderController[entries.Length];
        for(var i = 0;i<result.Length;++i)
        {
            result[i] = entries[i].Provider;
        }
        return result;
    }
    private void StartProviders()
    {
        EnsureDefaultProviders();
        var filePath = System.IO.Path.Join(Path, "providers.json");
        JsonArray? arr;
        using (var reader = new StreamReader(filePath, Encoding.UTF8))
        {
            arr = (JsonArray?)JsonArray.ReadFrom(reader);
        }
        if (arr == null) throw new NullReferenceException(); // shouldn't happen
        var entries = LocalProviderController.FromJson(this, arr);
        for (var i = 0; i < entries.Length; ++i)
        {
            if (entries[i].IsStarted)
            {
                try { 
                    Providers[i].Start();
                }
                catch { }
            }
        }
    }
    public void EnsureDefaultProviders()
    {
        var filePath = System.IO.Path.Join(Path, "providers.json");
        if (File.Exists(filePath))
        {
            return;
        }
        using var from = Assembly.GetExecutingAssembly()?.GetManifestResourceStream("Espmon.providers.json");
        if (from == null)
        {
            throw new InvalidProgramException("Default providers not found in executable resources");
        }
        using var to = File.OpenWrite(filePath);
        from.CopyTo(to);
        from.Close();
        to.Close();
    }
    protected override IEnumerable<HardwareInfoEntry> OnEvaluate(HardwareInfoExpression expression)
    {
        return expression.Evaluate(_hardwareInfo);
    }
    protected override string OnGetUnit(HardwareInfoExpression expression)
    {
        return expression.GetUnit(_hardwareInfo);
    }
    
    protected override DeviceController? OnGetDeviceByMac(byte[] macAddress)
    {
        if(_deviceFilesByMac.TryGetValue(Convert.ToHexString(macAddress), out var result)) {
            return result;
        }
        return null;
    }
    protected override void OnRefresh()
    {
        _hardwareInfo?.ExpireTracking();
    }
    protected override DeviceController[] CreateDevices()
    {
        foreach(var dev in _hookedDevices)
        {
            dev.PropertyChanging -= Device_PropertyChanging;
            dev.PropertyChanged -= Device_PropertyChanged;
            dev.Screens.CollectionChanged -= Device_Screens_CollectionChanged;
        }
        _hookedDevices.Clear();
        var result = new List<DeviceController>(_deviceFilesBySerial.Count>0? _deviceFilesBySerial.Count:8);
        if (_deviceFilesBySerial.Count == 0)
        {
            var deviceFiles = Directory.GetFiles(Path, "*.device.json");
            
            for (var i = 0; i < deviceFiles.Length; ++i)
            {
                var file = deviceFiles[i];
                JsonObject? obj;
                DeviceController? dev = null;
                try
                {
                    using (var reader = new StreamReader(File.OpenRead(file)))
                    {
                        obj = (JsonObject?)JsonObject.ReadFrom(reader);
                    }
                    if (obj != null)
                    {
                        dev = DeviceController.FromJson(this, System.IO.Path.GetFileNameWithoutExtension(System.IO.Path.GetFileNameWithoutExtension(file)), obj);
                        dev.PropertyChanging += Device_PropertyChanging;
                        dev.PropertyChanged += Device_PropertyChanged;
                        dev.Screens.CollectionChanged += Device_Screens_CollectionChanged;
                        _hookedDevices.Add(dev);
                    }
                }
                catch
                {
                    dev = null;
                    obj = null;
                }
                if (dev != null)
                {
                    _deviceFilesByMac[Convert.ToHexString(dev.MacAddress)]=dev;
                    for (var j = 0; j < dev.SerialNumbers.Length; ++j)
                    {
                        _deviceFilesBySerial[dev.SerialNumbers[j]]= dev;
                    }
                    result.Add(dev);

                }
            }
        }
        return result.ToArray();
    }

    private void Device_Screens_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if(_hooksEnabled)
        {
            // ugly
            foreach(var dev in _hookedDevices)
            {
                if(dev.Screens==sender)
                {
                    TrySaveDevice(dev);
                    break;
                }
            }
        }
    }
    private void Device_PropertyChanging(object? sender, System.ComponentModel.PropertyChangingEventArgs e)
    {
        if (_hooksEnabled && sender is DeviceController device)
        {
            if (e.PropertyName == null || e.PropertyName.Equals("Name", StringComparison.Ordinal))
            {
                var oldFile = System.IO.Path.Combine(Path, $"{device.Name}.device.json");
                if (File.Exists(oldFile))
                {
                    try
                    {

                        File.Delete(oldFile);

                    }
                    catch { }
                }

            }
        }
    }
    private void Device_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_hooksEnabled && sender is DeviceController device) {
            if (e.PropertyName == null || e.PropertyName.Equals("Name", StringComparison.Ordinal))
            {
                TrySaveDevice(device);
            }
            if(e.PropertyName==null||e.PropertyName.Equals("MacAddress",StringComparison.Ordinal))
            {
                var toRemove = new List<string>();
                foreach (var kvp in _deviceFilesByMac)
                {
                    if (object.ReferenceEquals(kvp.Value, sender))
                    {
                        toRemove.Add(kvp.Key);
                    }
                }
                foreach (var rem in toRemove)
                {
                    _deviceFilesByMac.Remove(rem);
                }
                if (device.MacAddress != null)
                {
                    _deviceFilesByMac[Convert.ToHexString(device.MacAddress)]= device;
                }
                TrySaveDevice(device);
            }
            
        }
    }

    protected override void OnDeviceAdded(DeviceController device)
    {
        var obj = device.ToJson();
        var name = device.Name;
        string file;
        // not named yet so we have to make a name for it
        if (name.Length == 0)
        {
            name = "Device";
            file = System.IO.Path.Combine(Path, $"{name}.device.json");
            if (File.Exists(file))
            {
                var i = 2;
                name = $"Device {i}";
                file = System.IO.Path.Combine(Path, $"{name}.device.json");
                while (File.Exists(file))
                {
                    ++i;
                    name = $"Device {i}";
                    file = System.IO.Path.Combine(Path, $"{name}.device.json");
                }
            }
            _hooksEnabled = false;
            device.Name = name;
            _hooksEnabled = true;
        }
        TrySaveDevice(device);
        if(device.MacAddress!=null)
        {
            _deviceFilesByMac[Convert.ToHexString(device.MacAddress)]= device;
        }
        for (var i = 0; i < device.SerialNumbers.Length; ++i) {
            _deviceFilesBySerial[device.SerialNumbers[i]] = device;
        }
        if (!_hookedDevices.Contains(device))
        {
            device.PropertyChanging += Device_PropertyChanging;
            device.PropertyChanged += Device_PropertyChanged;
            device.Screens.CollectionChanged += Device_Screens_CollectionChanged;
            _hookedDevices.Add(device);
        }
        base.OnDeviceAdded(device);
    }
    protected override void OnDeviceRemoved(DeviceController device)
    {
        if (device.Name.Length > 0) 
        { 
            var file = System.IO.Path.Combine(Path, $"{device.Name}.device.json");
            File.Delete(file);
        }
        var toRemove = new List<string>(2);
        foreach(var kvp in _deviceFilesBySerial)
        {
            if(object.ReferenceEquals(kvp.Value,device))
            {
                toRemove.Add(kvp.Key);
            }
        }
        foreach(var rem in toRemove)
        {
            _deviceFilesBySerial.Remove(rem);
        }
        toRemove.Clear();
        if (device.MacAddress != null)
        {
            _deviceFilesByMac.Remove(Convert.ToHexString(device.MacAddress));
        }
        if (_hookedDevices.Remove(device))
        {
            device.PropertyChanging -= Device_PropertyChanging;
            device.PropertyChanged -= Device_PropertyChanged;
            device.Screens.CollectionChanged -= Device_Screens_CollectionChanged;
        }
        base.OnDeviceRemoved(device);
    }
    protected override ScreenController[] CreateScreens()
    {
        var result = new List<ScreenController>(_screenFiles.Count > 0 ? _screenFiles.Count : 8);
        if (_screenFiles.Count == 0)
        {
            for(var i = 0;i<_hookedScreens.Count;++i)
            {
                _hookedScreens[i].PropertyChanged -= Screen_PropertyChanged;
            }
            _hookedScreens.Clear();
            var screenFiles = Directory.GetFiles(Path, "*.screen.json");

            for (var i = 0; i < screenFiles.Length; ++i)
            {
                var file = screenFiles[i];
                JsonObject? obj;
                ScreenController? scr = null;
                try
                {
                    using (var reader = new StreamReader(File.OpenRead(file)))
                    {
                        obj = (JsonObject?)JsonObject.ReadFrom(reader);
                    }
                    if (obj != null)
                    {
                        scr = ScreenController.FromJson(this, System.IO.Path.GetFileNameWithoutExtension(System.IO.Path.GetFileNameWithoutExtension(file)), obj);
                    }
                }
                catch
                {
                    scr = null;
                }
                if (scr != null)
                {
                    _screenFiles.Add(scr.Name, scr);
                    scr.PropertyChanged += Screen_PropertyChanged;
                    _hookedScreens.Add(scr);
                }
            }
        }
        return _screenFiles.Values.ToArray();
    }
    private bool TrySaveDevice(DeviceController device)
    {
        if(device.MacAddress!=null && !string.IsNullOrEmpty(device.Name))
        {
            var obj = device.ToJson();
            using (var writer = new StreamWriter(System.IO.Path.Combine(Path, $"{device.Name}.device.json"), false, Encoding.UTF8))
            {
                Debug.Write($"Saving {device.Name}");
                obj.WriteTo(writer);
                return true;
            }
        }
        return false;
    }
    private bool TrySaveScreen(ScreenController screen)
    {
        if (!string.IsNullOrEmpty(screen.Name))
        {
            var obj = screen.ToJson();
            using (var writer = new StreamWriter(System.IO.Path.Combine(Path, $"{screen.Name}.screen.json"), false, Encoding.UTF8))
            {
                obj.WriteTo(writer);
                return true;
            }
        }
        return false;
    }
    private void Screen_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_hooksEnabled && sender is ScreenController screen)
        {
            if (e.PropertyName == null || e.PropertyName.Equals("Name", StringComparison.Ordinal))
            {
                string? oldName = null;
                foreach (var kvp in _screenFiles)
                {
                    if (object.ReferenceEquals(kvp.Value, sender))
                    {
                        oldName = kvp.Key;
                        var oldFile = System.IO.Path.Combine(Path, $"{kvp.Key}.screen.json");
                        var newFile = System.IO.Path.Combine(Path, $"{screen.Name}.screen.json");
                        if (File.Exists(oldFile))
                        {
                            File.Move(oldFile, newFile);
                            if (File.Exists(oldFile))
                            {
                                try
                                {

                                    File.Delete(oldFile);

                                }
                                catch { }
                            }
                        }
                        foreach(var dev in Devices)
                        {
                            for(var i = 0;i<dev.Screens.Count;++i)
                            {
                                if(dev.Screens[i].Equals(kvp.Key,StringComparison.OrdinalIgnoreCase)) 
                                {
                                    dev.Screens[i] = screen.Name;
                                    TrySaveDevice(dev);
                                    break;
                                }
                            }
                        }
                        break;
                    }
                }
                if (oldName != null) {
                    _screenFiles.Remove(oldName);
                    _screenFiles[screen.Name] = screen;
                }
            }
            TrySaveScreen(screen);
        }
        
    }
    protected override void OnScreenAdded(ScreenController screen)
    {
        var obj = screen.ToJson();
        var name = screen.Name;
        string file;
        // not named yet so we have to make a name for it
        if (name.Length == 0)
        {
            name = "Screen";
            file = System.IO.Path.Combine(Path, $"{name}.screen.json");
            if (File.Exists(file))
            {
                var i = 2;
                name = $"Screen {i}";
                file = System.IO.Path.Combine(Path, $"{name}.screen.json");
                while (File.Exists(file))
                {
                    ++i;
                    name = $"Device {i}";
                    file = System.IO.Path.Combine(Path, $"{name}.screen.json");
                }
            }
            _hooksEnabled = false;
            screen.Name = name;
            _hooksEnabled = true;
        }
        TrySaveScreen(screen);
        _screenFiles.Add(name, screen);
        if (!_hookedScreens.Contains(screen))
        {
            screen.PropertyChanged += Screen_PropertyChanged;
            _hookedScreens.Add(screen);
        }
        base.OnScreenAdded(screen);
    }


    protected override void OnScreenRemoved(ScreenController screen)
    {
        if (screen.Name.Length > 0)
        {
            foreach(var dev in Devices)
            {
                for(var i = 0;i<dev.Screens.Count;++i)
                {
                    if (dev.Screens[i].Equals(screen.Name))
                    {
                        dev.Screens.RemoveAt(i);
                        --i;
                    }
                }
            }
            var file = System.IO.Path.Combine(Path, $"{screen.Name}.screen.json");
            File.Delete(file);

        }
        _screenFiles.Remove(screen.Name);
        if (_hookedScreens.Remove(screen))
        {
            screen.PropertyChanged -= Screen_PropertyChanged;
        }
        base.OnScreenRemoved(screen);
    }
    protected override SessionController[] CreateSessions()
    {
        var result = new List<SessionController>(Devices.Count);
        var portEntries = EspSerialSession.GetPorts();
        for (var i = 0; i < portEntries.Length; ++i)
        {
            var portEntry = portEntries[i];
            if (_deviceFilesBySerial.TryGetValue(portEntry.SerialNumber, out var dev))
            {

                result.Add(new LocalSessionController(this, portEntry.PortName, portEntry.SerialNumber, dev));
            }
            else
            {
                result.Add(new LocalSessionController( this,portEntry.PortName, portEntry.SerialNumber,null));
            }
        }
        return result.ToArray();
    }

    protected override void OnStart()
    {
        _hardwareInfo.MinimumTrackingInterval = TimeSpan.FromMilliseconds(100);
        _hardwareInfo.Providers.Clear();
        foreach (var provider in Providers)
        {
            _hardwareInfo.Providers.Add(((LocalProviderController)provider).Provider);
        }
        StartProviders();
    }

    protected override void OnStop()
    {
        _hardwareInfo.StopAll();
        for (var i = 0; i < _hookedDevices.Count; ++i)
        {
            _hookedDevices[i].PropertyChanging -= Device_PropertyChanging;
            _hookedDevices[i].PropertyChanged -= Device_PropertyChanged;
        }
        _hookedDevices.Clear();
        for (var i = 0; i < _hookedScreens.Count; ++i)
        {
            _hookedScreens[i].PropertyChanged -= Screen_PropertyChanged;
        }
        _hookedScreens.Clear();
        _deviceFilesBySerial.Clear();
        _screenFiles.Clear();
    }
}
