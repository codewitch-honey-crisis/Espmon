using HWKit;

using System;
using System.ComponentModel;

namespace Espmon;

public class ProviderEntry : INotifyPropertyChanged
{
    public ProviderEntry(ProviderController provider)
    {
        Provider = provider;
        provider.PropertyChanged += Provider_PropertyChanged;
    }

    private void Provider_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if(e.PropertyName==null || e.PropertyName==nameof(IsStarted))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStarted)));
        }
    }

    public ProviderController Provider { get; }
    public string Name => Provider.Name;
    public string[] Paths => Provider.Paths;
    public string Description => Provider.Description;
    public string Identifier => Provider.Identifier;
    public bool IsStarted
    {
        get
        {
            return Provider.IsStarted;
        }
        set
        {
            if(IsStarted!=value)
            {
                if(value)
                {
                    try
                    {
                        Provider.Start();
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStarted)));
                    }
                    catch
                    {

                    }
                } else
                {
                    try
                    {
                        Provider.Stop();
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStarted)));
                    }
                    catch
                    {

                    }
                }
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    
}
