namespace HWKit;

public sealed class HardwareInfoSuggestion
{
    public object Key { get; }
    public string Action { get; }
    public string Description { get; }

    public HardwareInfoSuggestion(object key, string action, string description)
    {
        ArgumentNullException.ThrowIfNull(key, nameof(key));
        Key = key;
        ArgumentNullException.ThrowIfNull(action, nameof(action));
        if(action.Length==0) { throw new ArgumentException("Argument must not be empty", nameof(action)); }
        Action = action;
        ArgumentNullException.ThrowIfNull(description, nameof(description));
        if (description.Length == 0) { throw new ArgumentException("Argument must not be empty", nameof(description)); }
        Description = description;
    }
}
