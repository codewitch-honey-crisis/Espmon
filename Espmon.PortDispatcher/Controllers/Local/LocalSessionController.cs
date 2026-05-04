using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Versioning;

namespace Espmon;

[SupportedOSPlatform("windows")]
internal class LocalSessionController : SessionController
{
#if LOG_SERIAL
#if DEBUG
    const bool _logging = true;
#else
    const bool _logging = false;
#endif
#else
    const bool _logging = false;
#endif
    EspSerialSession _transport;
    long _startIdentTicks;
    long _gotIdentTicks;
    int _needScreen;
    bool _dataReady = false;
    bool _needsFlash = false;
    RequestIdent? _ident = null;
    public LocalSessionController(PortController parent, string portName, string serialNumber,DeviceController? device) : base(parent, portName, serialNumber)
    {
        Device = device;
        _transport = new EspSerialSession(PortName, _logging, SyncContext);
        _transport.ConnectionError += _transport_ConnectionError;
        _transport.FrameError += _transport_FrameError;
        _transport.FrameReceived += _transport_FrameReceived;
    }

    private void _transport_FrameReceived(object? sender, FrameReceivedEventArgs e)
    {
        if (_transport.IsOpen)
        {
            switch ((Command)e.Command)
            {
                case Command.CmdScreen:
                    {
                       if (RequestScreen.TryRead(e.Data, out var req, out _))
                        {
                            _needScreen = req.ScreenIndex;
                            _dataReady = false;

                        }
                    }
                    break;
                case Command.CmdData:
                    {
                        if (RequestData.TryRead(e.Data, out var req, out _))
                        {
                            if (req.ScreenIndex == ScreenIndex)
                            {
                                if (!_dataReady)
                                {
                                    _dataReady = true;
                                }
                            }
                        }
                    }
                    break;
                case Command.CmdIdent:
                    RequestIdent.TryRead(e.Data, out _ident, out _);
                    _gotIdentTicks = Stopwatch.GetTimestamp();
                    break;

            }
        }
    }

    private void _transport_FrameError(object? sender, FrameReceivedEventArgs e)
    {
        
    }

    private void _transport_ConnectionError(object? sender, EventArgs e)
    {
        Disconnect();
    }

    protected override void OnConnect()
    {

        if (Status == SessionStatus.Closed || Status == SessionStatus.RequiresFlash)
        {
            Status = SessionStatus.Connecting;
            _dataReady = false;
            _needScreen = ScreenIndex;
            _startIdentTicks = 0;
            _gotIdentTicks = 0;
            _needsFlash = false;
            _transport.Open();
        }
    }
    protected override void OnDisconnect()
    {
        if (_transport.IsOpen)
        {
            _transport.Close();
        }
        Status = SessionStatus.Closed;
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
    protected override async Task OnFlashAsync(FirmwareEntry firmwareEntry, IFlashProgress? progress, CancellationToken cancellationToken)
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
            Disconnect();
        }
        Status = SessionStatus.Flashing;
        
        innerProgress?.SetAction("Extracting firmware...");
        prog = int.MinValue;
        var path = ((LocalPortController)Parent).Path;
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
        path = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory) ?? "", "esptool.exe");
        var cmdLine = $"--baud 921600 --port {PortName} write_flash 0x{firmwareEntry.Offsets.Partitiions:X} \"{pathPartition}\" 0x{firmwareEntry.Offsets.Bootloader:X} \"{pathBootloader}\" 0x{firmwareEntry.Offsets.Firmware:X} \"{pathFirmware}\"";
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
                if (line != null)
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
                                    }, null);
                                }
                            }
                        }
                    }
                }
            }
        }, cancellationToken);
        await procTask;
        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException(proc.StandardError.ReadToEnd()+Environment.NewLine+proc.StandardOutput.ReadToEnd());
        }
        path = ((LocalPortController)Parent).Path;
        var path2 = Path.Combine(path, "partition-table.bin");
        try { File.Delete(path2); } catch { }
        path2 = Path.Combine(path, "bootloader.bin");
        try { File.Delete(path2); } catch { }
        path2 = Path.Combine(path, "firmware.bin");
        try { File.Delete(path2); } catch { }
        _needsFlash = false;
        Status = SessionStatus.Closed;
        if (wasOpen)
        {
            Connect();
        }
    }
    protected override async Task OnResetAsync(IFlashProgress? progress, CancellationToken cancellationToken)
    {
        // esptool.py --no-stub flash_id
        var wasOpen = _transport != null && _transport.IsOpen;
        InnerOpenFlashProgress? innerProgress = (progress == null) ? null : new InnerOpenFlashProgress(progress);
        var prog = int.MinValue;
        if (wasOpen)
        {
            innerProgress?.SetAction("Closing...");
            innerProgress?.Report(prog++);
            Disconnect();
        }
        innerProgress?.SetAction("Resetting...");
        Status = SessionStatus.Resetting;
        innerProgress?.Report(prog++);
        var path = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory) ?? "", "esptool.exe");
        var cmdLine = $"--port {PortName} --no-stub flash_id";
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
        await proc.WaitForExitAsync(cancellationToken);
        if (wasOpen)
        {
            Connect();
        }
        else
        {
            Status = SessionStatus.Closed;
        }
    }
    private static ResponseValue _ToResponseData(ScreenValueController valueCtrl)
    {
        var result = new ResponseValue();
        result.Value = valueCtrl.Value;
        result.Scaled = valueCtrl.Scaled;
        return result;
    }
    private static ResponseValueEntry _ToResponseData(ScreenValuesController valuesCtrl)
    {
        var result = new ResponseValueEntry();
        result.Value1 = _ToResponseData(valuesCtrl.Value1);
        result.Value2 = _ToResponseData(valuesCtrl.Value2);
        return result;
    }
    private ResponseData _ToResponseData(ScreenController scrCtrl)
    {
        var result = new ResponseData();
        result.Top = _ToResponseData(scrCtrl.Top);
        result.Bottom = _ToResponseData(scrCtrl.Bottom);
        return result;
    }
    private ResponseColor _ToResponseScreen(int color)
    {
        return new ResponseColor()
        {
            A = (byte)(color >> 24),
            R = (byte)((color >> 16) & 0xFF),
            G = (byte)((color >> 8) & 0xFF),
            B = (byte)((color >> 0) & 0xFF)
        };
    }
    private ResponseScreenValueEntry _ToResponseScreen(ScreenValueController valueCtrl)
    {
        return new ResponseScreenValueEntry()
        {
            Color = _ToResponseScreen(valueCtrl.Color),
            Suffix = valueCtrl.Entry.Unit
        };
    }
    private ResponseScreenEntry _ToResponseScreen(ScreenValuesController valuesCtrl)
    {
        return new ResponseScreenEntry()
        {
            Color = _ToResponseScreen(valuesCtrl.Color),
            Label = valuesCtrl.Label,
            Value1 = _ToResponseScreen(valuesCtrl.Value1),
            Value2 = _ToResponseScreen(valuesCtrl.Value2)
        };
        
    }


    private ResponseScreen _ToResponseScreen(ScreenController scrCtrl)
    {
        byte flags = 0;
        if (scrCtrl.Top.Value1.HasGradient)
        {
            flags = (byte)(1 << 0);
        }
        if (scrCtrl.Top.Value2.HasGradient)
        {
            flags |= (byte)(1 << 1);
        }
        if (scrCtrl.Bottom.Value1.HasGradient)
        {
            flags |= (byte)(1 << 2);
        }
        if (scrCtrl.Bottom.Value2.HasGradient)
        {
            flags |= (byte)(1 << 3);
        }
        return new ResponseScreen()
        {
            Header = new ResponseScreenHeader()
            {
                Index = (sbyte)ScreenIndex,
                Flags = flags
            },
            Top = _ToResponseScreen(scrCtrl.Top),
            Bottom = _ToResponseScreen(scrCtrl.Bottom)
        };
    }
    protected override void OnScreenData(ScreenDataEventArgs args)
    {
        if (Screen != null)
        {
            var packet = _ToResponseData(Screen);
            Span<byte> tmp = stackalloc byte[ResponseData.StructMaxSize];
            if (packet.TryWrite(tmp, out _))
            {
                try
                {
                    _transport.Send((byte)Command.CmdData, tmp);
                    _dataReady = false;
                }
                catch (Win32Exception)
                {
                    Disconnect();
                }
            }
        }
        base.OnScreenData(args);
    }
    protected override void OnScreenIndexChanged()
    {
        _dataReady = false;
        var clearArgs = EventArgs.Empty;
        OnScreenCleared(clearArgs);
        var changeArgs = new ScreenChangedEventArgs(ScreenIndex);
        OnScreenChanged(changeArgs);
    }
    protected override void OnScreenCleared(EventArgs args)
    {
        if (_transport.IsOpen)
        {
            _transport.Send((byte)Command.CmdClear, Array.Empty<byte>());
        }

        base.OnScreenCleared(args);
    }
    protected override void OnScreenChanged(ScreenChangedEventArgs args)
    {
        if (Screen != null)
        {
            var packet = _ToResponseScreen(Screen);
            Span<byte> tmp = stackalloc byte[ResponseScreen.StructMaxSize];
            if (packet.TryWrite(tmp, out _))
            {
                try
                {
                    _transport.Send((byte)Command.CmdScreen, tmp);
                    _dataReady = false;
                }
                catch (Win32Exception)
                {
                    Disconnect();
                }
            }
        }
        base.OnScreenChanged(args);
    }
    static string _MacToString(byte[] mac, bool isFile)
    {
        var j = isFile ? "-" : ":";
        return string.Join(j, mac.Select(b => b.ToString("X2")));
    }
    protected override void OnRefresh()
    {
#if LOG_SERIAL
#if DEBUG
        if (_logging)
        {
            var dbgba = _transport.GetNextLogData();
            if (dbgba.Length > 0)
            {
                var dbgstr = Encoding.UTF8.GetString(dbgba);
                Debug.WriteLine(dbgstr);
            }
        }
#endif
#endif
        switch (Status)
        {
            case SessionStatus.Connecting:
                if (_transport != null && _transport.IsOpen && _startIdentTicks == 0)
                {
                    _startIdentTicks = Stopwatch.GetTimestamp();
                    try
                    {
                        _transport.Send((byte)Command.CmdIdent, Array.Empty<byte>());
                        Status = SessionStatus.Negotiating;
                    }
                    catch (Win32Exception)
                    {
                        Disconnect();
                    }
                }
                break;
            case SessionStatus.Busy:

                if (_needScreen > -1 && _transport != null && _transport.IsOpen && Device != null && Device.Screens.Count > 0)
                {
                    ScreenIndex = _needScreen % Device.Screens.Count;
                    _needScreen = -1;
                } else if (_dataReady && ScreenIndex > -1 && _transport != null && _transport.IsOpen)
                {
                    var scr = Screen;
                    if (scr != null)
                    {
                        var args = new ScreenDataEventArgs(
                            ScreenIndex,
                            scr.Top.Value1.Value,
                            scr.Top.Value1.Scaled,
                            scr.Top.Value2.Value,
                            scr.Top.Value2.Scaled,
                            scr.Bottom.Value1.Value,
                            scr.Bottom.Value1.Scaled,
                            scr.Bottom.Value2.Value,
                            scr.Bottom.Value2.Scaled
                        );
                        OnScreenData(args);

                    }
                }
                break;
            case SessionStatus.Negotiating:
                if (_transport != null && _transport.IsOpen && _startIdentTicks != 0)
                {
                    if (_needsFlash)
                    {
                        Status = SessionStatus.RequiresFlash;
                    }
                    else if (_gotIdentTicks != 0 && _ident != null)
                    {
                        var device = Parent.GetDeviceByMac(_ident.MacAddress);
                        if (device == null)
                        {
                            device = new DeviceController(this.Parent, _MacToString(_ident.MacAddress, true));
                            device.SerialNumbers = [SerialNumber];
                            Parent.Devices.Add(device);
                        }
                        else
                        {
                            if(!device.SerialNumbers.Contains(SerialNumber))
                            {
                                var sns = new string[device.SerialNumbers.Length + 1];
                                device.SerialNumbers.CopyTo(sns, 0);
                                sns[device.SerialNumbers.Length]=SerialNumber;
                                device.SerialNumbers = sns;
                            }
                            Device = device;
                        }
                        Id = _ident.ID;
                        VersionMajor = _ident.VersionMajor;
                        VersionMinor = _ident.VersionMinor;
                        Build = _ident.Build;
                        DeviceName = _ident.DisplayName;
                        HorizontalResolution = _ident.HorizontalResolution;
                        VerticalResolution = _ident.VerticalResolution;
                        IsMonochrome = _ident.IsMonochrome;
                        Dpi = _ident.Dpi;
                        PixelSize = _ident.PixelSize;
                        Input = (DeviceInputType)_ident.InputType;
                        _ident = null;
                        if (GetUpgrade() == FirmwareUpgrade.Required)
                        {
                            _needsFlash = true;
                            Status = SessionStatus.RequiresFlash;
                        }
                        else
                        {
                            Status = SessionStatus.Busy;
                        }
                    }
                    else if (TimeSpan.FromTicks(Stopwatch.GetTimestamp() - _startIdentTicks).TotalSeconds > 3)
                    {
                        _needsFlash = true;
                        Status = SessionStatus.RequiresFlash;
                    }
                }
                break;
        }
    }
    protected override void OnDispose()
    {
        _transport?.Close();
    }
}
