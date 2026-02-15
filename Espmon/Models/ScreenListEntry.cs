using System;
using System.ComponentModel;

namespace Espmon;

public sealed class ScreenListEntry : INotifyPropertyChanged
{
    public ScreenListEntry(string name, Screen screen) { ArgumentNullException.ThrowIfNull(name); _name = name; ArgumentNullException.ThrowIfNull(screen); _screen = screen; }
    private Screen? _screen;
    public Screen? Screen { 
        get
        {
            return _screen;
        }
        set
        {
            _screen = value;
            PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Screen)));
        }
    }
    public bool IsDefault { 
        get { return string.IsNullOrEmpty(Name)||Name=="(default)"; }
    }
    private string _name;
    public string Name
    {
        get { return _name; }
        set
        {
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDefault)));
        }
    }
    public override string ToString()
    {
        return _name;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
