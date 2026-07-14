using Espmon.Service;

using Microsoft.UI.Xaml;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

using Windows.System;

namespace Espmon;

[SupportedOSPlatform("windows")]
public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private enum ExitServiceTask
    {
        None,
        InstallAndStart,
        Remove
    }
    private Elevator _elevator;
    private int _devicePanelIndex;
    private ExitServiceTask _exitServiceTask = ExitServiceTask.None;
    public event PropertyChangedEventHandler? PropertyChanged;
    public PortController PortController { get; }
    private string _selectedPath = string.Empty;

    public ObservableCollection<string> Log { get; } = [];
    public ObservableCollection<ScreenListEntry> ScreenItems { get; } = [];
    private SessionController? _selectedSession;

    public Visibility DevicePanel1Visibility
    {
        get => _devicePanelIndex == 0 && SelectedSession != null ? Visibility.Visible : Visibility.Collapsed;
    }
    public Visibility DevicePanel2Visibility
    {
        get => _devicePanelIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }
    public Visibility DevicePanel3Visibility
    {
        get => _devicePanelIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }
    public int DevicePanelIndex
    {
        get => _devicePanelIndex;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 2);
            if (value != _devicePanelIndex)
            {
                _devicePanelIndex = value;
                OnPropertyChanged(nameof(DevicePanel1Visibility));
                OnPropertyChanged(nameof(DevicePanel2Visibility));
                OnPropertyChanged(nameof(DevicePanel3Visibility));
            }
        }
    }
    public SessionController? SelectedSession
    {
        get => _selectedSession;
        set
        {
            var changeDpv = _devicePanelIndex == 0 && (_selectedSession == null || value == null);
            if (_selectedSession != value)
            {
                if (_selectedSession != null)
                {
                    _selectedSession.PropertyChanged -= _selectedSession_PropertyChanged;
                }
                _selectedSession = value;
                if (_selectedSession != null)
                {
                    _selectedSession.PropertyChanged += _selectedSession_PropertyChanged;
                }
                OnPropertyChanged(nameof(ConnectVisibility));
                OnPropertyChanged(nameof(SelectedSession));
                OnPropertyChanged(nameof(SelectedSessionScreenMetrics));
                OnPropertyChanged(nameof(SessionOpenVisibility));
                OnPropertyChanged(nameof(SessionFlashVisibility));
                OnPropertyChanged(nameof(SessionRunningVisibility));
                OnPropertyChanged(nameof(SessionScreenListVisibility));
                if (changeDpv)
                {
                    OnPropertyChanged(nameof(DevicePanel1Visibility));
                }

            }
        }
    }
    private bool _flashRequested;
    public bool FlashRequested
    {
        get => _flashRequested;
        set
        {
            if (_flashRequested != value)
            {
                _flashRequested = value;
                OnPropertyChanged(nameof(FlashRequested));
                OnPropertyChanged(nameof(SessionFlashVisibility));
                OnPropertyChanged(nameof(SessionRunningVisibility));
                OnPropertyChanged(nameof(SessionOpenVisibility));
                OnPropertyChanged(nameof(SessionScreenListVisibility));
            }
        }
    }
    public string? SelectedSessionScreenMetrics
    {
        get
        {
            if (SelectedSession == null)
            {
                return null;
            }
            return $"{SelectedSession.HorizontalResolution}x{SelectedSession.VerticalResolution} @ {Math.Round(SelectedSession.Dpi)} dpi";
        }
    }

    private void _selectedSession_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SelectedSessionScreenMetrics));
        OnPropertyChanged(nameof(SessionOpenVisibility));
        OnPropertyChanged(nameof(SessionFlashVisibility));
        OnPropertyChanged(nameof(SessionRunningVisibility));
        OnPropertyChanged(nameof(SessionScreenListVisibility));
    }

    public Visibility SessionFlashVisibility
    {
        get
        {
            if (SelectedSession != null)
            {
                return FlashRequested || SelectedSession.GetUpgrade() != FirmwareUpgrade.NotRequired || SelectedSession.Status == SessionStatus.Flashing ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }
    }
    public Visibility SessionScreenListVisibility
    {
        get
        {
            if (SelectedSession != null)
            {
                return !SelectedSession.IsWaitingForScreenChange && SelectedSession.Device != null && SelectedSession.Status == SessionStatus.Busy || SelectedSession.Status == SessionStatus.ReadyForData ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }
    }
    public Visibility SessionOpenVisibility
    {
        get
        {
            if (FlashRequested) return Visibility.Collapsed;
            if (SelectedSession != null)
            {
                return SelectedSession.Status == SessionStatus.Closed ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }
    }
    public Visibility SessionRunningVisibility
    {
        get
        {
            if (FlashRequested) return Visibility.Collapsed;
            if (SelectedSession != null)
            {

                return SelectedSession.Device != null && SelectedSession.Status == SessionStatus.Busy || SelectedSession.Status == SessionStatus.ReadyForData || SelectedSession.Status == SessionStatus.NeedScreen ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }
    }
    public ObservableCollection<string> ValidationLog { get; } = new();

    public void Load()
    {
        if (StartWithWindows)
        {
            Exception? lastException = null;
            bool loaded = false;

            for (int i = 0; i < 10; i++)
            {
                // Fresh pipe each attempt — a timed-out / half-read pipe is never reused.
                using var pipe = new NamedPipeClientStream(
                    ".", "Espmon.Service", PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    pipe.Connect(500);

                    var req = new Espmon.Service.ServiceAppStartRequest();
                    var payload = new byte[req.SizeOfStruct];
                    PipeFrame.WriteFrame(pipe, (byte)ServiceCommand.AppStart, payload);

                    var res = PipeFrame.ReadFrameOrTimeout(pipe, TimeSpan.FromMilliseconds(2000));
                    ServiceAppStartResponse.TryRead(res.Payload, out var resp, out var _);

                    loaded = true;
                    break;
                }
                catch (Exception e)
                {
                    lastException = e;
                    Thread.Sleep(200);
                }
            }

            if (!loaded)
                throw lastException ?? new Exception("The service could not be communicated with");
        }
        PortController.Start();
        PortController.SessionStatusChanged += PortController_SessionStatusChanged;

    }

    private void PortController_SessionStatusChanged(object sender, SessionStatusChangedEventArgs args)
    {
        //Debug.WriteLine($"PortController signalled SessionStatusChange for {args.Session.Device?.Name??args.Session.PortName}: {args.Session.Status}");
        RefreshDevices();
    }

    private ProviderEntry[] GetProviderEntries()
    {
        var result = new ProviderEntry[PortController.Providers.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new ProviderEntry(PortController.Providers[i]);
        }
        return result;
    }
    IList<ProviderEntry>? _providerEntries = null;
    public IList<ProviderEntry> ProviderEntries
    {
        get
        {
            if (_providerEntries == null || _providerEntries.Count == 0)
            {
                _providerEntries = new List<ProviderEntry>(GetProviderEntries());
            }
            return _providerEntries;
        }
        set
        {
            _providerEntries = value;
            OnPropertyChanged(nameof(ProviderEntries));
            OnPropertyChanged(nameof(ProviderPanelVisibility));
        }
    }
    private ProviderEntry? _selectedProviderEntry = null;
    public ProviderEntry? SelectedProviderEntry
    {
        get
        {
            return _selectedProviderEntry;
        }
        set
        {
            if (_selectedProviderEntry != value)
            {
                _selectedProviderEntry = value;
                OnPropertyChanged(nameof(SelectedProviderEntry));
                OnPropertyChanged(nameof(ProviderPanelVisibility));
            }
        }
    }
    public Visibility ProviderPanelVisibility
    {
        get
        {
            return _selectedProviderEntry != null ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    public void RefreshProviders()
    {
        ProviderEntries = new List<ProviderEntry>(GetProviderEntries());
    }
    public void RefreshDevices()
    {
        if (PortController != null)
        {
            PortController.RefreshSessions();
        }
        SessionEntries = new List<SessionEntry>(GetSessionEntries());
    }
    private SessionEntry[] GetSessionEntries()
    {
        var result = new SessionEntry[Sessions.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new SessionEntry(Sessions[i]);
        }
        return result;
    }
    IList<SessionEntry>? _sessionEntries = null;
    public IList<SessionEntry> SessionEntries
    {
        get
        {
            if (_sessionEntries == null || _sessionEntries.Count == 0)
            {
                _sessionEntries = new List<SessionEntry>(GetSessionEntries());
            }
            return _sessionEntries;
        }
        set
        {
            _sessionEntries = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SessionEntries)));
        }
    }
    public SessionEntry? SelectedSessionEntry
    {
        get
        {
            var ses = SelectedSession;
            for (var i = 0; i < SessionEntries.Count; i++)
            {
                var entry = SessionEntries[i];
                if (object.ReferenceEquals(entry.Session, ses))
                {
                    return SessionEntries[i];
                }
            }
            return null;
        }
        set
        {
            var ses = value?.Session;
            if (ses != null)
            {
                for (var i = 0; i < SessionEntries.Count; i++)
                {
                    var entry = SessionEntries[i];
                    if (object.ReferenceEquals(entry.Session, ses))
                    {
                        if (SelectedSession != entry.Session)
                        {
                            SelectedSession = entry.Session;
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedSessionEntry)));
                        }
                        return;
                    }
                }
            }
            SelectedSession = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedSessionEntry)));
        }
    }
    public MainViewModel()
    {
        SettingsMessage = "";
        FlashMessage = "";
        ConnectMessage = "";
        _elevator = new Elevator();
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Espmon");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        

        PortController = new LocalPortController(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Espmon"), SynchronizationContext.Current);
        PortController.Screens.CollectionChanged += Screens_CollectionChanged;

        Load();



    }
    public ReadOnlyObservableCollection<SessionController> Sessions => PortController.Sessions;
    public FirmwareEntry[] FirmwareEntries => FirmwareEntry.GetFirmwareEntries();
    private void Screens_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewStartingIndex != -1 && e.NewItems != null)
                {
                    var i = 0;
                    foreach (ScreenController item in e.NewItems)
                    {
                        ScreenItems.Insert(e.NewStartingIndex + i, new ScreenListEntry(item.Name ?? "(null)", item));
                        ++i;
                    }
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldStartingIndex != -1 && e.OldItems != null)
                {
                    var i = e.OldItems.Count;
                    while (i-- > 0)
                    {
                        ScreenItems.RemoveAt(e.OldStartingIndex);
                    }
                }
                break;
            case NotifyCollectionChangedAction.Reset:
                ScreenItems.Clear();
                break;
            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Move:
                throw new NotImplementedException();
        }
    }

    public string SelectedPath
    {
        get => _selectedPath;
        set
        {
            if (_selectedPath != value)
            {
                _selectedPath = value;
                OnPropertyChanged(nameof(SelectedPath));
            }
        }
    }
    private ScreenController? _selectedScreen = null;
    private bool _isDisposed;

    public ScreenController? SelectedScreen
    {
        get => _selectedScreen;
        set
        {
            if (_selectedScreen != value)
            {
                _selectedScreen = value;
                OnPropertyChanged(nameof(SelectedScreen));
                OnPropertyChanged(nameof(SelectedScreenIndex));
            }
        }
    }
    private bool HasScreen(string name)
    {
        foreach (var scr in PortController.Screens)
        {
            if (scr.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
    public void NewScreen()
    {
        var baseName = "Screen";
        var newName = baseName;
        var i = 2;
        while (HasScreen(newName))
        {
            newName = $"{baseName} {i++}";
        }
        var scr = PortController.CreateScreen(newName);
        PortController.Screens.Add(scr);
    }
    public int SelectedScreenIndex
    {
        get
        {
            var scr = SelectedScreen;
            if (ScreenItems.Count > 0)
            {
                for (var i = 0; i < ScreenItems.Count; i++)
                {
                    var entry = ScreenItems[i];
                    if (object.ReferenceEquals(entry.Screen, scr))
                    {
                        return i;
                    }
                }
                return 0;
            }
            return -1;
        }
        set
        {
            var changed = false;
            if (value < 0)
            {
                if (ScreenItems.Count > 0)
                {
                    changed = SelectedScreen != ScreenItems[0].Screen;
                    SelectedScreen = ScreenItems[0].Screen;
                }
                else
                {
                    if (SelectedScreen != null)
                    {
                        SelectedScreen = null;
                        changed = true;
                    }
                }
            }
            else
            {
                var scr = ScreenItems[value].Screen;
                if (_selectedScreen != scr)
                {
                    changed = true;
                    _selectedScreen = scr;
                }
            }
            if (changed)
            {

                PortController.ViewSession.ScreenIndex = value;

                OnPropertyChanged(nameof(SelectedScreen));
                OnPropertyChanged(nameof(SelectedScreenIndex));
            }
        }
    }
    public string AppDataPath
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Espmon");
        }
    }
    public void AddValidationLog(string message)
    {
        ValidationLog.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");

        // Keep only last 20 entries
        while (ValidationLog.Count > 20)
        {
            ValidationLog.RemoveAt(ValidationLog.Count - 1);
        }
    }


    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                bool serviceRunning = File.Exists(@"\\.\pipe\Espmon.Service");
                if (PortController != null)
                {
                    PortController.SessionStatusChanged -= PortController_SessionStatusChanged;
                    PortController.Stop();
                    PortController.Dispose();
                }
                if (_exitServiceTask == ExitServiceTask.InstallAndStart)
                {
                    // Turning ON at exit. The service we're about to start uses the
                    // same COM ports as the app, so release the app's ports FIRST,
                    // then install + start so the service can take over cleanly.
                    RunExitServiceTask();
                }
                else if (_exitServiceTask == ExitServiceTask.Remove)
                {
                    // Turning OFF at exit. The service is going away, so no AppEnd
                    // handoff is needed — just stop and uninstall it.
                    RunExitServiceTask();
                }
                else if (serviceRunning)
                {
                    // No change. The running service was put to sleep via AppStart
                    // when the app launched; AppEnd tells it the app is exiting so
                    // it can resume operating the COM ports.
                    Exception? lastException = null;
                    for (int i = 0; i < 10; i++)
                    {
                        // Fresh pipe each attempt — a timed-out / half-read pipe is never reused.
                        using var pipe = new NamedPipeClientStream(
                            ".", "Espmon.Service", PipeDirection.InOut, PipeOptions.Asynchronous);
                        try
                        {
                            pipe.Connect(500);
                            var req = new Espmon.Service.ServiceAppStopRequest();
                            var payload = new byte[req.SizeOfStruct];
                            PipeFrame.WriteFrame(pipe, (byte)ServiceCommand.AppEnd, payload);
                            var res = PipeFrame.ReadFrame(pipe);
                            ServiceAppStartResponse.TryRead(res.Payload, out var resp, out var _);
                            Thread.Sleep(100);
                            break;

                        }
                        catch (Exception e) { lastException = e; Thread.Sleep(200); }
                    }
                }

                if (_elevator != null)
                {
                    _elevator.Dispose();
                }
                

            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _isDisposed = true;
        }
    }
    public bool IsServiceRunning
    {
        get
        {
            return File.Exists(@"\\.\pipe\Espmon.Service");
        }
    }
    public bool IsRunning
    {
        get
        {
            if (PortController != null && PortController.IsStarted)
            {
                return true;
            }
            else
            {
                return IsServiceRunning;
            }
        }
        set
        {
            if (IsRunning != value)
            {
                if (!WindowsServiceManager.IsInstalled)
                {
                    if (PortController != null)
                    {
                        if (value)
                        {
                            PortController.Start();
                        }
                        else
                        {
                            PortController.Stop();
                        }
                    }
                }
                else
                {
                    if (!_elevator.IsConnected)
                    {
                        _elevator.Connect();
                        OnPropertyChanged(nameof(IsNotElevated));
                        OnPropertyChanged(nameof(IsServiceRunning));
                    }
                    if (value)
                    {
                        _elevator.StartService();
                    }
                    else
                    {
                        _elevator.StopService();
                    }
                }
                OnPropertyChanged(nameof(IsRunning));
            }
        }

    }
    public bool IsNotElevated
    {
        get
        {
            return !_elevator.IsConnected;
        }
    }
    public bool RequiresElevation
    {
        get
        {
            return IsNotElevated && IsServiceRunning;
        }
    }


    public bool StartWithWindows
    {
        // The effective state is: whatever we intend to leave behind on exit.
        //   InstallAndStart -> will be on  -> true
        //   Remove          -> will be off -> false
        //   None            -> unchanged   -> whatever the service is doing right now
        get => _exitServiceTask switch
        {
            ExitServiceTask.InstallAndStart => true,
            ExitServiceTask.Remove => false,
            _ => File.Exists(@"\\.\pipe\Espmon.Service"),
        };
        set
        {
            try
            {
                // Launch/connect the elevator now, the first time the toggle is
                // flipped, so the UAC prompt happens interactively. The actual
                // install/uninstall is deferred to Dispose via _exitServiceTask.
                if (!_elevator.IsConnected)
                {
                    _elevator.Connect();
                    OnPropertyChanged(nameof(IsNotElevated));
                }

                // Baseline = is the service actually running right now (named pipe).
                // Comparing the desired value against the real current state means
                // toggling back to where you started cancels any pending task,
                // instead of queueing a bogus install-of-installed / remove-of-absent.
                bool running = File.Exists(@"\\.\pipe\Espmon.Service");
                ExitServiceTask newTask =
                    value == running ? ExitServiceTask.None
                    : value ? ExitServiceTask.InstallAndStart
                    : ExitServiceTask.Remove;

                if (newTask != _exitServiceTask)
                {
                    _exitServiceTask = newTask;
                    OnPropertyChanged(nameof(StartWithWindows));
                }
            }
            catch(Exception ex)
            {
                SettingsMessage = $"Could not alter the setting. The process could not be elevated: {ex.Message}";
                OnPropertyChanged(nameof(SettingsMessage));
                OnPropertyChanged(nameof(StartWithWindows));
                return;
            }
            SettingsMessage = StartWithWindows ? "" : "Please reboot to finalize the service removal.";
            OnPropertyChanged(nameof(SettingsMessage));
        }
    }
    public Visibility ConnectVisibility
    {
        get
        {
            return SelectedSession != null && SelectedSession.Status == SessionStatus.Closed?Visibility.Visible:Visibility.Collapsed;
        }
    }
    public void SetFlashError(Exception? ex)
    {
        if (ex == null)
        {
            FlashMessage= "";
        }
        else
        {
            FlashMessage= $"Could not flash device. {ex.Message}";
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FlashMessage)));
    }
    public string FlashMessage { get; private set; }
    public void SetConnectError(Exception? ex)
    {
        if (ex == null)
        {
            ConnectMessage = "";
        }
        else
        {
            ConnectMessage = $"Could not connect. {ex.Message}";
        }
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(ConnectMessage)));
    }
    public string ConnectMessage { get; private set; }
    public string SettingsMessage { get; private set; }
    // Executed on teardown to carry out whatever the StartWithWindows toggle
    // queued up. Best-effort: swallows failures since we're already exiting.
    private void RunExitServiceTask()
    {
        if (_exitServiceTask == ExitServiceTask.None || _elevator == null)
        {
            return;
        }
        try
        {
            if (!_elevator.IsConnected)
            {
                _elevator.Connect();
            }
            if (_exitServiceTask == ExitServiceTask.InstallAndStart)
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Espmon");
                _elevator.InstallService(path);
                _elevator.StartService();
            }
            else if (_exitServiceTask == ExitServiceTask.Remove)
            {
                _elevator.StopService();
                _elevator.UninstallService();
            }
        }
        catch
        {
            // best-effort during teardown
        }
    }


    public void Dispose()
    {
        Dispose(disposing: true);
    }
}