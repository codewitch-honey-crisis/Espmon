using HWKit;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;

namespace Espmon;

public enum SessionStatus
{
    Closed = 0,
    Connecting,
    Resetting,
    Negotiating,
    Busy,
    ReadyForData,
    NeedScreen,
    RequiresFlash,
    Flashing
}
[SupportedOSPlatform("windows")]
public partial class Session : Component, INotifyPropertyChanged
{
    PortDispatcher _parent;
    SessionStatus _state = SessionStatus.Closed;
    EspSerialSession? _transport;
    long _startIdentTicks;
    long _gotIdentTicks;
    Device? _device;
    ushort _versionMajor;
    ushort _versionMinor;
    ulong _build;
    short _id = -1;
    byte[]? _macAddress;
    string? _displayName;
    string? _slug;
    ushort _hres;
    ushort _vres;
    bool _isMonochrome;
    float _dpi;
    float _pixelSize;
    bool _dataReady;
    int _needScreen;
    bool _needsFlash;
    DeviceInputType _inputType;
    string _portName;
    string[] _serialNumbers;
    string _serialNumber;
   int _screenIndex = -1;
    string _path;
    Dictionary<string, (long TimestampTicks, Screen Screen)> _screenCache = new(StringComparer.Ordinal);
    public event PropertyChangedEventHandler? PropertyChanged;
    
    public Session(PortDispatcher parent, string path, string name, string portName, string[] serialNumbers, string serialNumber)
    {
        ArgumentNullException.ThrowIfNull(parent, nameof(parent));
        ArgumentNullException.ThrowIfNull(path, nameof(path));
        ArgumentNullException.ThrowIfNull(name, nameof(name));
        ArgumentNullException.ThrowIfNull(portName, nameof(portName));
        ArgumentNullException.ThrowIfNull(serialNumbers, nameof(serialNumbers));
        ArgumentNullException.ThrowIfNull(serialNumber, nameof(serialNumber));
        _parent = parent;
        _path = path;
        _name = name;
        _portName = portName;
        _serialNumbers = serialNumbers;
        _transport = null;
        _serialNumber = serialNumber;
        InitializeComponent();
    }

    public Session(PortDispatcher parent, string path, string name, string portName,string[] serialNumbers, string serialNumber, IContainer container)
    {
        ArgumentNullException.ThrowIfNull(parent, nameof(parent));
        ArgumentNullException.ThrowIfNull(path, nameof(path));
        ArgumentNullException.ThrowIfNull(name, nameof(name));
        ArgumentNullException.ThrowIfNull(portName, nameof(portName));
        ArgumentNullException.ThrowIfNull(serialNumbers, nameof(serialNumbers));
        ArgumentNullException.ThrowIfNull(serialNumber, nameof(serialNumber));
        _parent = parent;
        _path = path;
        _name = name;
        _portName = portName;
        _serialNumbers= serialNumbers;
        _serialNumber = serialNumber;
        _transport = null;
        container?.Add(this);
        InitializeComponent();
    }
    public string PortName
    {
        get
        {
            return _portName;
        }
    }
    public string SerialNumber
    {
        get { return _serialNumber; }
    }
    HardwareInfoCollection? _hardwareInfo = null;
    public HardwareInfoCollection? HardwareInfo
    {
        get { return _hardwareInfo; }
        set
        {
            if (_hardwareInfo != value)
            {
                _hardwareInfo = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HardwareInfo)));
            }
        }
    }
    public void RefreshScreen(string name)
    {
        var n = CurrentScreenName;
        if (_transport!=null && _transport.IsOpen && _screenCache.Remove(name) && n!=null && name.Equals(n,StringComparison.Ordinal))
        {
            // don't need to go through the motions of serializing, the struct is empty
            try
            {
                _transport.Send((byte)Command.CmdRefreshScreen, Array.Empty<byte>());
            }
            catch (Win32Exception)
            {
                Close();
            }
        }
    }
    Screen? GetScreen(string name)
    {
        var filePath = Path.Join(_path, $"{name}.screen.json");
        if (_screenCache.TryGetValue(name, out var entry))
        {
            var lastWrite = File.GetLastWriteTime(filePath).Ticks;
            if(lastWrite>entry.TimestampTicks)
            {
                // cache expired because file is newer
                _screenCache.Remove(name);
            } else
            {
                
                entry.Screen.HardwareInfo  = HardwareInfo;
                return entry.Screen;
            }
        }
        if(File.Exists(filePath))
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8);
            var result = Screen.ReadFrom(reader);
            reader.Close();
            // the OS isn't necessarily the same as DateTime.Now.Ticks
            var lastWrite = File.GetLastWriteTime(filePath).Ticks;
            result.HardwareInfo = HardwareInfo;
            _screenCache.Add(name, (lastWrite, result));
            return result;
        }
        return null;
    }
    public short Id
    {
        get { return _id; }
    }
    public DeviceInputType InputType
    {
        get { return _inputType; }
    }
    public string? DisplayName
    {
        get
        {
            return _displayName;
        }
    }
    public bool IsMonochrome
    {
        get { return _isMonochrome; }
    }
    public byte[]? MacAddess
    {
        get { return _macAddress; }
    }
    public float Dpi
    {
        get { return _dpi; }
    }
    public float PixelSize
    {
        get { return _pixelSize; }
    }
    private string? _name;
    public string? StoredName
    {
        get
        {
            return _name;
        }
    }
    public string Name
    {
        get
        {
            if (_name==null || _state==SessionStatus.Closed || _state==SessionStatus.Connecting)
            {
                return PortName.ToLowerInvariant();
            }
            return _name;
        }
        set
        {
            if (_name != value)
            {
                var path = _path;
                if (!string.IsNullOrEmpty(_name))
                {
                    var filename = $"{_name}.device.json";
                    var filepath = Path.Combine(path, filename);
                    if (File.Exists(filepath))
                    {
                        var filename2 = $"{value}.device.json";
                        var filepath2 = Path.Combine(path, filename2);
                        File.Move(filepath, filepath2);
                    }
                }

                _name = value;

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));

            }
        }
    }
    public string[] SerialNumbers
    {
        get
        {
            return _serialNumbers;
        }
    }
    public Screen? CurrentScreen
    {
        get
        {
            var name = CurrentScreenName;
            if (name == null) return null;
            return GetScreen(name);
        }
    }
    public string? CurrentScreenName
    {
        get
        {
            if(_device!=null && _screenIndex>-1 && _device.Screens.Count > 0)
            {
                return _device.Screens[_screenIndex % _device.Screens.Count];
            }
            return null;
        }
        set
        {
            if (_device != null && !string.IsNullOrEmpty(value))
            {
                var idx = value==null?-1:_device.Screens.IndexOf(value);
                if(idx!=_screenIndex)
                {
                    _screenIndex = idx % _device.Screens.Count;
                    if (_screenIndex > -1 && value!=null && _transport!=null)
                    {
                        var scr = GetScreen(value)?.ToResponseScreen();
                        if (scr != null)
                        {
                            Span<byte> tmp = stackalloc byte[scr.SizeOfStruct];
                            if (scr != null)
                            {
                                scr.Header.Index = (sbyte)idx;
                                if (scr.TryWrite(tmp, out _))
                                {
                                    try
                                    {
                                        _transport.Send((byte)Command.CmdScreen, tmp);
                                    }
                                    catch (Win32Exception)
                                    {
                                        Close();
                                        return;
                                    }
                                }
                            }
                        }
                        //Debug.WriteLineIf(_portName.Equals("COM14", StringComparison.OrdinalIgnoreCase), "Send CMD_SCREEN");
                    }
                    //Debug.WriteLine($"Set CurrentScreenName to {value}, index {_screenIndex}");
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentScreenName)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentScreenIndex)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentScreen)));
            }
        }
    }
    public int CurrentScreenIndex
    {
        get
        {
            if (_device != null && _screenIndex > -1)
            {
                return _screenIndex;
            }
            return -1;
        }
        set
        {
            if (_device != null && value > -1)
            {
                if (value != _screenIndex)
                {
                    _screenIndex = value;
                    if (_screenIndex > -1 && _transport != null)
                    {
                        var idx = _screenIndex % _device.Screens.Count;
                        var scr = GetScreen(_device.Screens[idx])?.ToResponseScreen();
                        if (scr != null)
                        {
                            Span<byte> tmp = stackalloc byte[scr.SizeOfStruct];
                            if (scr != null)
                            {
                                scr.Header.Index = (sbyte)idx;
                                if (scr.TryWrite(tmp, out _))
                                {
                                    try
                                    {
                                        _transport.Send((byte)Command.CmdScreen, tmp);
                                    }
                                    catch (Win32Exception)
                                    {
                                        Close();
                                        return;
                                    }
                                }
                            }
                        }
                        //Debug.WriteLineIf(_portName.Equals("COM14",StringComparison.OrdinalIgnoreCase), "Send CMD_SCREEN");
                    }
                    //Debug.WriteLine($"Set CurrentScreenIndex to {value}, index {_screenIndex}");
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentScreenName)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentScreenIndex)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentScreen)));
            }
        }
    }

    public ushort HorizontalResolution
    {
        get { return _hres; }
    }
    public ushort VerticalResolution
    {
        get { return _vres; }
    }
    static string MacToString(byte[] mac, bool isFile )
    {
        var j = isFile ? "-" : ":";
        return string.Join(j, mac.Select(b => b.ToString("X2")));
    }
    public SessionStatus Status => _state;

    public void Open()
    {
        Debug.WriteLine($"Try open {_portName}");
        if (_state == SessionStatus.Closed || _state == SessionStatus.RequiresFlash)
        {
            _state = SessionStatus.Connecting;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            if (_transport == null)
            {
#if DEBUG
                _transport = new EspSerialSession(_portName, true);
#else
            _transport = new EspSerialSession(_portName, false);
#endif
            }
            _dataReady = false;
            _needScreen = CurrentScreenIndex;
            _transport.ConnectionError += _transport_ConnectionError;
            _transport.FrameError += _transport_FrameError;
            _transport.FrameReceived += _transport_FrameReceived;
            _transport.Open();
            _startIdentTicks = 0;
            _gotIdentTicks = 0;
            _needsFlash = false;
        }     
    }

    private void _transport_FrameReceived(object? sender, FrameReceivedEventArgs e)
    {
        if(_transport!=null && _transport.IsOpen)
        {
            switch((Command)e.Command)
            {
                case Command.CmdScreen:
                    {
                        //Debug.WriteLineIf(_portName.Equals("COM14", StringComparison.OrdinalIgnoreCase), "Received CMD_SCREEN");
                        if (RequestScreen.TryRead(e.Data, out var req, out _))
                        {
                            _screenIndex = -1; // force change
                            _needScreen = req.ScreenIndex;
                            _dataReady = false;
                            
                        }
                    }
                    break;
                case Command.CmdData:
                    {
                        //Debug.WriteLineIf(_portName.Equals("COM14", StringComparison.OrdinalIgnoreCase), "Received CMD_DATA");
                        if (RequestData.TryRead(e.Data, out var req, out _))
                        {
                            if(req.ScreenIndex==CurrentScreenIndex)
                            {
                                if (!_dataReady)
                                {
                                    _dataReady = true;
                                    Thread.MemoryBarrier();
                                }
                            }
                        }
                    }
                    break;
                case Command.CmdIdent:
                    RequestIdent.TryRead(e.Data, out var ident, out _);
                    _versionMajor = ident.VersionMajor;
                    _versionMinor = ident.VersionMinor;
                    _build = ident.Build;
                    var appBuild = FirmwareBuild.Timestamp;
                    var diff = TimeSpan.FromSeconds((long)(appBuild - _build));
                    if (appBuild > _build)
                    {
                        _needsFlash = true;
                        break;
                    }
                    _id = ident.ID;
                    _macAddress = ident.MacAddress;
                    _displayName = ident.DisplayName;
                    _slug = ident.Slug;
                    _hres = ident.HorizontalResolution;
                    _vres = ident.VerticalResolution;
                    _isMonochrome = ident.IsMonochrome;
                    _dpi = ident.Dpi;
                    _pixelSize = ident.PixelSize;
                    _inputType = (DeviceInputType)ident.InputType;
                    _gotIdentTicks = Stopwatch.GetTimestamp();
                    
                    break;

            }
        }
    }

    private void _transport_FrameError(object? sender, FrameReceivedEventArgs e)
    {
        Debug.WriteLine("Serial frame error");
    }

    private void _transport_ConnectionError(object? sender, EventArgs e)
    {
        Debug.WriteLine("Disconnect detected");
        
        Close();
        
    }

    public void Close()
    {
        if (_transport != null)
        {
            _transport.ConnectionError -= _transport_ConnectionError;
            _transport.FrameError -= _transport_FrameError;
            _transport.FrameReceived -= _transport_FrameReceived;
            _transport.Close();
            _transport = null;
        }
        if (_state != SessionStatus.Closed )
        {
            _state = SessionStatus.Closed;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
        
    }
    private sealed class InnerOpenFlashProgress : IProgress<int>
    {
        IFlashProgress _inner;
        public InnerOpenFlashProgress(IFlashProgress inner)
        {
            _inner = inner;
        }
        public string Action { get; private set; } = string.Empty;
        public void SetAction(string action)
        {
            Action = action;
        }
        public void Report(int value)
        {
            _inner.Report(new FlashProgressEntry(Action, value));
        }
        public static InnerOpenFlashProgress? Wrap(IFlashProgress? progress)
        {
            if (progress == null) return null;
            return new InnerOpenFlashProgress(progress);
        }
    }
    public async Task ResetAsync(IFlashProgress? progress = null, CancellationToken cancellationToken=default)
    {
        // esptool.py --no-stub flash_id
        var wasOpen = _transport!=null && _transport.IsOpen;
        InnerOpenFlashProgress? innerProgress = (progress == null) ? null : new InnerOpenFlashProgress(progress);
        var prog = int.MinValue;
        if (wasOpen)
        {
            innerProgress?.SetAction("Closing...");
            innerProgress?.Report(prog++);
            Close();
        }
        innerProgress?.SetAction("Resetting...");
        _state = SessionStatus.Resetting;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        innerProgress?.Report(prog++);
        var path = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory) ?? "", "esptool.exe");
        var cmdLine = $"--port {_portName} --no-stub flash_id";
        var psi = new ProcessStartInfo(path, cmdLine)
        {
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false
        };
        using var proc = new Process();
        proc.StartInfo = psi;
        proc.Start();
        var procTask = proc.WaitForExitAsync(cancellationToken);
        if (wasOpen)
        {
            Open();
        } else
        {
            _state = SessionStatus.Closed;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));

        }
    }
    public async Task FlashAsync(bool noReset, FirmwareEntry firmwareEntry, IFlashProgress? progress = null, CancellationToken cancellationToken = default)
    {
        
        using var stm = Assembly.GetExecutingAssembly()?.GetManifestResourceStream("Espmon.firmware.boards.zip");
        if (stm == null)
        {
            throw new InvalidProgramException("boards not found in executable resources");
        }
        var slug = firmwareEntry.Slug;
        using var archive = new ZipArchive(stm);
        var entries = new Dictionary<string, ZipArchiveEntry>();
        for (var i = 0; i < archive.Entries.Count; i++)
        {
            var entry = archive.Entries[i];
            if (entry.FullName.StartsWith(slug + "\\"))
            {
                entries.Add(entry.Name, entry);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (entries.Count != 3)
        {
            throw new InvalidProgramException("Files missing from boards");
        }
        if (!entries.ContainsKey("partition-table.bin") || !entries.ContainsKey("bootloader.bin") || !entries.ContainsKey("firmware.bin"))
        {
            throw new InvalidProgramException("Files missing from boards");
        }
        var wasOpen = _transport != null && _transport.IsOpen;
        var prog = int.MinValue;
        InnerOpenFlashProgress? innerProgress = (progress == null) ? null : new InnerOpenFlashProgress(progress);
        if (wasOpen)
        {
            innerProgress?.SetAction("Closing...");
            innerProgress?.Report(prog++);
            Close();
        }
        _state = SessionStatus.Flashing;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));

        innerProgress?.SetAction("Extracting firmware...");
        prog = int.MinValue;
        var path = _path;
        innerProgress?.Report(0);
        var pathPartition = Path.Combine(path, "partition-table.bin");
        try { File.Delete(pathPartition); } catch { }
        entries[Path.GetFileName(pathPartition)].ExtractToFile(pathPartition);
        innerProgress?.Report(33);
        var pathBootloader = Path.Combine(path, "bootloader.bin");
        try { File.Delete(pathBootloader); } catch { }
        entries[Path.GetFileName(pathBootloader)].ExtractToFile(pathBootloader);
        innerProgress?.Report(66);
        var pathFirmware = Path.Combine(path, "firmware.bin");
        try { File.Delete(pathFirmware); } catch { }
        entries[Path.GetFileName(pathFirmware)].ExtractToFile(pathFirmware);
        innerProgress?.Report(99);
        archive.Dispose();
        stm.Close();
        innerProgress?.Report(100);
        innerProgress?.SetAction("Running Esptool...");
        innerProgress?.Report(0);
        path = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory)??"", "esptool.exe");
        var cmdLine = $"--baud 921600 --port {_portName} write_flash 0x{firmwareEntry.Offsets.Partitiions:X} \"{pathPartition}\" 0x{firmwareEntry.Offsets.Bootloader:X} \"{pathBootloader}\" 0x{firmwareEntry.Offsets.Firmware:X} \"{pathFirmware}\"";
        var psi = new ProcessStartInfo(path, cmdLine)
        {
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false
        };
        using var proc = new Process();
        proc.StartInfo = psi;
        proc.Start();
        var procTask = proc.WaitForExitAsync(cancellationToken);
        SynchronizationContext? sync = SynchronizationContext.Current;
        await Task.Run(() => { 
            while (!proc.StandardOutput.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = proc.StandardOutput.ReadLine();
                if(line!=null)
                {
                    if (line.EndsWith(" %)"))
                    {
                        int idx = line.IndexOf("... ");
                        if (idx > -1)
                        {
                            var num = line.Substring(idx + 5, line.Length - idx - 8);
                            var action = line.Substring(0, idx + 3);
                            int i;
                            if (int.TryParse(num, out i))
                            {
                                if (sync == null)
                                {
                                    innerProgress?.SetAction(action);
                                    innerProgress?.Report(i);
                                }
                                else
                                {
                                    sync.Post((st) => {
                                        innerProgress?.SetAction(action);
                                        innerProgress?.Report(i);
                                    },null);
                                }
                            }
                        }
                    }
                }
            }
        },cancellationToken);
        await procTask;
        if(proc.ExitCode!=0)
        {
            throw new InvalidOperationException(proc.StandardError.ReadToEnd());
        }
        path = _path;
        var path2 = Path.Combine(path, "partition-table.bin");
        try { File.Delete(path2); } catch { }
        path2 = Path.Combine(path, "bootloader.bin");
        try { File.Delete(path2); } catch { }
        path2 = Path.Combine(path, "firmware.bin");
        try { File.Delete(path2); } catch { }
        _needsFlash = false;
        _state = SessionStatus.Closed;
        if (wasOpen)
        {
            Open();
        } else
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    private void LoadDeviceByName()
    {
        if (_serialNumbers == null) throw new InvalidOperationException("The serial number could not be retrieved");
        if (_macAddress == null) throw new InvalidOperationException("The session has not been started");
        var path = _path;
        string pattern;
        string[] files;
        if (_name != null) 
        { 
            pattern = $"{_name}.device.json";
            files = Directory.GetFiles(path, pattern);
        } else
        {
            files = Array.Empty<string>();
        }
        if (files.Length == 0) {
            var result = _parent.GetDeviceForMac(_macAddress);
            if (result == null)
            {
                result = new Device();
                result.MacAddress = _macAddress;
                Name = MacToString(_macAddress, true);
                var fileName = Path.Join(path, $"{_name}.device.json");
                using var writer = new StreamWriter(fileName, false, Encoding.UTF8);
                result.WriteTo(writer);
                if (_device != null)
                {
                    _device.PropertyChanged -= _device_PropertyChanged;
                    _device.Screens.CollectionChanged -= _device_Screens_CollectionChanged;
                }
                _device = result;
                _device.PropertyChanged += _device_PropertyChanged;
                _device.Screens.CollectionChanged += _device_Screens_CollectionChanged;
                return;
            }
            _device = result;
            var changed = false;
            _name = _parent.GetNameForDevice(_device);
            for (var i = 0; i < _serialNumbers.Length; ++i)
            {
                if (0 > Array.IndexOf(_device.SerialNumbers, _serialNumbers[i]))
                {
                    var nsa = new string[_device.SerialNumbers.Length + 1];
                    _device.SerialNumbers.CopyTo(nsa, 0);
                    nsa[_device.SerialNumbers.Length] = _serialNumbers[i];
                    _device.SerialNumbers = nsa;
                    changed = true;
                }
            }
            if(changed)
            {   
                var fileName = Path.Join(path, $"{_name}.device.json");
                using var writer = new StreamWriter(fileName, false, Encoding.UTF8);
                _device.WriteTo(writer);
            }
            _device.PropertyChanged += _device_PropertyChanged;
            _device.Screens.CollectionChanged += _device_Screens_CollectionChanged;
            return;
        }
        var fullPath = files[0];
        using var reader = new StreamReader(File.OpenRead(fullPath),Encoding.UTF8);
        if (_device != null)
        {
            _device.PropertyChanged -= _device_PropertyChanged;
            _device.Screens.CollectionChanged -= _device_Screens_CollectionChanged;
        }
        _device = Device.ReadFrom(reader);
        reader.Close();
        _device.PropertyChanged += _device_PropertyChanged;
        _device.Screens.CollectionChanged += _device_Screens_CollectionChanged;
    }
    private void SaveDevice()
    {
        if (_device == null) throw new InvalidOperationException("The device is null");
        if (_device.MacAddress == null) throw new InvalidOperationException("The device is incomplete");
        for (var i = 0; i < _serialNumbers.Length; ++i)
        {
            if (0 > Array.IndexOf(_device.SerialNumbers, _serialNumbers[i]))
            {
                var nsa = new string[_device.SerialNumbers.Length + 1];
                _device.SerialNumbers.CopyTo(nsa, 0);
                nsa[_device.SerialNumbers.Length] = _serialNumbers[i];
                _device.SerialNumbers = nsa;
            }
        }
        
        var path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Espmon");
        var fullPath = Path.Join(path, $"{_name}.device.json");
        if (File.Exists(fullPath))
        {
            int tries = 0;
            while (true)
            {
                try
                {

                    File.Delete(fullPath);
                    break;
                }
                catch
                {
                    if (tries > 10)
                    {
                        throw;
                    }
                    ++tries;
                    Thread.Sleep(100);
                }
            }

        }
        
        using var writer = new StreamWriter(fullPath, false, Encoding.UTF8);
        _device.WriteTo(writer);
        writer.Close();
    }
    private void _device_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_device == null) return;
        SaveDevice();
        
    }

    public Device? Device => _device;

    private void _device_Screens_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_device == null) return;
        SaveDevice();
    }
   
    public void Update()
    {
        switch (_state)
        {
            case SessionStatus.Connecting:
                if(_transport!=null && _transport.IsOpen && _startIdentTicks==0)
                {
                    _startIdentTicks = Stopwatch.GetTimestamp();
                    try
                    {
                        _transport.Send((byte)Command.CmdIdent, Array.Empty<byte>());
                        _state = SessionStatus.Negotiating;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
                    }
                    catch (Win32Exception)
                    {
                        Close();
                    }
                }
                break;
            case SessionStatus.Busy:

                if (_needScreen > -1 && _transport != null && _transport.IsOpen &&_device!=null && _device.Screens.Count>0)
                {
                    CurrentScreenIndex = _needScreen%_device.Screens.Count;
                    _needScreen = -1;
                    _dataReady = false;
                }
                if (_dataReady && _screenIndex > -1 && _transport != null && _transport.IsOpen)
                {
                    var scr = CurrentScreen;
                    if (scr != null)
                    {
                        var packet = scr.ToResponseData();
                        Span<byte> tmp = stackalloc byte[packet.SizeOfStruct];
                        if (packet.TryWrite(tmp, out _))
                        {
                           
                            try
                            {
                                _transport.Send((byte)Command.CmdData, tmp);
                            } 
                            catch(Win32Exception)
                            {
                                Close();
                            }
                            _dataReady = false;
                        }

                    }
                }
                break;
            case SessionStatus.Negotiating:
                if (_transport != null && _transport.IsOpen && _startIdentTicks != 0)
                {
                    if(_needsFlash)
                    {
                        Debug.WriteLine($"{_portName} requires flash");
                        _state = SessionStatus.RequiresFlash;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
                    } else if (_gotIdentTicks!=0)
                    {
                        LoadDeviceByName();
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Id)));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HorizontalResolution)));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VerticalResolution)));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMonochrome)));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Dpi)));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PixelSize)));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InputType)));
                        _state = SessionStatus.Busy;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));

                    } else if(TimeSpan.FromTicks( Stopwatch.GetTimestamp()-_startIdentTicks).TotalSeconds>3)
                    {
                        _state = SessionStatus.RequiresFlash;
                        _needsFlash = true;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
                    }
                }
                break;
        }

    }
 
}
public struct FlashProgressEntry
{
    public string Action { get; }
    public int Progress { get; }

    public FlashProgressEntry(string action, int progress)
    {
        Action = action; 
        Progress = progress; 
    }
}
public interface IFlashProgress : IProgress<FlashProgressEntry>
{
    
}
