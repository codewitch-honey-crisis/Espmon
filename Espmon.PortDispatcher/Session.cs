using EL;

using HWKit;

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO.Compression;
using System.IO.Ports;
using System.Reflection;
using System.Text;

namespace Espmon;

public enum SessionStatus
{
    Closed = 0,
    Connecting,
    Negotiating,
    Busy,
    ReadyForData,
    NeedScreen,
    RequiresFlash,
    Flashing
}

public partial class Session : Component, INotifyPropertyChanged
{
    private sealed class FirmwareIdent {
        public ushort VersionMajor { get; set; }
        public ushort VersionMinor { get; set; }
        public ulong Build { get; set; }
        public short Id { get; set; }
        public byte[]? MacAddress { get; set; }
        public string? DisplayName { get; set; }
        public string? Slug { get; set; }
        public ushort HorizontalResolution { get; set;  }
        public ushort VerticalResolution { get; set; }
        public bool IsMonochrome { get; set; }
        public float Dpi { get; set;  }
        public float PixelSize { get; set;  }
        public DeviceInputType InputType { get; set;  }
        public static FirmwareIdent Read(byte[] ba)
        {
            var result = new FirmwareIdent();   
            /*
            uint16_t version_major; +0
            uint16_t version_minor; +2
            uint64_t build; +4
            int16_t id; +12
            uint8_t mac_address[6]; +14
            char display_name[64]; +20
            char slug[64] + 84
            uint16_t horizontal_resolution; + 148
            uint16_t vertical_resolution; + 150
            uint8_t is_monochrome; + 152
            float dpi; +153
            float pixel_size; +157 // in millimeters
            input_type_t input_type + 161;
            */
            result.VersionMajor= BitConverter.ToUInt16(ba, 0);
            result.VersionMinor = BitConverter.ToUInt16(ba, 2);
            result.Build = BitConverter.ToUInt64(ba, 4);
            result.Id = BitConverter.ToInt16(ba, 12);
            result.MacAddress = new byte[6];
            Array.Copy(ba, 14, result.MacAddress, 0, result.MacAddress.Length);
            result.DisplayName = Encoding.UTF8.GetString(ba, 20, 64);
            result.Slug = Encoding.UTF8.GetString(ba, 84, 64);
            result.HorizontalResolution = BitConverter.ToUInt16(ba, 148);
            result.VerticalResolution = BitConverter.ToUInt16(ba, 150);
            result.IsMonochrome = ba[152] != 0;
            result.Dpi = BitConverter.ToSingle(ba, 153);
            result.PixelSize = BitConverter.ToSingle(ba, 157);
            result.InputType = (DeviceInputType)ba[161];
            if (!BitConverter.IsLittleEndian)
            {
                result.VersionMajor = Swap(result.VersionMajor);
                result.VersionMinor = Swap(result.VersionMinor);
                result.Build = Swap(result.Build);
                result.Id = Swap(result.Id);
                result.HorizontalResolution = Swap(result.HorizontalResolution);
                result.VerticalResolution = Swap(result.VerticalResolution);
                result.Dpi = Swap(result.Dpi);
                result.PixelSize = Swap(result.PixelSize);
            }
            return result;
        }
    }
    SessionStatus _state = SessionStatus.Closed;
    Transport _transport;
    long _startTicks;
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
    DeviceInputType _inputType;

   int _screenIndex = -1;
    ConcurrentQueue<(int Cmd, object Data)> _requests = new();
    Dictionary<string, (long TimestampTicks, Screen Screen)> _screenCache = new(StringComparer.Ordinal);
    public event PropertyChangedEventHandler? PropertyChanged;
    
    public Session(SerialPort port)
    {
        ArgumentNullException.ThrowIfNull(port, nameof(port));
        _transport = new SerialTransport(port, 8192, 8192);
        
        InitializeComponent();
    }

    public Session(SerialPort port, IContainer container)
    {
        ArgumentNullException.ThrowIfNull(port, nameof(port));
        _transport = new SerialTransport(port, 8192, 8192);
        container.Add(this);
        InitializeComponent();
    }
    public string PortName
    {
        get
        {
            return _transport.Name;
        }
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
        if (_screenCache.Remove(name) && n!=null && name.Equals(n,StringComparison.Ordinal))
        {
            _requests.Enqueue((0, (sbyte)_screenIndex));
        }
    }
    Screen? GetScreen(string name)
    {
        var path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Espmon");
        var filePath = Path.Join(path, $"{name}.screen.json");
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
    public float Dpi
    {
        get { return _dpi; }
    }
    public float PixelSize
    {
        get { return _pixelSize; }
    }
    public string Name
    {
        get
        {
            if (_macAddress==null || _state==SessionStatus.Closed || _state==SessionStatus.Connecting)
            {
                return PortName.ToLowerInvariant();
            }
            return $"{PortName.ToLowerInvariant()} ({MacToString(_macAddress,false)})";
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
            if(_device!=null && _screenIndex>-1)
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
                    _screenIndex = idx;
                    if (_screenIndex > -1)
                    {
                        _requests.Enqueue((5, (sbyte)_screenIndex));
                        _requests.Enqueue((0, (sbyte)_screenIndex));
                    }
                    Debug.WriteLine($"Set CurrentScreenName to {value}, index {_screenIndex}");
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
                    if (_screenIndex > -1)
                    {
                        var idx = _screenIndex % _device.Screens.Count;
                        _requests.Enqueue((5, (sbyte)_screenIndex));
                        _requests.Enqueue((0, (sbyte)idx));
                    }
                    Debug.WriteLine($"Set CurrentScreenIndex to {value}, index {_screenIndex}");
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
        var j = isFile ? "_" : ":";
        return string.Join(j, mac.Select(b => b.ToString("X2")));
    }
    public SessionStatus Status => _state;

    public void Open()
    {
        if (_state == SessionStatus.Closed || _state == SessionStatus.RequiresFlash)
        {
            _state = SessionStatus.Connecting;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }     
    }
   
    public void Close()
    {
        if (_state != SessionStatus.Closed)
        {
            _state = SessionStatus.Closed;
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
    }
    private sealed class InnerOpenFlashProgress : IProgress<int>
    {
        IOpenFlashProgress _inner;
        public InnerOpenFlashProgress(IOpenFlashProgress inner)
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
        public static InnerOpenFlashProgress? Wrap(IOpenFlashProgress? progress)
        {
            if (progress == null) return null;
            return new InnerOpenFlashProgress(progress);
        }
    }
    public async Task ResetAsync(IOpenFlashProgress? progress = null, CancellationToken cancellationToken=default)
    {
        var isOpen = _transport.IsOpen;
        InnerOpenFlashProgress? innerProgress = (progress == null) ? null : new InnerOpenFlashProgress(progress);
        innerProgress?.SetAction("Closing...");
        var prog = int.MinValue;
        innerProgress?.Report(prog++);
        Close();
        innerProgress?.Report(prog++);
        Update();
        var espLink = new EspLink(_transport.Name);
        innerProgress?.SetAction("Connecting...");
        await espLink.ConnectAsync(EspConnectMode.Default, 3, false, espLink.DefaultTimeout,innerProgress,cancellationToken);
        innerProgress?.SetAction("Resetting...");
        await espLink.ResetAsync(null, cancellationToken);
        if (isOpen)
        {
            innerProgress?.SetAction("Connecting...");
            await Task.Delay(1000);
            Open();
            Update();
        }
    }
    public async Task FlashAsync(bool noReset,FirmwareEntry firmwareEntry, IOpenFlashProgress? progress = null, CancellationToken cancellationToken = default)
    {
        using var stm = Assembly.GetExecutingAssembly()?.GetManifestResourceStream("Espmon.firmware.boards.zip");
        if(stm==null)
        {
            throw new InvalidProgramException("boards not found in executable resources");
        }
        var slug = firmwareEntry.Slug;
        using var archive = new ZipArchive(stm);
        var entries = new Dictionary<string,ZipArchiveEntry>();
        for (var i = 0; i < archive.Entries.Count; i++)
        {
            var entry = archive.Entries[i]; 
            if(entry.FullName.StartsWith(slug+"\\"))
            {
                entries.Add(entry.Name, entry);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if(entries.Count!=3)
        {
            throw new InvalidProgramException("Files missing from boards");
        }
        if(!entries.ContainsKey($"partition-table.bin") || !entries.ContainsKey("bootloader.bin") || !entries.ContainsKey("firmware.bin"))
        {
            throw new InvalidProgramException("Files missing from boards");
        }
        var isOpen = _transport.IsOpen;
        Close();
        if(_transport.IsOpen)
        {
            Update();
        }
        
        _state = SessionStatus.Flashing;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        cancellationToken.ThrowIfCancellationRequested();
        MemoryStream? tmp = new MemoryStream();
        var espLink = new EspLink(_transport.Name);
        var innerProgress = InnerOpenFlashProgress.Wrap(progress);
        cancellationToken.ThrowIfCancellationRequested();
        innerProgress?.SetAction("Connecting...");
        await espLink.ConnectAsync(noReset?EspConnectMode.NoReset:EspConnectMode.Default, 3, false, espLink.DefaultTimeout, innerProgress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        innerProgress?.SetAction("Running stub...");
        await espLink.RunStubAsync(espLink.DefaultTimeout, innerProgress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await espLink.SetBaudRateAsync(921600, espLink.DefaultTimeout, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var file = entries["partition-table.bin"];
        using var partStm = file.Open();
        tmp.Seek(0, SeekOrigin.Begin);
        tmp.SetLength(0);
        cancellationToken.ThrowIfCancellationRequested();
        await partStm.CopyToAsync(tmp,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        tmp.Seek(0, SeekOrigin.Begin);
        partStm.Close();
        innerProgress?.SetAction("Flashing partition table...");
        cancellationToken.ThrowIfCancellationRequested();
        await espLink.FlashAsync(tmp, true, 0,firmwareEntry.Offset.Partitiions, 3, false, espLink.DefaultTimeout, innerProgress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        file = entries["bootloader.bin"];
        using var bootStm = file.Open();
        tmp.Seek(0, SeekOrigin.Begin);
        tmp.SetLength(0);
        cancellationToken.ThrowIfCancellationRequested();
        await bootStm.CopyToAsync(tmp,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        tmp.Seek(0, SeekOrigin.Begin);
        innerProgress?.SetAction("Flashing bootloader...");
        cancellationToken.ThrowIfCancellationRequested();
        await espLink.FlashAsync(tmp, true, 0, firmwareEntry.Offset.Bootloader, 3, false, espLink.DefaultTimeout, innerProgress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        bootStm.Close();
        file = entries["firmware.bin"];
        using var appStm = file.Open();
        tmp.Seek(0, SeekOrigin.Begin);
        tmp.SetLength(0);
        cancellationToken.ThrowIfCancellationRequested();
        await appStm.CopyToAsync(tmp,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        tmp.Seek(0, SeekOrigin.Begin);
        appStm.Close();
        innerProgress?.SetAction("Flashing application...");
        cancellationToken.ThrowIfCancellationRequested();
        await espLink.FlashAsync(tmp, true, 0, firmwareEntry.Offset.Firmware, 3, false, espLink.DefaultTimeout, innerProgress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        tmp.Close();
        tmp = null;
        innerProgress?.SetAction("Resetting device...");
        innerProgress?.Report(int.MinValue);
        await espLink.ResetAsync();
        _state = SessionStatus.Closed;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        if (isOpen)
        {
            Open();
            Update();
        }
    }
    private void LoadDeviceByMac()
    {
        if (_macAddress == null) throw new InvalidOperationException("The session has not been started");
        var path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Espmon");
        var mac = MacToString(_macAddress,true);
        var pattern = $"*.{mac}.device.json";
        var files = Directory.GetFiles(path, pattern);
        if (files.Length == 0) {
            var result = new Device();
            result.Id = _id;
            result.MacAddress = _macAddress;
            result.DisplayName = _displayName;
            result.HorizontalResolution = _hres;
            result.VerticalResolution= _vres;
            result.IsMonochrome = _isMonochrome;
            result.Dpi = _dpi;
            result.PixelSize = _pixelSize;
            result.InputType = _inputType;
            var fileName = Path.Join(path, $"{_transport.Name}.{mac}.device.json");
            using var writer = new StreamWriter(fileName, false, Encoding.UTF8);
            result.WriteTo(writer);
            if(_device!=null)
            {
                _device.PropertyChanged -= _device_PropertyChanged;
                _device.Screens.CollectionChanged -= _device_Screens_CollectionChanged;
            }
            _device = result;
            _device.PropertyChanged += _device_PropertyChanged;
            _device.Screens.CollectionChanged += _device_Screens_CollectionChanged;
            return;
        }
        var fullPath = files[0];
        var file = Path.GetFileName(fullPath);
        var idx = file.IndexOf('.');
        var portName = file.Substring(0, idx);
        if(portName!=_transport.Name)
        {
            var tmp = Path.Join(path, $"{_transport.Name}.{mac}.device.json");
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }
            File.Move(fullPath, tmp);
            fullPath = tmp;
        }
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
        if (_device!.MacAddress == null) throw new InvalidOperationException("The device is incomplete");

        var path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Espmon");
        var mac = MacToString(_device.MacAddress, true);
        var fullPath = Path.Join(path, $"{_transport.Name}.{mac}.device.json");
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
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
    private static ushort Swap(ushort data) { return (ushort)((data >> 8) | ((data << 8) & 0xFF00)); }
    private static short Swap(short data) { return unchecked((short)((data >> 8) | ((data << 8) & 0xFF00))); }
    private static float Swap(float data) { var arr = BitConverter.GetBytes(data); arr.Reverse(); return BitConverter.ToSingle(arr); }
    private static ulong Swap(ulong data) { var arr = BitConverter.GetBytes(data); arr.Reverse(); return BitConverter.ToUInt64(arr); }

    private static void BuildScreenValueEntryPart(ScreenValueEntry value, byte[] ba, ref int offset)
    {
        ba[offset++] = (byte)((value.Color >> 24) & 0xFF);
        ba[offset++] = (byte)((value.Color >> 16) & 0xFF);
        ba[offset++] = (byte)((value.Color >> 8) & 0xFF);
        ba[offset++] = (byte)((value.Color >> 0) & 0xFF);
        int startText = offset;
        var suffix = value.Entry.Unit ?? "";
        Span<byte> buffer = stackalloc byte[12];
        while (!Encoding.UTF8.TryGetBytes(suffix, buffer, out _)) { suffix = suffix[^1..]; }
        //Array.Clear(ba, offset, 12);
        buffer.CopyTo(ba.AsSpan(offset));
        offset = startText + 12;
    }
    private static void BuildScreenEntryPart(ScreenEntry entry, byte[] ba, ref int offset)
    {
        int start = offset;
        var label = entry.Label ?? "";
        Span<byte> buffer = stackalloc byte[16];
        while (!Encoding.UTF8.TryGetBytes(label, buffer, out _)) { label = label[^1..]; }
        //Array.Clear(ba, offset, 16);
        buffer.CopyTo(ba.AsSpan(offset));
        offset = start + 16;
        // color = (a << 24) | (r << 16) | (g << 8) | b;
        ba[offset++] = (byte)((entry.Color >> 24) & 0xFF);
        ba[offset++] = (byte)((entry.Color >> 16) & 0xFF);
        ba[offset++] = (byte)((entry.Color >> 8) & 0xFF);
        ba[offset++] = (byte)((entry.Color >> 0) & 0xFF);
        BuildScreenValueEntryPart(entry.Value1!, ba, ref offset);
        BuildScreenValueEntryPart(entry.Value2!, ba, ref offset);
    }
    private static byte[] BuildScreenPacket(int index,Screen screen)
    {
        ArgumentNullException.ThrowIfNull(screen, nameof(screen));
        if (screen.Top == null) throw new ArgumentException("Top was null", nameof(screen));
        if (screen.Bottom == null) throw new ArgumentException("Bottom was null", nameof(screen));
        if(screen.Top.Value1 == null) throw new ArgumentException("Screen is incomplete", nameof(screen));
        if (screen.Top.Value2 == null) throw new ArgumentException("Screen is incomplete", nameof(screen));
        if (screen.Bottom.Value1 == null) throw new ArgumentException("Screen is incomplete", nameof(screen));
        if (screen.Bottom.Value2 == null) throw new ArgumentException("Screen is incomplete", nameof(screen));
        
        var result = new byte[109];
        var offset = 0;
        result[offset++] = 0; // CMD_SCREEN
        result[offset++] = unchecked((byte)index);
        byte flags = 0;
        if (screen.Top.Value1.HasGradient) { flags |= (1 << 0); }
        if (screen.Top.Value2.HasGradient) { flags |= (1 << 1); }
        if (screen.Bottom.Value1.HasGradient) { flags |= (1 << 2); }
        if (screen.Bottom.Value2.HasGradient) { flags |= (1 << 3); }
        result[offset++] = flags;
        BuildScreenEntryPart(screen.Top, result, ref offset);
        BuildScreenEntryPart(screen.Bottom, result, ref offset);
        return result;
    }
    private static void BuildDataValue(ScreenValueEntry value, byte[] ba, ref int offset)
    {
        var v= value.Value;
        var s =value.Scaled;
        if(!BitConverter.IsLittleEndian) { v = Swap(v); s = Swap(s); }
        Array.Copy(BitConverter.GetBytes(v), 0, ba, offset, 4);
        offset += 4;
        Array.Copy(BitConverter.GetBytes(s), 0, ba, offset, 4);
        offset += 4;
    }
    private static byte[] BuildDataPacket(Screen screen)
    {
        ArgumentNullException.ThrowIfNull(screen, nameof(screen));
        if (screen.HardwareInfo == null) throw new ArgumentException("HardwareInfo was null", nameof(screen));
        if (screen.Top == null) throw new ArgumentException("Top was null", nameof(screen));
        if (screen.Bottom == null) throw new ArgumentException("Bottom was null", nameof(screen));
        if (screen.Top.Value1 == null) throw new ArgumentException("Screen is incomplete", nameof(screen));
        if (screen.Top.Value2 == null) throw new ArgumentException("Screen is incomplete", nameof(screen));
        if (screen.Bottom.Value1 == null) throw new ArgumentException("Screen is incomplete", nameof(screen));
        if (screen.Bottom.Value2 == null) throw new ArgumentException("Screen is incomplete", nameof(screen));
        var result = new byte[33];
        var offset = 0;
        result[offset++] = 1;
        BuildDataValue(screen.Top.Value1, result, ref offset);
        BuildDataValue(screen.Top.Value2, result, ref offset);
        BuildDataValue(screen.Bottom.Value1, result, ref offset);
        BuildDataValue(screen.Bottom.Value2, result, ref offset);
        return result;
    }
    public void Update()
    {
        if (_transport.IsOpen)
        {
            var done = false;
            while (!done && _transport.AvaialableLength > 0)
            {
                var cmd = _transport.ReadByte();
                if (cmd == _transport.ReadByte() && cmd == _transport.ReadByte())
                {
                    switch (cmd)
                    {
                        case 0: // need screen
                            {
                                var b = _transport.ReadByte();
                                if(b==-1)
                                {
                                    _transport.DiscardAvailable();
                                    done = true;
                                    break;
                                }
                                var scnidx = unchecked((sbyte)b);
                                if (_device == null) break;
                                if (_device.Screens.Count == 0) break;
                                if (_transport.IsOpen && _device != null && scnidx > -1)
                                {
                                    var idx = scnidx % _device.Screens.Count;
                                    var name = _device.Screens[idx];
                                    var screen = GetScreen(name);
                                    if (screen != null)
                                    {
                                        var screenPacket = BuildScreenPacket(idx, screen);
                                        try
                                        {
                                            _transport.Send(screenPacket, 0, screenPacket.Length);
                                        }
                                        catch
                                        {
                                            if (!_transport.IsOpen)
                                            {
                                                Close();
                                            }
                                            return;
                                        }
                                        _screenIndex = idx;
                                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentScreenIndex)));
                                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentScreen)));
                                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentScreenName)));
                                    }
                                }
                            }
                            break;
                        case 1: // need data
                            {
                                var b = _transport.ReadByte();
                                if (b == -1)
                                {
                                    _transport.DiscardAvailable();
                                    done = true;
                                    break;
                                }
                                var screenIndex = unchecked((sbyte)b);
                                if (_device != null)
                                {
                                    if (_device.Screens.Count > 0)
                                    {
                                        if (_transport.IsOpen && _screenIndex == (sbyte)screenIndex)
                                        {
                                            var name = _device.Screens[_screenIndex % _device.Screens.Count];
                                            var screen = GetScreen(name);
                                            if (screen != null)
                                            {
                                                var dataPacket = BuildDataPacket(screen);
                                                try
                                                {
                                                    _transport.Send(dataPacket, 0, dataPacket.Length);
                                                }
                                                catch
                                                {
                                                    if (!_transport.IsOpen)
                                                    {
                                                        Close();
                                                    }
                                                }

                                            }
                                        }
                                    }
                                }
                            }
                            break;
                        case 2: // mode change
                            {
                                var b = _transport.ReadByte();
                                if (b == -1)
                                {
                                    _transport.DiscardAvailable();
                                    done = true;
                                }
                                break;
                            }
                        case 3: // firmware ident
                      
                            var data = new byte[162];
                            _transport.Receive(data, 0, data.Length);
                            var ident = FirmwareIdent.Read(data);
                            _versionMajor = ident.VersionMajor;
                            _versionMinor = ident.VersionMinor;
                            _build = ident.Build;
                            var appBuild = FirmwareBuild.Timestamp;
                            var diff = TimeSpan.FromSeconds((long)(appBuild - _build));
                            if (appBuild > _build)
                            {
                                _state = SessionStatus.RequiresFlash;
                                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
                                done = true;
                                break;
                            }
                            _id = ident.Id;
                            _macAddress = ident.MacAddress;
                            _displayName = ident.DisplayName;
                            _slug = ident.Slug;
                            _hres = ident.HorizontalResolution;
                            _vres = ident.VerticalResolution;
                            _isMonochrome = ident.IsMonochrome;
                            _dpi = ident.Dpi;
                            _pixelSize = ident.PixelSize;
                            _inputType = ident.InputType;
                            LoadDeviceByMac();
                            _state = SessionStatus.Busy;
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Id)));
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HorizontalResolution)));
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VerticalResolution)));
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMonochrome)));
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PixelSize)));
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Dpi)));
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Device)));
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
                            // set the screen
                            if (_device != null && _device.Screens.Count > 0)
                            {
                                var name = _device.Screens[0];
                                var screen = GetScreen(name);
                                if (screen != null)
                                {
                                    CurrentScreenIndex = 0;
                                }
                            }
                            break;
                        case 4: // NOP
                            {
                                var b = _transport.ReadByte();
                                if (b == -1)
                                {
                                    _transport.DiscardAvailable();
                                    done = true;
                                    break;
                                }
                                break;
                            }
                        default:
                            _transport.DiscardAvailable();
                            break;
                    }
                }
            }
        }
        switch (_state)
        {
            case SessionStatus.Connecting:
                if (!_transport.IsOpen)
                {
                    try
                    {
                        _transport.Open();
                    }
                    catch { return; }
                    _startTicks = DateTimeOffset.UtcNow.Ticks;
                    _state = SessionStatus.Negotiating;
                    try
                    {
                        _transport.Send([3], 0, 1);
                    } catch
                    {
                        Close();
                        return;
                    }
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
                    
                }

                break;
            case SessionStatus.Negotiating:
                if ((DateTimeOffset.UtcNow.Ticks - _startTicks) > TimeSpan.FromSeconds(3).Ticks)
                {
                    _state = SessionStatus.RequiresFlash;

                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
                }
                else
                {
                    try
                    {
                        _transport.Send([3], 0, 1);
                    }
                    catch
                    {
                        Close();
                    }
                }

                break;
            case SessionStatus.Closed:
                if (_transport.IsOpen)
                {
                    _transport.Close();

                    if (_device != null)
                    {
                        _device.PropertyChanged -= _device_PropertyChanged;
                        _device.Screens.CollectionChanged -= _device_Screens_CollectionChanged;
                        _device = null;
                    }
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Device)));
                }
                break;
            default:
                if (_transport.TimeSinceLastReceived >= TimeSpan.FromSeconds(5))
                {
                        
                    Debug.WriteLine("Port closing due to timeout");
                    Close();
                    return;
                }
                if (_transport.TimeSinceLastSent>=TimeSpan.FromMilliseconds(500))
                {
                    try
                    {
                        _transport.Send([4], 0, 1);
                    }
                    catch
                    {
                        Close();
                        return;
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
public interface IOpenFlashProgress : IProgress<FlashProgressEntry>
{
    
}
