using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Xml.Linq;

namespace Espmon;

public abstract class ControllerBase : INotifyPropertyChanged, INotifyPropertyChanging
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event PropertyChangingEventHandler? PropertyChanging;

    protected SynchronizationContext? SyncContext { get; }

    protected void OnPropertyChanged(string name)
    {
        Post(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
    }
    protected void UpdateProperty(string name, Action setter)
    {
        PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(name));
        setter();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    }
    protected void UpdateProperties(Action setter, params string[] names)
    {
        for (var i = 0; i < names.Length; ++i)
        {
            PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(names[i]));
        }
        setter();
        for (var i = 0; i < names.Length; ++i)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(names[i]));
        }
    }
    protected ControllerBase(SynchronizationContext? syncContext)
    {
        SyncContext = syncContext;
    }
    protected ControllerBase(ControllerBase parent)
    {
        ArgumentNullException.ThrowIfNull(parent, nameof(parent));
        SyncContext = parent.SyncContext;
    }
        
    protected Task PostAsync(Action action)
    {
        TaskCompletionSource tcs = new TaskCompletionSource();

        if (SyncContext != null)
        {
            try
            {
                SyncContext.Post(_ => action(), (object state) =>
                {
                    ((TaskCompletionSource)state).SetResult();
                });
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }
        else
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }
        return tcs.Task;
    }
    protected void Post(Action action)
    {
        if (SyncContext != null)
        {
            SyncContext.Post(_ => action(), null);
        }
        else
        {
            action();
        }
    }
}