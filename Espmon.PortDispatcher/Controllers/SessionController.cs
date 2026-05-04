using System.ComponentModel;
using System.Diagnostics;

namespace Espmon;

public enum FirmwareUpgrade
{
    Required=0,
    Suggested=1,
    NotRequired=2
}
public abstract class SessionController : ControllerBase, INotifyPropertyChanged
{
    public event ScreenChangedEventHandler? ScreenChanged;
    public event ScreenClearedEventHandler? ScreenCleared;
    public event ScreenDataEventHandler? ScreenData;
    private DeviceController? _device;
    public bool IsWaitingForScreenChange { get; protected set; }
    public DeviceController? Device
    {
        get
        {
            return _device;
        }
        set
        {
            if (value != _device)
            {
                _device = value;
                UpdateProperty(nameof(Device), () => _device = value);
                OnDeviceChanged();
            }
        }
    }
    protected virtual void OnDeviceChanged()
    {

    }
    protected abstract void OnRefresh();
    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if(Parent.IsStarted)
        {
            OnRefresh();
        }
    }
    
    private int _screenIndex = -1;
    public int ScreenIndex
    {
        get
        {
            return _screenIndex;
        }
        set
        {
            if (value<0 && _screenIndex>-1)
            {
                //if (IsWaitingForScreenChange)
                //{
                //    IsWaitingForScreenChange = false;
                //    OnPropertyChanged(nameof(IsWaitingForScreenChange));
                //}
                //UpdateProperties(() => { _screenIndex = -1; _screen = null; }, nameof(ScreenIndex), nameof(Screen));
                //OnScreenIndexChanged();
                //OnScreenChanged();
                return;
            }
            if (value != _screenIndex && value>-1)
            {
                if (_device != null && _device.Screens.Count>0)
                {
                    if (_status == SessionStatus.NeedScreen || _status == SessionStatus.Busy) {
                        
                        var si = value % _device.Screens.Count;
                        var name = _device.Screens[si];
                        var found = false;
                        ScreenController? scr = null;
                        for(var i = 0;i<Parent.Screens.Count;++i)
                        {
                            scr = Parent.Screens[i];
                            if(scr.Name.Equals(name,StringComparison.OrdinalIgnoreCase))
                            {
                                found = true;
                                break;
                            }
                        }
                        if(!found)
                        {
                            throw new InvalidOperationException("The device has a screen named that doesn't exist");
                        }
                        UpdateProperties(() => { _screenIndex = si; _screen = scr; }, nameof(ScreenIndex), nameof(Screen));
                        //if (!IsWaitingForScreenChange)
                        //{
                        //    IsWaitingForScreenChange = true;
                        //    OnPropertyChanged(nameof(IsWaitingForScreenChange));
                        //}
                        OnScreenIndexChanged();
                        OnScreenChanged();
                        return;
                    }
                }
                throw new InvalidOperationException("The session is not in a valid state to switch screens");
            }
            
        }
    }
    protected virtual void OnScreenIndexChanged()
    {

    }
    private ScreenController? _screen;
    public ScreenController? Screen
    {
        get
        {
            return _screen;
        }
        set
        {
            if(value==null && _screen!=null)
            {
                //if (IsWaitingForScreenChange)
                //{
                //    IsWaitingForScreenChange = false;
                //    OnPropertyChanged(nameof(IsWaitingForScreenChange));
                //}

                //UpdateProperties(() => { _screenIndex = -1; _screen = null; }, nameof(ScreenIndex), nameof(Screen));
                //OnScreenIndexChanged();
                //OnScreenChanged();
                return;
            }
            if (!object.ReferenceEquals(_screen, value))
            {
                if (_device != null && _device.Screens.Count>0)
                {
                    if (_status == SessionStatus.NeedScreen || _status == SessionStatus.Busy)
                    {
                        for (var i = 0; i < _device.Screens.Count; ++i)
                        {
                            var name = _device.Screens[i];
                            for (var j = 0; j < Parent.Screens.Count; ++j)
                            {
                                var scr = Parent.Screens[j];
                                if (scr.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (_screenIndex != i)
                                    {
                                        UpdateProperties(() => { _screenIndex = i; _screen = value; }, nameof(ScreenIndex), nameof(Screen));
                                        //if (!IsWaitingForScreenChange)
                                        //{
                                        //    IsWaitingForScreenChange = true;
                                        //    OnPropertyChanged(nameof(IsWaitingForScreenChange));
                                        //}
                                        OnScreenIndexChanged();
                                        OnScreenChanged();
                                    }
                                    return;
                                }
                            }
                        }
                        throw new ArgumentException("The screen is not part of the device screen list");
                    }
                }
                throw new InvalidOperationException("The session is not in a valid state to switch screens");
            }
        }
    }
    protected virtual void OnScreenChanged()
    {
  
    }
    private SessionStatus _status = SessionStatus.Closed;
    public SessionStatus Status
    {
        get
        {
            return _status;
        }
        protected set
        {
            if (value != _status)
            {
                UpdateProperty(nameof(Status), () => _status = value);
                OnStatusChanged();
            }
        }
    }
    protected virtual void OnStatusChanged()
    {

    }

    private int _id = -1;
    public int Id
    {
        get
        {
            return _id;
        }
        protected set
        {
            if (value != _id)
            {
                UpdateProperty(nameof(Id), () => _id = value);
                OnIdChanged();
            }
        }
    }
    protected virtual void OnIdChanged()
    {

    }
    private int _horizontalResolution = 0;
    public int HorizontalResolution
    {
        get
        {
            return _horizontalResolution;
        }
        protected set
        {
            if (value != _horizontalResolution)
            {
                UpdateProperty(nameof(HorizontalResolution), () => _horizontalResolution= value);
                OnHorizontalResolutionChanged();
            }
        }
    }
    protected virtual void OnHorizontalResolutionChanged()
    {

    }
    private int _verticalResolution = 0;
    public int VerticalResolution
    {
        get
        {
            return _verticalResolution;
        }
        protected set
        {
            if (value != _verticalResolution)
            {
                UpdateProperty(nameof(VerticalResolution), () => _verticalResolution = value);
                OnVerticalResolutionChanged();
                OnPropertyChanged(nameof(VerticalResolution));
            }
        }
    }
    protected virtual void OnVerticalResolutionChanged()
    {

    }
    private string _deviceName = string.Empty;
    public string DeviceName
    {
        get
        {
            return _deviceName;
        }
        set
        {
            if (value != _deviceName)
            {
                UpdateProperty(nameof(DeviceName), () => _deviceName= value);
                OnDeviceNameChanged();
            }
        }
    }
    protected virtual void OnDeviceNameChanged()
    {

    }
    private string _slug = string.Empty;
    public string Slug
    {
        get
        {
            return _slug;
        }
        protected set
        {
            if (value != _slug)
            {
                UpdateProperty(_slug, () => _slug = value);
                OnSlugChanged();
            }
        }
    }
    protected virtual void OnSlugChanged()
    {

    }
    private float _dpi = 0f;
    public float Dpi
    {
        get
        {
            return _dpi;
        }
        protected set
        {
            if (value != _dpi)
            {
                UpdateProperty(nameof(Dpi), () => _dpi = value);
                OnDpiChanged();
            }
        }
    }
    protected virtual void OnDpiChanged()
    {

    }
    private float _pixelSize = 0f;
    public float PixelSize
    {
        get
        {
            return _pixelSize;
        }
        protected set
        {
            if (value != _pixelSize)
            {   
                UpdateProperty(nameof(PixelSize), () => _pixelSize = value);
                OnPixelSizeChanged();
            }
        }
    }
    protected virtual void OnPixelSizeChanged()
    {

    }
    private DeviceInputType _input = DeviceInputType.None;
    public DeviceInputType Input
    {
        get
        {
            return _input;
        }
        protected set
        {
            if (value != _input)
            {
                UpdateProperty(nameof(Input), () => _input = value);
                OnInputChanged();
            }
        }
    }
    protected virtual void OnInputChanged()
    {

    }
    private bool _isMonochrome = false;
    public bool IsMonochrome
    {
        get
        {
            return _isMonochrome;
        }
        protected set
        {
            if (value != _isMonochrome)
            {
                UpdateProperty(nameof(IsMonochrome), () => _isMonochrome = value);
                OnIsMonochromeChanged();
            }
        }
    }
    protected virtual void OnIsMonochromeChanged()
    {

    }
    private int _versionMajor = 0;
    public int VersionMajor
    {
        get
        {
            return _versionMajor;
        }
        protected set
        {
            if (value != _versionMajor)
            {
                UpdateProperty(nameof(VersionMajor), () => _versionMajor = value);
                OnVersionMajorChanged();
            }
        }
    }
    protected virtual void OnVersionMajorChanged()
    {

    }
    private int _versionMinor = 0;
    public int VersionMinor
    {
        get
        {
            return _versionMinor;
        }
        protected set
        {
            if (value != _versionMinor)
            {
                UpdateProperty(nameof(VersionMinor), () => _versionMinor = value);
                OnVersionMinorChanged();

            }
        }
    }
    protected virtual void OnVersionMinorChanged()
    {

    }
    private ulong _build = 0;
    public ulong Build
    {
        get
        {
            return _build;
        }
        protected set
        {
            if (value != _build)
            {
                UpdateProperty(nameof(Build), () => _build= value);
                OnBuildChanged();
            }
        }
    }
    protected virtual void OnBuildChanged()
    {

    }

    public FirmwareUpgrade GetUpgrade() 
    {
        if(_versionMajor!=(int)FirmwareBuild.VersionMajor)
        {
            return FirmwareUpgrade.Required;
        }
        if(_versionMinor<(int)FirmwareBuild.VersionMinor || _build!=FirmwareBuild.Timestamp)
        {
            return FirmwareUpgrade.Suggested;
        }
        return FirmwareUpgrade.NotRequired;
        
    }
    
    public PortController Parent { get; }
    public string PortName { get; }
    public string SerialNumber { get; }
    protected SessionController(PortController parent, string portName, string serialNumber) : base(parent)
    {
        ArgumentNullException.ThrowIfNull(parent, nameof(parent));
        ArgumentException.ThrowIfNullOrWhiteSpace(portName, nameof(portName));
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber, nameof(serialNumber));
        Parent = parent;
        PortName = portName;
        SerialNumber = serialNumber;

    }
    protected bool IsDisposed { get; private set; } = false;
    protected abstract void OnConnect();

    protected abstract void OnDisconnect();

    public void Disconnect()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!Parent.IsStarted)
        {
            throw new InvalidOperationException("The port controller has not been started.");
        }
        if (Status != SessionStatus.Closed)
        {
            if (IsWaitingForScreenChange)
            {
                IsWaitingForScreenChange = false;
                OnPropertyChanged(nameof(IsWaitingForScreenChange));
            }
            OnDisconnect();
        }
    }
    public void Connect()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if(!Parent.IsStarted)
        {
            throw new InvalidOperationException("The port controller has not been started.");
        }
        if (Status == SessionStatus.Closed)
        {
            if (IsWaitingForScreenChange)
            {
                IsWaitingForScreenChange = false;
                OnPropertyChanged(nameof(IsWaitingForScreenChange));
            }
            OnConnect();
        } else if(Status !=SessionStatus.Busy && Status!=SessionStatus.NeedScreen && Status!=SessionStatus.Negotiating && Status!=SessionStatus.Connecting && Status != SessionStatus.ReadyForData)
        {
            throw new InvalidOperationException("The session controller is in an invalid state.");
        }
    }
    protected virtual void OnDispose()
    {
    }
    ~SessionController()
    {
        OnDispose();
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            OnDispose();
            IsDisposed = true;
        }
        GC.SuppressFinalize(this);
    }
    protected virtual void OnScreenChanged(ScreenChangedEventArgs args)
    {
        ScreenChanged?.Invoke(this, args);
        if (IsWaitingForScreenChange)
        {
            IsWaitingForScreenChange = true;
            OnPropertyChanged(nameof(IsWaitingForScreenChange));
        }
        
        
    }
    protected virtual void OnScreenCleared(EventArgs args)
    {
        ScreenCleared?.Invoke(this, args);
    }
    protected virtual void OnScreenData(ScreenDataEventArgs args)
    {
        if (IsWaitingForScreenChange)
        {
            IsWaitingForScreenChange = false;
            OnPropertyChanged(nameof(IsWaitingForScreenChange));
        }
        ScreenData?.Invoke(this, args);
    }
    protected abstract Task OnFlashAsync(FirmwareEntry firmwareEntry, IFlashProgress? progress, CancellationToken cancellationToken);
    public Task FlashAsync(FirmwareEntry firmwareEntry, IFlashProgress? progress = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return OnFlashAsync(firmwareEntry, progress, cancellationToken);
    }
    protected abstract Task OnResetAsync(IFlashProgress? progress, CancellationToken cancellationToken);

    public Task ResetAsync(IFlashProgress? progress = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return OnResetAsync(progress, cancellationToken);
    }
}
