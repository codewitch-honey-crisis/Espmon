using Microsoft.UI;
using Microsoft.UI.Xaml;

using System;
using System.ComponentModel;
using System.Runtime.Versioning;

using Windows.UI;

namespace Espmon;

[SupportedOSPlatform("windows")]
public sealed class SessionEntry : IComparable<SessionEntry>, INotifyPropertyChanged
{
    public SessionEntry(SessionController session)
    {
        ArgumentNullException.ThrowIfNull(session, nameof(session));
        this.Session = session;
        session.PropertyChanged += Session_PropertyChanged;
    }

    private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if(e.PropertyName =="Status")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOpen)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsClosed)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFlashing)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanFlash)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScreenMetrics)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Input)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FlashVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OpenVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RunningVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TextColor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
        if(e.PropertyName=="Name")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public SessionController Session { get; }
    public bool IsOpen => Session.Status != SessionStatus.Closed;
    public bool CanFlash => Session.GetUpgrade() != FirmwareUpgrade.NotRequired;
    public bool IsFlashing => Session.Status == SessionStatus.Flashing;
    public bool IsClosed => Session.Status == SessionStatus.Closed;
    
    public string Name {
        get
        {
            if (Session.Device != null)
            {
                return Session.Device.Name;
            }
            return Session.PortName;
        }
        set
        {
            if (Session.Device == null)
            {
                throw new InvalidOperationException("Cannot rename the session until the associated device is established");
            }
            Session.Device.Name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }
    public string ScreenMetrics
    {
        get
        {
            var mono = Session.IsMonochrome ? "(Monochrome) " : "";
            return $"{Session.HorizontalResolution}x{Session.VerticalResolution} {mono}at {(int)Math.Round(Session.Dpi)} DPI";
        }
    }
    public string Input
    {
        get
        {
            return $"Input type is {Session.Input}";
        }
    }
    public Color TextColor => IsOpen ? Colors.White : Colors.LightGray;

    public event PropertyChangedEventHandler? PropertyChanged;
    public Visibility FlashVisibility => CanFlash||IsFlashing ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OpenVisibility => Session.Status==SessionStatus.Closed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RunningVisibility =>  (Session.Status==SessionStatus.Busy||Session.Status==SessionStatus.NeedScreen)? Visibility.Visible : Visibility.Collapsed;
    public Visibility DeleteVisibility => (Session.Device!=null && Session.Device.MacAddress!=null) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ScreenListVisibility => Session.IsWaitingForScreenChange?Visibility.Collapsed:Visibility.Visible;

    public void Refresh()
    {
        Session.Refresh();
    }

    public int CompareTo(SessionEntry? other)
    {
        if(other == null) return 1;
        if(this == other) return 0;
        return string.CompareOrdinal(Name, other.Name);
    }
}
