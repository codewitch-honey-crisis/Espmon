namespace HWKit;

public sealed class HardwareInfoSuggestion
{
    private static readonly object _averageKey = new object();
    private static readonly object _sumKey = new object();
    private static readonly object _minKey = new object();
    private static readonly object _maxKey = new object();
    private static readonly object _firstKey = new object();
    private static readonly object _lastKey = new object();
    private static readonly object _roundKey = new object();
    private static readonly object _round1Key = new object();
    private static readonly object _past30SecKey = new object();
    private static readonly object _past1MinKey = new object();
    private static readonly object _past5MinKey = new object();

    public object Key { get; }
    public string? Category { get; }
    public string Action { get; }
    public string Description { get; }

    public HardwareInfoSuggestion(object key, string action, string description, string? category)
    {
        ArgumentNullException.ThrowIfNull(key, nameof(key));
        Key = key;
        ArgumentNullException.ThrowIfNull(action, nameof(action));
        if (action.Length == 0) { throw new ArgumentException("Argument must not be empty", nameof(action)); }
        Action = action;
        ArgumentNullException.ThrowIfNull(description, nameof(description));
        if (description.Length == 0) { throw new ArgumentException("Argument must not be empty", nameof(description)); }
        Description = description;
        Category = category;
    }
    private static void FillSuggestions(HardwareInfoSuggestionContext context, bool isReduced, IList<HardwareInfoSuggestion> result)
    {
        string? fn = null;
        if (context.Expression is HardwareInfoInvokeExpression invoke)
        {
            fn = invoke.Function.Name;
        }
        if (!isReduced)
        {
            if (fn == null || !fn.StartsWith("round", StringComparison.Ordinal))
            {
                result.Add(new HardwareInfoSuggestion(_roundKey, "Round to a whole number", "Rounds each result of the expression to a whole number", null));
                result.Add(new HardwareInfoSuggestion(_round1Key, "Round to a x.x", "Rounds each result of the expression to a fractional number", null));
            }
            if (fn == null || !fn.Equals("avg", StringComparison.Ordinal))
            {
                result.Add(new HardwareInfoSuggestion(_averageKey, "Take the average", "Computes the average of the expression", null));
            }
            if (fn == null || !fn.Equals("sum", StringComparison.Ordinal))
            {
                result.Add(new HardwareInfoSuggestion(_sumKey, "Take the sum", "Computes the sum of the expression", null));
            }
            if (fn == null || !fn.Equals("first", StringComparison.Ordinal))
            {
                result.Add(new HardwareInfoSuggestion(_firstKey, "Take the first item", "Gets the first result of the expression", null));
            }
            if (fn == null || !fn.Equals("last", StringComparison.Ordinal))
            {
                result.Add(new HardwareInfoSuggestion(_lastKey, "Take the last item", "Gets the last result of the expression", null));
            }
            if (fn == null || !fn.Equals("min", StringComparison.Ordinal))
            {
                result.Add(new HardwareInfoSuggestion(_minKey, "Take the minimum value", "Gets the minimum value result of the expression", null));
            }
            if (fn == null || !fn.Equals("max", StringComparison.Ordinal))
            {
                result.Add(new HardwareInfoSuggestion(_maxKey, "Take the maximum value", "Gets the maximum value result of the expression", null));
            }
        }
        else
        {
            if (fn == null || !fn.StartsWith("round", StringComparison.Ordinal))
            {
                result.Add(new HardwareInfoSuggestion(_roundKey, "Round to a whole number", "Rounds the result of the expression to a whole number", null));
                result.Add(new HardwareInfoSuggestion(_round1Key, "Round to a x.x", "Rounds the result of the expression to a fractional number", null));
            }
            if (fn == null || !fn.Equals("past", StringComparison.Ordinal))
            {
                result.Add(new HardwareInfoSuggestion(_past30SecKey, "30 second history", "Gets the results of the expression for the past 30 seconds", null));
                result.Add(new HardwareInfoSuggestion(_past1MinKey, "1 minute history", "Gets the results of the expression for the past minute", null));
                result.Add(new HardwareInfoSuggestion(_past5MinKey, "5 minute history", "Gets the results of the expression for the past 5 minutes", null));
            }
        }
    }
    public static HardwareInfoSuggestion[] GetSuggestions(HardwareInfoSuggestionContext context)
    {
        var result = new List<HardwareInfoSuggestion>();
        if (context.ParseException == null)
        {
            if (context.Expression != null && !context.Expression.IsEmpty)
            {
                FillSuggestions(context, context.Expression.IsReduced, result);
            }
            else
            {
                FillSuggestions(context, false, result);
            }
        }
        return result.ToArray();
    }
    public static HardwareInfoExpression? ApplySuggestion(HardwareInfoSuggestionContext context, object key)
    {
        if (context.Expression != null)
        {
            if (key == _roundKey)
            {
                var fn = new HardwareInfoInvokeExpression()
                {
                    Function = HardwareInfoFunction.Round
                };
                fn.Children.Add(context.Expression);
                return fn;
            }
            if (key == _round1Key)
            {
                var fn = new HardwareInfoInvokeExpression()
                {
                    Function = HardwareInfoFunction.Round1
                };
                fn.Children.Add(context.Expression);
                return fn;
            }
            if (key == _averageKey)
            {
                var fn = new HardwareInfoInvokeExpression()
                {
                    Function = HardwareInfoFunction.Avg
                };
                fn.Children.Add(context.Expression);
                return fn;
            }
            if (key == _sumKey)
            {
                var fn = new HardwareInfoInvokeExpression()
                {
                    Function = HardwareInfoFunction.Sum
                };
                fn.Children.Add(context.Expression);
                return fn;
            }
            if (key == _firstKey)
            {
                var fn = new HardwareInfoInvokeExpression()
                {
                    Function = HardwareInfoFunction.First
                };
                fn.Children.Add(context.Expression);
                return fn;
            }
            if (key == _lastKey)
            {
                var fn = new HardwareInfoInvokeExpression()
                {
                    Function = HardwareInfoFunction.Last
                };
                fn.Children.Add(context.Expression);
                return fn;
            }
            if (key == _minKey)
            {
                var fn = new HardwareInfoInvokeExpression()
                {
                    Function = HardwareInfoFunction.Min
                };
                fn.Children.Add(context.Expression);
                return fn;
            }
            if (key == _maxKey)
            {
                var fn = new HardwareInfoInvokeExpression()
                {
                    Function = HardwareInfoFunction.Max
                };
                fn.Children.Add(context.Expression);
                return fn;
            }
            if (key == _past30SecKey)
            {
                var fn = new HardwareInfoInvokeExpression()
                {
                    Function = HardwareInfoFunction.Past
                };
                fn.Children.Add(new HardwareInfoUnitExpression(new HardwareInfoLiteralExpression(30), "sec"));
                fn.Children.Add(context.Expression);
                return fn;
            }
            if (key == _past1MinKey)
            {
                var fn = new HardwareInfoInvokeExpression()
                {
                    Function = HardwareInfoFunction.Past
                };
                fn.Children.Add(new HardwareInfoUnitExpression(new HardwareInfoLiteralExpression(1), "min"));
                fn.Children.Add(context.Expression);
                return fn;
            }
            if (key == _past5MinKey)
            {
                var fn = new HardwareInfoInvokeExpression()
                {
                    Function = HardwareInfoFunction.Past
                };
                fn.Children.Add(new HardwareInfoUnitExpression(new HardwareInfoLiteralExpression(5), "min"));
                fn.Children.Add(context.Expression);
                return fn;
            }
        }
        return null;
    }
}
