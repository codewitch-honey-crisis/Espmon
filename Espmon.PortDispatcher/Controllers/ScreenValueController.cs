
using HWKit;

using System.Xml.Linq;

namespace Espmon;

public sealed class ScreenValueController : ControllerBase
{
    internal ScreenValueController(ScreenValuesController parent) : base(parent)
    {
        Parent = parent;
        _minExpression = new HardwareInfoEmptyExpression();
        _maxExpression = new HardwareInfoEmptyExpression();
        _valueExpression = new HardwareInfoEmptyExpression();
    }
    public ScreenValuesController Parent { get; }
    private int _color = unchecked((int)0xFFFFFFFF);
    public int Color
    {
        get { return _color; }
        set
        {
            if (_color != value)
            {
                UpdateProperty(nameof(Color), () => _color = value);
            }
        }
    }
    public float Value
    {
        get
        {
            try
            {
                return Entry.Value;
            }
            catch
            {
                return float.NaN;
            }
        }
    }
    public HardwareInfoEntry Entry
    {
        get
        {
            try
            {
                return _valueExpression != null ? Parent.Parent.Parent.Evaluate(_valueExpression).First() : HardwareInfoEntry.Empty;
            }
            catch
            {
                return HardwareInfoEntry.Empty;
            }
        }
    }
    public string Unit
    {
        get
        {
            var result = string.Empty;
            try
            {
                result = Entry.Unit;
            }
            catch { }
            if (!string.IsNullOrEmpty(result))
            {
                return result;
            }
            return BaseUnit;
        }
    }
    public string BaseUnit
    {
        get
        {
            try
            {
                return _valueExpression != null ? Parent.Parent.Parent.GetUnit( _valueExpression) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
    public float Min
    {
        get
        {
            try
            {
                return _minExpression != null ? Parent.Parent.Parent.Evaluate(_minExpression).First().Value : float.NaN;
            }
            catch
            {
                return float.NaN;
            }
        }
    }
    public float Max
    {
        get
        {
            try
            {
                return _maxExpression != null ? Parent.Parent.Parent.Evaluate(_maxExpression).First().Value : float.NaN;
            }
            catch
            {
                return float.NaN;
            }
        }
    }
    public float Scaled
    {
        get
        {
            return (Value - Min) / (Max - Min);
        }
    }

    private bool _hasGradient;
    public bool HasGradient
    {
        get { return _hasGradient; }
        set
        {
            if (_hasGradient != value)
            {
                UpdateProperty(nameof(HasGradient), () => _hasGradient = value);
            }
        }
    }
    HardwareInfoExpression _minExpression;
    public HardwareInfoExpression MinExpression
    {
        get { return _minExpression; }
        set
        {
            if (!object.ReferenceEquals(_minExpression, value))
            {
                UpdateProperty(nameof(Min), () => _minExpression = value);
            }
        }
    }
    HardwareInfoExpression _maxExpression;
    public HardwareInfoExpression MaxExpression
    {
        get { return _maxExpression; }
        set
        {
            if (!object.ReferenceEquals(_maxExpression, value))
            {
                UpdateProperty(nameof(MaxExpression), () => _maxExpression = value);
            }
        }
    }
    HardwareInfoExpression _valueExpression;
    public HardwareInfoExpression ValueExpression
    {
        get { return _valueExpression; }
        set
        {
            if (!object.ReferenceEquals(_valueExpression, value))
            {
                UpdateProperty(nameof(ValueExpression),()=>_valueExpression=value);
            }
        }
    }
    private static void _AddExprOrLit(JsonObject obj, string name, HardwareInfoExpression? expr)
    {
        if (expr == null) return;
        if (expr is HardwareInfoLiteralExpression lit)
        {
            obj.Add(name, (double)lit.Value);
        }
        else
        {
            obj.Add(name, expr.ToString());
        }
    }
    internal JsonObject ToJson()
    {
        var json = new JsonObject();
        if (ValueExpression == null) throw new System.InvalidOperationException("Trying to serialize when ValueExpression is null");
        if (MaxExpression == null) throw new System.InvalidOperationException("Trying to serialize when MaxExpression is null");
        _AddExprOrLit(json, "value", ValueExpression);
        _AddExprOrLit(json, "max", MaxExpression);
        _AddExprOrLit(json, "min", MinExpression);
        if (Color != -1)
        {
            json.Add("color", Espmon.ScreenController.GetJsonColorString(Color));
        }
        if (HasGradient)
        {
            json.Add("has_gradient", true);
        }
        return json;
    }
    internal static ScreenValueController FromJson(ScreenValuesController parent, JsonObject json)
    {
        var result = new ScreenValueController(parent);
        if (json.TryGetValue("color", out var color))
        {
            int icol = -1;
            if (color is string str)
            {
                if (!ScreenController.TryGetColor(str, out icol))
                {
                    throw new ScreenParseException($"Invalid color value: {str}", 0, 0, 0);
                }
            }
            else if (color is double d && d == (int)d)
            {
                icol = (int)d;
            }
            else
            {
                throw new ScreenParseException($"Invalid color value: {color}", 0, 0, 0);
            }
            result.Color = icol;
        }
        else
        {
            result.Color = -1; // white
        }
        if (json.TryGetValue("value", out var value))
        {
            if (value is double d)
            {
                result.ValueExpression = new HardwareInfoLiteralExpression((float)d);
            }
            else if (value is string)
            {
                result.ValueExpression = HardwareInfoExpression.Parse((string)value);
            }
            else
            {
                throw new ScreenParseException($"Screen value entry \"value\" field must be numeric or an expression.", 0, 0, 0);
            }
        }
        else
        {
            throw new ScreenParseException($"Screen value entry must have a \"value\" field.", 0, 0, 0);
        }
        if (json.TryGetValue("max", out var max))
        {
            if (max is double d)
            {
                result.MaxExpression = new HardwareInfoLiteralExpression((float)d);
            }
            else if (max is string)
            {
                result.MaxExpression = HardwareInfoExpression.Parse((string)max);
            }
            else
            {
                throw new ScreenParseException($"Screen value entry \"max\" field must be numeric or an expression.", 0, 0, 0);
            }
        }
        else
        {
            throw new ScreenParseException($"Screen value entry must have a \"max\" field.", 0, 0, 0);
        }
        if (json.TryGetValue("min", out var min))
        {
            if (min is double d)
            {
                result.MinExpression = new HardwareInfoLiteralExpression((float)d);
            }
            else if (min is string)
            {
                result.MinExpression = HardwareInfoExpression.Parse((string)min);
            }
            else
            {
                throw new ScreenParseException($"Screen value entry \"min\" field must be numeric or an expression.", 0, 0, 0);
            }
        }
        else
        {
            result.MinExpression = new HardwareInfoLiteralExpression(0);
        }
        if (json.TryGetValue("has_gradient", out var hasGradient))
        {
            if (hasGradient is bool b)
            {
                result.HasGradient = b;
            }
            else
            {
                throw new ScreenParseException($"Screen value entry \"has_gradient\" field must be true or false.", 0, 0, 0);
            }
        }
        return result;
    }
}
