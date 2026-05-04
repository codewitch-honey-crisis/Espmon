using Microsoft.UI.Xaml;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Espmon;

[SupportedOSPlatform("windows")]
public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private Elevator _elevator;
    private int _devicePanelIndex;
    public event PropertyChangedEventHandler? PropertyChanged;
    public PortController PortController { get; }
    private string _selectedPath = string.Empty;

    public ObservableCollection<string> Log { get; } = [];
    public ObservableCollection<ScreenListEntry> ScreenItems { get; } = [];
    private SessionController? _selectedSession;

    public Visibility DevicePanel1Visibility
    {
        get => _devicePanelIndex == 0 ? Visibility.Visible:Visibility.Collapsed;
    }
    public Visibility DevicePanel2Visibility
    {
        get => _devicePanelIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }
    public Visibility DevicePanel3Visibility
    {
        get => _devicePanelIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }
    public int DevicePanelIndex { get=> _devicePanelIndex; 
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value,0);
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
            if (_selectedSession!=value)
            {
                if (_selectedSession!= null)
                {
                    _selectedSession.PropertyChanged -= _selectedSession_PropertyChanged;
                }
                _selectedSession = value;
                if (_selectedSession != null)
                {
                    _selectedSession.PropertyChanged += _selectedSession_PropertyChanged;
                }
                OnPropertyChanged(nameof(SelectedSession));
                OnPropertyChanged(nameof(SelectedSessionScreenMetrics));
                OnPropertyChanged(nameof(SessionOpenVisibility));
                OnPropertyChanged(nameof(SessionFlashVisibility));
                OnPropertyChanged(nameof(SessionRunningVisibility));
                OnPropertyChanged(nameof(SessionScreenListVisibility));
            }
        }
    }
    private bool _flashRequested;
    public bool FlashRequested
    {
        get => _flashRequested;
        set
        {
            if(_flashRequested!=value)
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
            if (SelectedSession==null)
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
            if(SelectedSession!=null)
            {
                return FlashRequested || SelectedSession.GetUpgrade()!=FirmwareUpgrade.NotRequired||SelectedSession.Status==SessionStatus.Flashing ?Visibility.Visible:Visibility.Collapsed; 
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
                return !SelectedSession.IsWaitingForScreenChange && SelectedSession.Device != null && SelectedSession.Status == SessionStatus.Busy || SelectedSession.Status == SessionStatus.ReadyForData? Visibility.Visible:Visibility.Collapsed;
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
                return SelectedSession.Status==SessionStatus.Closed? Visibility.Visible : Visibility.Collapsed;
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

                return SelectedSession.Device!=null && SelectedSession.Status==SessionStatus.Busy || SelectedSession.Status== SessionStatus.ReadyForData || SelectedSession.Status==SessionStatus.NeedScreen?Visibility.Visible:Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }
    }
    public ObservableCollection<string> ValidationLog { get; } = new();
    
    public void Load()
    {
        RefreshStartWithWindowsAsync();
        if (!_startWithWindows)
        {
            PortController.Start();
        }
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
    IList<ProviderEntry>? _providerEntries= null;
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
            return _selectedProviderEntry!=null?Visibility.Visible:Visibility.Collapsed;
        }
    }
    public void RefreshProviders()
    {
        ProviderEntries = new List<ProviderEntry>(GetProviderEntries());
    }
    public void RefreshDevices()
    {
        if(PortController!=null)
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
            result[i]=new SessionEntry(Sessions[i]);
        }
        return result;
    }
    IList<SessionEntry>? _sessionEntries = null;
    public IList<SessionEntry> SessionEntries
    {
        get
        {
            if(_sessionEntries==null || _sessionEntries.Count==0)
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
                if (object.ReferenceEquals(entry.Session, ses)) {
                    return SessionEntries[i];
                }
            }
            return null;
        }
        set
        {
            var ses = value?.Session;
            if (ses != null) {
                for (var i = 0; i < SessionEntries.Count; i++)
                {
                    var entry = SessionEntries[i];
                    if (object.ReferenceEquals(entry.Session, ses)) {
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
        _elevator = new Elevator();
        var scr = Screen.Default;
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Espmon");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        
 
        PortController = new LocalPortController(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Espmon"), SynchronizationContext.Current);
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
        foreach(var scr in PortController.Screens)
        {
            if(scr.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
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
        while(HasScreen(newName))
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
            if(value <0)
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
            if(changed)
            {
              
               PortController.ViewSession.ScreenIndex = value;
             
                OnPropertyChanged(nameof(SelectedScreen));
                OnPropertyChanged(nameof(SelectedScreenIndex));   
            }
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
                if(_elevator!=null)
                {
                    _elevator.Dispose();
                }
                if(PortController!=null)
                { 
                    PortController.Stop();
                    PortController.Dispose();
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
            try
            {
                using var client = new NamedPipeClientStream(".", "Espmon.Service", PipeDirection.InOut);
                client.Connect(0); // 0ms timeout = non-blocking
                return true; 
            }
            catch (TimeoutException)
            {
                return false; // pipe not found 
            }
            catch (Exception)
            {
                return false; // treat any error as not running
            }
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
    private bool _startWithWindows;
    private static async Task<bool> IsServiceRunningAsync()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", "Espmon.Service", PipeDirection.In, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(200);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void RefreshStartWithWindowsAsync()
    {
        _startWithWindows = WindowsServiceManager.IsInstalled;
        OnPropertyChanged(nameof(StartWithWindows));
    }
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => _ = SetStartWithWindowsAsync(value);
    }

    private async Task SetStartWithWindowsAsync(bool value)
    {
        if (!_elevator.IsConnected)
        {
            await _elevator.ConnectAsync();
            OnPropertyChanged(nameof(IsNotElevated));
            OnPropertyChanged(nameof(IsServiceRunning));
        }

        var isInstalled = _elevator.IsInstalled;
        if (value != isInstalled)
        {
            if (!value)
                await _elevator.UninstallServiceAsync();
            else
            {
                await _elevator.InstallServiceAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Espmon"));
                await _elevator.StartServiceAsync();
            }
            RefreshStartWithWindowsAsync();
        }
    }
    
    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~MainViewModel()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}