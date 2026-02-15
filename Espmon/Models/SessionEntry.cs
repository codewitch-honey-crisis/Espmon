using Microsoft.UI;
using Microsoft.UI.Xaml;

using System;
using System.ComponentModel;

using Windows.UI;

namespace Espmon;

public sealed class SessionEntry : IComparable<SessionEntry>, INotifyPropertyChanged
{
    public SessionEntry(Session session)
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RequiresFlash)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScreenMetrics)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Input)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FlashVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OpenVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RunningVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TextColor)));
        }
    }

    public Session Session { get; }
    public bool IsOpen => Session.Status != SessionStatus.Closed;
    public bool RequiresFlash => Session.Status == SessionStatus.RequiresFlash;
    public bool IsFlashing => Session.Status == SessionStatus.Flashing;
    public bool IsClosed => Session.Status == SessionStatus.Closed;
    public string Name => Session.Name;
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
            return $"Input type is {Session.InputType}";
        }
    }
    public Color TextColor => IsOpen ? Colors.White : Colors.LightGray;

    public event PropertyChangedEventHandler? PropertyChanged;
    public Visibility FlashVisibility => RequiresFlash||IsFlashing ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OpenVisibility => Session.Status==SessionStatus.Closed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RunningVisibility =>  (Session.Status==SessionStatus.Busy||Session.Status==SessionStatus.NeedScreen)? Visibility.Visible : Visibility.Collapsed;

    public void Refresh()
    {
        Session.Update();
    }

    public int CompareTo(SessionEntry? other)
    {
        if(other == null) return 1;
        if(this == other) return 0;
        return string.CompareOrdinal(Session.Name, other.Session.Name);
    }
}
