namespace HWKit
{
    public class HardwareInfoProviderPublishedEventArgs : EventArgs
    {
        public HardwareInfoProviderPublishedEventArgs(IHardwareInfoProvider provider, string internalPath, string unit, Func<float> getter)
        {
            Provider = provider;
            InternalPath = internalPath;
            Path = $"/{Provider.Identifier}{InternalPath}";
            Unit = unit;
            Getter = getter;
        }
        public IHardwareInfoProvider Provider { get; }
        public string InternalPath { get; }
        public string Path { get; }
        public string Unit { get; }
        public Func<float> Getter { get; }
    }
    public class HardwareInfoProviderRevokedEventArgs : EventArgs
    {
        public HardwareInfoProviderRevokedEventArgs(IHardwareInfoProvider provider, string internalPath)
        {
            Provider = provider;
            InternalPath = internalPath;
            Path = $"{Provider.Identifier}{InternalPath}";
        }
        public IHardwareInfoProvider Provider { get; }
        public string InternalPath { get; }
        public string Path { get; }

    }
    public enum HardwareInfoProviderStatus
    {
        Stopped = 0,
        Started = 1
    }
    
    
    public interface IHardwareInfoProvider : IDisposable
    {
        public event EventHandler<HardwareInfoProviderPublishedEventArgs>? Published;
        public event EventHandler<HardwareInfoProviderRevokedEventArgs>? Revoked;
        public event EventHandler<EventArgs>? StateChanged;
        string DisplayName { get; }
        string Description { get; }
        string Identifier { get; }
        HardwareInfoProviderStatus Status { get; }
        void Start();
        void Stop();
        HardwareInfoSuggestion[] GetSuggestions(HardwareInfoSuggestionContext context);

        HardwareInfoExpression? ApplySuggestion(HardwareInfoSuggestionContext context, object key);
    }
}
