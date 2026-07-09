using System;
using System.ComponentModel;

namespace Espmon;

public sealed class ScreenListEntry : INotifyPropertyChanged
{
    public ScreenListEntry(string name, ScreenController screen) { ArgumentNullException.ThrowIfNull(name); _name = name; ArgumentNullException.ThrowIfNull(screen); _screen = screen; _screen?.PropertyChanged += _screen_PropertyChanged; }
    private ScreenController? _screen;
    public ScreenController? Screen { 
        get
        {
            return _screen;
        }
        set
        {
            if(_screen!=value)
            {
                if(_screen!=null)
                {
                    _screen.PropertyChanged -= _screen_PropertyChanged;
                }
                _screen = value;
                _screen?.PropertyChanged += _screen_PropertyChanged;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Screen)));
            }
            
        }
    }

    private void _screen_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == null || e.PropertyName.Equals(nameof(Name), StringComparison.Ordinal))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public bool IsDefault { 
        get { return false; }
    }
    private string _name;
    public string Name
    {
        get { return _name; }
        set
        {
            
            _name = value;
            _screen?.Name = value;
        }
    }
    public override string ToString()
    {
        return _name;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
