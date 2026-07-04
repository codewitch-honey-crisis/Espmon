using HWKit;

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
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

public sealed class SessionStatusChangedEventArgs : EventArgs
{
    public SessionController Session { get; }
    public SessionStatusChangedEventArgs(SessionController session) { Session=session; }
}
public delegate void SessionStatusChangedEventHandler(object sender, SessionStatusChangedEventArgs args);
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
    public event SessionStatusChangedEventHandler? SessionStatusChanged;
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
        //Debug.WriteLine($"Collection change: {e.Action}");
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
 
    private sealed class SessionComparer : IComparer<SessionController>
    {
        public int Compare(SessionController? x, SessionController? y)
        {
            if(x==null)
            {
                if(y==null)
                {
                    return 0;
                }
                return 1;
            } else if(y==null)
            {
                return -1;
            }
            if(x.Device==null)
            { 
                if(y.Device!=null)
                {
                    return 1;
                } 
                return string.Compare(x.PortName,y.PortName, StringComparison.Ordinal);
                
            }
            if (y.Device == null)
            {
                return -1;
            }
            return string.Compare(x.Device.Name, y.Device.Name, StringComparison.Ordinal);
        }
        public static readonly SessionComparer Default = new SessionComparer();
    }

    public void RefreshSessions()
    {
        if (IsDisposed || !IsStarted) return;

        var sessions = CreateSessions();
        var newSerials = new HashSet<string>(sessions.Select(s => s.SerialNumber));
        var oldSerials = new HashSet<string>(_sessions.Select(s => s.SerialNumber));

        // Remove sessions that are gone
        for (var i = _sessions.Count - 1; i >= 0; --i)
        {
            if (!newSerials.Contains(_sessions[i].SerialNumber))
            {
                _sessions[i].PropertyChanged -= Session_PropertyChanged;
               // UpdateSession(_sessions[i]);
                _sessions.RemoveAt(i);
            }
        }
        // Add sessions that are new
        foreach (var session in sessions)
        {
            if (!oldSerials.Contains(session.SerialNumber))
            {
                session.PropertyChanged += Session_PropertyChanged;
                _sessions.Add(session);
                UpdateSession(session);
            }
        }
    }

    private void Session_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if(sender is SessionController session && (e.PropertyName==null || 0==string.CompareOrdinal(e.PropertyName,"Status" )))
        {
            UpdateSession(session);
        }
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
            if (session.Device != null && session.Device.MacAddress != null && session.Device.MacAddress[0] != 0)
            {
                if (session.Status == SessionStatus.Closed || session.Status == SessionStatus.RequiresFlash)
                {
                    try
                    {
                        session.Connect();
                    }
                    catch (Exception)
                    {
                        //Console.Error.WriteLine($"Error trying to connect {session.PortName}: {ex.Message}");
                    }
                }
            }
        }
        
    }
    private static void _timer_Tick(object? state)
    {
        if(state is PortController cntrl)
        {
            if (cntrl.IsDisposed) return;
            cntrl.Post(() =>
            {
                int iter = cntrl._timerIteration++;
                RefreshSessions(cntrl.GatherForInterval(0)); // both are 10Hz
                RefreshSessions(cntrl.GatherForInterval(100));
                if (iter % 2 == 0)
                    RefreshSessions(cntrl.GatherForInterval(200));   // 5 Hz

                switch (iter % 10)
                {
                    case 0:
                        RefreshSessions(cntrl.GatherForInterval(1000)); // 1 Hz
                        break;
                    case 9: // every second try to refresh and connect
                        cntrl.OnRefresh();
                        cntrl.TryConnectSessions();
                        break;
                }
                if (iter == 49) // every 5 seconds try to enum new devices
                {
                    cntrl._timerIteration = 0;
                    cntrl.RefreshSessions();
                }
                //RefreshSessions(cntrl.GatherForInterval(0));
                //RefreshSessions(cntrl.GatherForInterval(100));
                //switch (iter % 10)
                //{
                //    case 0:
                //        RefreshSessions(cntrl.GatherForInterval(500)); // wrong
                //        RefreshSessions(cntrl.GatherForInterval(1000));
                //        break;
                //    case 4:
                //        RefreshSessions(cntrl.GatherForInterval(500)); // wrong
                //        break;
                //    case 9:
                //        cntrl.OnRefresh();
                //        cntrl.TryConnectSessions();
                //        break;
                //}
                //if (iter == 49)
                //{
                //    cntrl._timerIteration = 0;
                //    cntrl.RefreshSessions();
                //}

            });
        }
    }
    
    protected void UpdateSession(SessionController session)
    {
        SessionStatusChanged?.Invoke(this, new SessionStatusChangedEventArgs(session));
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
            Array.Sort(sessions, SessionComparer.Default);
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
                
            });
            foreach (var session in _sessions)
            {
                try
                {
                    session.Disconnect();
                }
                catch { }
            }

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
