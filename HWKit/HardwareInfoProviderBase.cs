using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;

namespace HWKit
{
    public abstract class HardwareInfoProviderBase : IHardwareInfoProvider
    {
        private bool _disposed = false;
        protected abstract HardwareInfoProviderState GetState();
        protected virtual string GetIdentifier()
        {
            var name = GetDisplayName();
            var sb = new StringBuilder(name.Length);
            var wasUnderscore = false;
            for (var i = 0; i < name.Length; i++)
            {
                if (char.IsLetter(name[i]))
                {
                    sb.Append(name[i]);
                    wasUnderscore = false;
                }
                else if ('_' == name[i])
                {
                    if (!wasUnderscore)
                    {
                        sb.Append("_");
                    }
                    wasUnderscore = true;
                }
            }
            return sb.ToString();
        }
        protected virtual string GetDisplayName()
        {
            var result = GetType().FullName;
            if (result == null) throw new Exception("Unable to retrieve display name");
            return result;
        }
        protected virtual void OnStart()
        {

        }
        protected virtual void OnStop()
        {

        }
        public string DisplayName { get { return GetDisplayName(); } }
        public string Identifier
        {
            get { return GetIdentifier(); }
        }
        protected virtual void Publish(string path, string unit, Func<float> getter)
        {
            if (Published != null)
            {
                var args = new HardwareInfoProviderPublishedEventArgs(this, path, unit, getter);
                Published(this, args);  
            }
        }
        protected virtual void Revoke(string path)
        {
            if (Revoked != null)
            {
                var args = new HardwareInfoProviderRevokedEventArgs(this, path);
                Revoked(this, args);
            }
        }
        public void Start()
        {
            if (State == HardwareInfoProviderState.Stopped)
            {
                OnStart();

                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public void Stop()
        {
            if (State == HardwareInfoProviderState.Started)
            {
                OnStop();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            
        }

        public HardwareInfoProviderState State => GetState();
        public event EventHandler<HardwareInfoProviderPublishedEventArgs>? Published;
        public event EventHandler<HardwareInfoProviderRevokedEventArgs>? Revoked;
        public event EventHandler<EventArgs>? StateChanged;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Stop();
                }

                _disposed = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~HardwareInfoProviderBase()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        void IDisposable.Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            // GC.SuppressFinalize(this);
        }

        public virtual HardwareInfoSuggestion[] GetSuggestions(HardwareInfoSuggestionContext context)
        {
            return Array.Empty<HardwareInfoSuggestion>();
        }
        public virtual HardwareInfoExpression? ApplySuggestion(HardwareInfoSuggestionContext context, object key)
        {
            return null;
        }
    }
}
