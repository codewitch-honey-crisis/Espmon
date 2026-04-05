using HWKit;

using Microsoft.UI.Xaml;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Espmon;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private Elevator _elevator;
    private ScreenWatcher _watcher;
    public event PropertyChangedEventHandler? PropertyChanged;
    private PortDispatcher _portDispatcher;
    private string _selectedPath = string.Empty;
    public FirmwareEntry[] FirmwareEntries { get; private set; }
    public ObservableCollection<string> Log { get; } = [];
    public ObservableCollection<ScreenListEntry> ScreenItems { get; } = [];
    private Session? _selectedSession;
    public Session? SelectedSession
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
    }

    public Visibility SessionFlashVisibility
    {
        get
        {
            if(SelectedSession!=null)
            {
                return SelectedSession.Status==SessionStatus.RequiresFlash||SelectedSession.Status==SessionStatus.Flashing ?Visibility.Visible:Visibility.Collapsed; 
            }
            return Visibility.Collapsed;
        }
    }
    public Visibility SessionOpenVisibility
    {
        get
        {
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
            if (SelectedSession != null)
            {

                return SelectedSession.Device!=null && SelectedSession.Status==SessionStatus.Busy || SelectedSession.Status== SessionStatus.ReadyForData || SelectedSession.Status==SessionStatus.NeedScreen?Visibility.Visible:Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }
    }
    public ObservableCollection<string> ValidationLog { get; } = new();
    public void Refresh()
    {
        
    }
    public void Load()
    {
        RefreshStartWithWindowsAsync();
        if (!_startWithWindows)
        {
            _portDispatcher.Start();
        }
    }
    public void RefreshDevices()
    {
        if(_portDispatcher!=null)
        {
            _portDispatcher.RefreshAllSessions();
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
                        SelectedSession = entry.Session;
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
        if(!File.Exists(Path.Join(path,"(default).screen.json")))
        {
            using var writer = new StreamWriter(Path.Combine(path, "(default).screen.json"), false, Encoding.UTF8);
            scr.WriteTo(writer);
            writer.Close();
        }
        FirmwareEntries = PortDispatcher.GetFirmwareEntries();
        _watcher = new ScreenWatcher(path, SynchronizationContext.Current);
        foreach (var screen in _watcher.Screens)
        {
            ScreenItems.Add(new ScreenListEntry(_watcher.GetName(screen) ?? "(null)", screen));
        }
        _watcher.Screens.CollectionChanged += Screens_CollectionChanged;
        _portDispatcher = new PortDispatcher(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Espmon"), SynchronizationContext.Current);
        var hwInfo = _portDispatcher.HardwareInfo;
        hwInfo.Providers.Add(new CoreTempCpuProvider());
        hwInfo.Providers.Add(new CimCpuProvider());
        hwInfo.Providers.Add(new CimRamProvider());
        hwInfo.Providers.Add(new CimDiskProvider());
        hwInfo.Providers.Add(new AmdAdlGpuProvider());
        hwInfo.Providers.Add(new NvidiaNvmlGpuProvider());
        hwInfo.Providers.Add(new DxgiProvider());
        Load();



    }
    public ObservableCollection<Session> Sessions => _portDispatcher.Sessions;
    public void AddScreen(string name, Screen screen)
    {
        ArgumentNullException.ThrowIfNull(name, nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        if (name == "(default)") throw new ArgumentException("The (default) screen cannot be overwritten", nameof(name));
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Espmon");
        if (Directory.Exists(path))
        {
            using var writer = new StreamWriter(Path.Combine(path, $"{name}.screen.json"), false, Encoding.UTF8);
            screen.WriteTo(writer);
            writer.Close();
        }
    }
    public void SaveScreen(Screen screen)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Espmon");
        if (Directory.Exists(path))
        {
            var name = _watcher.GetName(screen);
            if (name != null)
            {
                using var writer = new StreamWriter(Path.Combine(path, $"{name}.screen.json"), false, Encoding.UTF8);
                screen.WriteTo(writer);
                writer.Close();
            }
        }
    }
    public void DeleteScreen(Screen screen)
    {
        var name = _watcher.GetName(screen);
        if (name == null) return;
        if (name == "(default)") throw new ArgumentException("The (default) screen cannot be deleted", nameof(screen));
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Espmon");
        File.Delete(Path.Combine(path, $"{name}.screen.json"));
    }
    public void RenameScreen(Screen screen, string newName)
    {
        if (newName == "(default)") throw new ArgumentException("The (default) screen cannot be renamed", nameof(newName));
        DeleteScreen(screen);
        AddScreen(newName, screen);
    }
    public void RenameDevice(Session session, string newName)
    {
        session.Name = newName;
    }
    private void Screens_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewStartingIndex != -1 && e.NewItems != null)
                {
                    var i = 0;
                    foreach (Screen item in e.NewItems)
                    {
                        ScreenItems.Insert(e.NewStartingIndex + i, new ScreenListEntry(_watcher.GetName(item) ?? "(null)", item));
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

    public HardwareInfoCollection HardwareInfo =>   _portDispatcher.HardwareInfo;

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
    private Screen? _selectedScreen = null;
    private bool _isDisposed;

    public Screen? SelectedScreen
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
                if(_portDispatcher!=null)
                { 
                    _portDispatcher.Stop();
                    _portDispatcher.Dispose();
                }
                if(HardwareInfo!=null)
                {
                    HardwareInfo.Dispose();
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
            if (_portDispatcher != null && _portDispatcher.IsStarted)
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
                    if (_portDispatcher != null)
                    {
                        if (value)
                        {
                            _portDispatcher.Start();
                        }
                        else
                        {
                            _portDispatcher.Stop();
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
    public string SelectedUpdateInterval
    {
        get
        {
            switch(_portDispatcher.Interval.TotalMilliseconds)
            {
                case 1000: return "1 Hz";
                case 200: return "5 Hz";
                default: return "10 Hz";
            }
        }
        set
        {
            switch(value.ToLowerInvariant()) {
                case "1 hz":
                    _portDispatcher.Interval = TimeSpan.FromMilliseconds(1000);
                    break;
                case "5 hz":
                    _portDispatcher.Interval = TimeSpan.FromMilliseconds(200);
                    break;
                default:
                    _portDispatcher.Interval = TimeSpan.FromMilliseconds(100);
                    break;
            }
            //OnPropertyChanged(nameof(SelectedUpdateInterval));
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