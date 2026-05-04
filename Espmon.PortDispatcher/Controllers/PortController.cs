using HWKit;

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Espmon;

public abstract class PortController : ControllerBase, IDisposable
{
    public ObservableCollection<DeviceController> Devices { get; } = [];
    public ObservableCollection<ScreenController> Screens { get; } = [];
    public ObservableCollection<ProviderController> Providers { get; } = [];

    ObservableCollection<SessionController> _sessions = [];
    public ReadOnlyObservableCollection<SessionController> Sessions { get; }

    public ViewSessionController ViewSession { get; }

    protected PortController(SynchronizationContext? syncContext) : base(syncContext)
    {
        Sessions = new ReadOnlyObservableCollection<SessionController>(_sessions);
        Providers.CollectionChanged += Providers_CollectionChanged;
        Devices.CollectionChanged += Devices_CollectionChanged;
        Screens.CollectionChanged += Screens_CollectionChanged;
        ViewSession = new ViewSessionController(this);
    }
    private Timer? _timer = null;
    private int _timerIteration = 0;
    protected abstract IEnumerable<HardwareInfoEntry> OnEvaluate(HardwareInfoExpression expression);
    public IEnumerable<HardwareInfoEntry> Evaluate(HardwareInfoExpression expression) 
    {
        return OnEvaluate(expression);
    }
    protected abstract string OnGetUnit(HardwareInfoExpression expression);
    public string GetUnit(HardwareInfoExpression expression)
    {
        return OnGetUnit(expression);
    }

    private void Screens_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsDisposed) return;
        Debug.WriteLine($"Collection change: {e.Action}");
        switch(e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if(IsStarted && e.NewItems!=null)
                {
                    foreach(var item in e.NewItems)
                    {
                        if (item is ScreenController scr)
                        {
                            if (!scr.Name.Equals("(default)", StringComparison.Ordinal))
                            {
                                ViewSession.Device?.Screens.Add(scr.Name);
                                OnScreenAdded(scr);
                            }
                        }
                    }
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (IsStarted && e.OldItems != null)
                {
                    foreach (var item in e.OldItems)
                    {
                        if (item is ScreenController scr)
                        {
                            ViewSession.Device?.Screens.Remove(scr.Name);
                            OnScreenRemoved(scr);
                        }
                    }
                }
                break;
            case NotifyCollectionChangedAction.Replace:
                if (IsStarted && e.OldItems!=null && e.NewItems!= null)
                {
                    foreach (var item in e.OldItems)
                    {
                        if (item is ScreenController scr)
                        {
                            if (scr.Name.Equals("(default)", StringComparison.Ordinal))
                            {
                                throw new InvalidOperationException("The default screen cannot be replaced");
                            }
                            ViewSession.Device?.Screens.Remove(scr.Name);
                            OnScreenRemoved(scr);
                        }
                    }
                    foreach (var item in e.NewItems)
                    {
                        if (item is ScreenController scr)
                        {
                            if (scr.Name.Equals("(default)", StringComparison.Ordinal))
                            {
                                throw new InvalidOperationException("The default screen cannot be added");
                            }
                            ViewSession.Device?.Screens.Add(scr.Name);
                            OnScreenAdded(scr);
                        }
                    }
                }
                break;
            case NotifyCollectionChangedAction.Reset:
                if (IsStarted)
                {
                    ViewSession.Device?.Screens.Clear();
                    if (e.NewItems != null)
                    {
                        foreach (var item in e.NewItems)
                        {
                            if (item is ScreenController scr)
                            {
                                ViewSession.Device?.Screens.Add(scr.Name);
                            }
                        }
                    }
                    OnScreensReset();
                }
                break;
        }
    }
    protected virtual void OnScreenAdded(ScreenController screen)
    {
        
    }
    protected virtual void OnScreenRemoved(ScreenController screen)
    {

    }
    protected virtual void OnScreensReset()
    {

    }
    protected abstract DeviceController? OnGetDeviceByMac(byte[] macAddress);
    public DeviceController? GetDeviceByMac(byte[] macAddress)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if(!IsStarted)
        {
            return null;
        }
        return OnGetDeviceByMac(macAddress);
    }
    public ScreenController CreateScreen(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using var stm = Assembly.GetExecutingAssembly()?.GetManifestResourceStream("Espmon.default.screen.json");
        if (stm == null)
        {
            throw new InvalidProgramException("Default screen not found in executable resources");
        }
        JsonObject? obj;
        using (var reader = new StreamReader(stm, Encoding.UTF8))
        {
            obj=(JsonObject?)JsonObject.ReadFrom(reader);
            
        }
        if(obj==null)
        {
            throw new InvalidProgramException("Invalid JSON for default screen in executable resources");
        }
        return ScreenController.FromJson(this, name, obj);
    }
    private void Devices_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsDisposed) return;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (IsStarted && e.NewItems != null)
                {
                    foreach (var item in e.NewItems)
                    {
                        if (item is DeviceController dev)
                        {
                            OnDeviceAdded(dev);
                        }
                    }
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (IsStarted && e.OldItems != null)
                {
                    foreach (var item in e.OldItems)
                    {
                        if (item is DeviceController dev)
                        {
                            OnDeviceRemoved(dev);
                        }
                    }
                }
                break;
            case NotifyCollectionChangedAction.Replace:
                if (IsStarted && e.OldItems != null && e.NewItems != null)
                {
                    foreach (var item in e.OldItems)
                    {
                        if (item is DeviceController dev)
                        {
                            OnDeviceRemoved(dev);
                        }
                    }
                    foreach (var item in e.NewItems)
                    {
                        if (item is DeviceController dev)
                        {
                            OnDeviceAdded(dev);
                        }
                    }
                }
                break;
            case NotifyCollectionChangedAction.Reset:
                if (IsStarted)
                {
                    OnDevicesReset();
                }
                break;
        }
    }
    protected virtual void OnDeviceAdded(DeviceController device)
    {

    }
    protected virtual void OnDeviceRemoved(DeviceController device)
    {

    }
    protected virtual void OnDevicesReset()
    {

    }
    private void Providers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsDisposed) return;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (IsStarted && e.NewItems != null)
                {
                    foreach (var item in e.NewItems)
                    {
                        if (item is ProviderController prov)
                        {
                            try
                            {
                                prov.Start();
                            }
                            catch { }
                            OnProviderAdded(prov);
                        }
                    }
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (IsStarted && e.OldItems != null)
                {
                    foreach (var item in e.OldItems)
                    {
                        if (item is ProviderController prov)
                        {
                            try
                            {
                                prov.Stop();
                            }
                            catch { }
                            OnProviderRemoved(prov);
                        }
                    }
                }
                break;
            case NotifyCollectionChangedAction.Replace:
                if (IsStarted && e.OldItems != null && e.NewItems != null)
                {
                    foreach (var item in e.OldItems)
                    {
                        if (item is ProviderController prov)
                        {
                            OnProviderRemoved(prov);
                        }
                    }
                    foreach (var item in e.NewItems)
                    {
                        if (item is ProviderController prov)
                        {
                            try
                            {
                                prov.Start();
                            }
                            catch { }
                            OnProviderAdded(prov);
                        }
                    }
                }
                break;
            case NotifyCollectionChangedAction.Reset:
                if (IsStarted)
                {
                    if (e.OldItems != null) 
                    {
                        foreach (var item in e.OldItems)
                        {
                            if (item is ProviderController prov)
                            {
                                try
                                {
                                    prov.Stop();
                                }
                                catch { }
                            }
                        }
                    }
                    OnProvidersReset();
                }
                break;
        }
    }
    protected virtual void OnProviderAdded(ProviderController provider)
    {
        
    }
    protected virtual void OnProviderRemoved(ProviderController provider)
    {
    }
    protected virtual void OnProvidersReset()
    {

    }
    protected abstract ProviderController[] CreateProviders();
    protected abstract DeviceController[] CreateDevices();
    protected abstract ScreenController[] CreateScreens();
    protected abstract SessionController[] CreateSessions();
    public bool IsStarted { get; private set; } = false;
    protected bool IsDisposed { get; private set; } = false;

    protected virtual void OnDispose()
    {
    }
    protected abstract void OnStart();
    private SessionController? FindSessionBySerial(IEnumerable<SessionController> sessions, string serialNumber)
    {
        foreach(var session in sessions)
        {
            if(session.SerialNumber.Equals(serialNumber,StringComparison.Ordinal))
            {
                return session;
            }
        }
        return null;
    }
    public void RefreshSessions()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!IsStarted)
        {
            return;
        }
        var sessions = CreateSessions();
        var toAdd = new List<SessionController>();
        var toReplace = new Dictionary<int, SessionController>();
        for(var i = 0;i<sessions.Length;++i)
        {
           var newSession = sessions[i];
           var oldSession = FindSessionBySerial(_sessions,newSession.SerialNumber);
            if(oldSession==null)
            {
                toAdd.Add(newSession);
            }
        }
        var toRemove = new List<SessionController>();
        foreach(var oldSession in _sessions)
        {
            var newSession = FindSessionBySerial(sessions, oldSession.SerialNumber);
            if(newSession==null)
            {
                toRemove.Add(oldSession);
            }
        }
        for(var i = 0;i<toRemove.Count;++i)
        {
            _sessions.Remove(toRemove[i]);
        }
        toRemove.Clear();
        for (var i = 0; i < toAdd.Count; ++i)
        {
            _sessions.Add(toAdd[i]);
        }
        toAdd.Clear();
    }
    private SessionController[] GatherForInterval(int interval)
    {
        var result = new List<SessionController>(_sessions.Count+1);
        for (var i = 0; i < _sessions.Count; ++i)
        {
            var session = _sessions[i];
            bool added = false;
            if (session.Device != null)
            {
                if (session.ScreenIndex > -1)
                {
                    var scr = session.Screen;
                    if (scr != null)
                    {
                        if(((int)scr.Interval)==interval)
                        {
                            result.Add(session);
                            added = true;
                        }
                    } 
                } 
            }
            if (!added)
            {
                result.Add(session);
            }
        }
        if(ViewSession.Device!=null && ViewSession.Screen!=null)
        {
            if(((int)ViewSession.Screen.Interval)==interval)
            {
                result.Add(ViewSession);
            }
        }
        return result.ToArray();
    }
    private static void RefreshSessions(SessionController[] sessions)
    {
        for (var i = 0; i < sessions.Length; ++i)
        {
            sessions[i].Refresh();
        }
    }
    protected virtual void OnRefresh()
    {

    }
    private void TryConnectSessions()
    {
        for (var i = 0; i < _sessions.Count; ++i)
        {
            var session = _sessions[i];
            if(session.Device!=null && session.Device.MacAddress!=null&& session.Device.MacAddress[0] != 0)
            {
                if (session.Status == SessionStatus.Closed)
                {
                    try
                    {
                        session.Connect();
                    }
                    catch { }
                }
            }
        }
    }
    private static void _timer_Tick(object? state)
    {
        if(state is PortController cntrl)
        {
            cntrl.Post(() =>
            {
                int iter = cntrl._timerIteration++;
                RefreshSessions(cntrl.GatherForInterval(0));
                RefreshSessions(cntrl.GatherForInterval(100));
                switch (iter)
                {
                    case 0:
                        RefreshSessions(cntrl.GatherForInterval(500));
                        RefreshSessions(cntrl.GatherForInterval(1000));
                        break;
                    case 4:
                        RefreshSessions(cntrl.GatherForInterval(500));
                        break;
                    case 9:
                        cntrl.OnRefresh();
                        cntrl.TryConnectSessions();
                        cntrl._timerIteration = 0;
                        break;
                }
            });
        }
    }
    public void Start()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!IsStarted)
        {
            _sessions.Clear();
            Devices.Clear();
            Screens.Clear();
            if(Providers.Count==0)
            {
                var providers = CreateProviders();
                for (var i = 0; i < providers.Length; ++i)
                {
                    Providers.Add(providers[i]);
                }
            }
            var screens = CreateScreens();
            for (var i = 0; i < screens.Length; ++i)
            {
                Screens.Add(screens[i]);
                ViewSession.Device?.Screens.Add(screens[i].Name);
            }
            var devices = CreateDevices();
            for (var i = 0;i<devices.Length;++i)
            {
                Devices.Add(devices[i]);
            }
            var sessions = CreateSessions();
            for (var i = 0; i < sessions.Length; ++i)
            {
                _sessions.Add(sessions[i]);
            }
            OnStart();
            UpdateProperty(nameof(IsStarted), () =>
            {
                IsStarted = true;
                ViewSession.Connect();
                _timerIteration = 0;
                _timer = new Timer(_timer_Tick, this, 100, 100);

            });
            
        }
    }
    protected abstract void OnStop();
    public void Stop()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (IsStarted)
        {
            ViewSession.Disconnect();
            if (_timer!=null)
            {
                _timer.Dispose();
                _timer = null;
            }
            OnStop();
            UpdateProperty(nameof(IsStarted), () => {
                IsStarted = false;
                foreach (var provider in Providers)
                {
                    try
                    {
                        provider.Stop();
                    }
                    catch { }
                }
                _sessions.Clear();
                Devices.Clear();
                Screens.Clear();

            });
        }
    }
    
    ~PortController()
    {
        OnDispose();
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            Stop();
            OnDispose();
            IsDisposed = true;
        }
        GC.SuppressFinalize(this);
    }

}
