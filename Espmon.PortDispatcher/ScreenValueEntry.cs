using HWKit;

using System.ComponentModel;

namespace Espmon;

public partial class ScreenValueEntry : Component, INotifyPropertyChanged
{
    public ScreenValueEntry(ScreenEntry parent)
    {
        _parent = parent;
        InitializeComponent();
    }
    public Screen? Screen
    {
        get
        {
            return _parent?.Parent; 
        }
    }
    public HardwareInfoCollection? HardwareInfo
    {
        get
        {
            return Screen?.HardwareInfo;
        }
    }

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Max)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Min)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }
    public ScreenValueEntry(ScreenEntry parent, IContainer container)
    {
        _parent = parent;
        container.Add(this);

        InitializeComponent();
    }
    private ScreenEntry _parent;
    public ScreenEntry Parent
    {
        get
        {
            return _parent;
        }
        set
        {
            _parent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Parent)));
        }
    }
    private int _color = 0;
    public int Color { 
        get { return _color; }
        set
        {
            _color = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color)));
        }
    }
    private bool _hasGradient = false;
    public bool HasGradient
    {
        get { return _hasGradient; }
        set
        {
            _hasGradient = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasGradient)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public float Value
    {
        get
        {
            try
            {
                return HardwareInfo != null && _valueExpression!=null ? _valueExpression.Evaluate(HardwareInfo).First().Value : float.NaN;
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
                return HardwareInfo != null && _valueExpression != null ? _valueExpression.Evaluate(HardwareInfo).First():HardwareInfoEntry.Empty;
            }
            catch
            {
                return HardwareInfoEntry.Empty;
            }
        }
    }
    public float Min
    {
        get
        {
            try
            {
                return HardwareInfo != null && _minExpression!=null ? _minExpression.Evaluate(HardwareInfo).First().Value  : float.NaN;
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
                return HardwareInfo != null && _maxExpression != null ? _maxExpression.Evaluate(HardwareInfo).First().Value : float.NaN;
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

    private HardwareInfoExpression? _valueExpression;
    public HardwareInfoExpression? ValueExpression
    {
        get { return _valueExpression; }
        set
        {
            if (_valueExpression != value)
            {
                _valueExpression = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValueExpression)));
            }
        }
    }
    
    private HardwareInfoExpression? _minExpression;
    public HardwareInfoExpression? MinExpression
    {
        get { return _minExpression; }
        set
        {
            if (_minExpression != value)
            {
                _minExpression = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MinExpression)));
            }
        }
    }

    private HardwareInfoExpression? _maxExpression;
    public HardwareInfoExpression? MaxExpression
    {
        get { return _maxExpression; }
        set
        {
            if (_maxExpression != value)
            {
                _maxExpression = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaxExpression)));
            }
        }
    }
    private static void AddExprOrLit(JsonObject obj, string name, HardwareInfoExpression? expr)
    {
        if (expr == null) return;
        if(expr is HardwareInfoLiteralExpression lit)
        {
            obj.Add(name, (double)lit.Value);
        } else
        {
            obj.Add(name, expr.ToString());
        }
    }
    internal JsonObject ToJson()
    {
        var json = new JsonObject();
        if (ValueExpression == null) throw new System.InvalidOperationException("Trying to serialize when ValueExpression is null");
        if (MaxExpression == null) throw new System.InvalidOperationException("Trying to serialize when MaxExpression is null");
        AddExprOrLit(json,"value", ValueExpression);
        AddExprOrLit(json, "max", MaxExpression);
        AddExprOrLit(json, "min", MinExpression);
        if (Color != -1)
        {
            json.Add("color", Espmon.Screen.GetJsonColorString(Color));
        }
        if(HasGradient)
        {
            json.Add("has_gradient", true);
        }
        return json;
    }
    internal static ScreenValueEntry FromJson(ScreenEntry parent, JsonObject json)
    {
        ScreenValueEntry result = new ScreenValueEntry(parent);
        if (json.TryGetValue("color", out var color))
        {
            int icol = -1;
            if (color is string str)
            {
                if (!Screen.TryGetColor(str, out icol))
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
            if(value is double d)
            {
                result.ValueExpression = new HardwareInfoLiteralExpression((float)d);
            } else if(value is string)
            {
                result.ValueExpression = HardwareInfoExpression.Parse((string)value);
            } else
            {
                throw new ScreenParseException($"Screen value entry \"value\" field must be numeric or an expression.", 0, 0, 0);
            }
        } else
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
        if(json.TryGetValue("has_gradient",out var hasGradient))
        {
            if(hasGradient is bool b)
            {
                result.HasGradient = b;
            } else
            {
                throw new ScreenParseException($"Screen value entry \"has_gradient\" field must be true or false.", 0, 0, 0);
            }
        }
        return result;
    }
}
