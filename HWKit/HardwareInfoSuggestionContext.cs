namespace HWKit;

public sealed class HardwareInfoSuggestionContext
{
    public HardwareInfoExpression? Expression { get; }
    public IEnumerable<HardwareInfoEntry> Matches { get; }
    public HardwareInfoParseException? ParseException { get; }
    public IReadOnlyList<IHardwareInfoProvider> Providers { get; }
    public HardwareInfoSuggestionContext(
HardwareInfoExpression? expression,
IEnumerable<HardwareInfoEntry> matches,
HardwareInfoParseException? parseException,
IReadOnlyList<IHardwareInfoProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(providers);

        Expression = expression;
        Matches = matches;
        ParseException = parseException;
        Providers = providers;
    }
}
