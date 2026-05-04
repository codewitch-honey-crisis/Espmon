
namespace Espmon;

public sealed class ScreenValuesController : ControllerBase
{
    internal ScreenValuesController(ScreenController parent) : base(parent)
    {
        Parent = parent;
        _value1 = new ScreenValueController(this);
        _value2 = new ScreenValueController(this);
    }
    public ScreenController Parent { get; }

    public string _label = string.Empty;
    public string Label
    {
        get { return _label; }
        set
        {
            if (value != _label)
            {
                UpdateProperty(nameof(Label), () => _label = value);
            }
        }
    }
    private int _color;
    public int Color
    {
        get { return _color; }
        set
        {
            if (value != _color)
            {
                UpdateProperty(nameof(Color), () => _color = value);
            }
        }
    }
    private ScreenValueController _value1;
    public ScreenValueController Value1
    {
        get { return _value1; }
        set
        {
            if (value != _value1)
            {   
                UpdateProperty(nameof(Value1), () => _value1 = value);
            }
        }
    }
    private ScreenValueController _value2;
    public ScreenValueController Value2
    {
        get { return _value2; }
        set
        {
            if (value != _value2)
            {
                UpdateProperty(nameof(Value2), () => _value2 = value);
            }
        }
    }
    internal JsonObject ToJson()
    {
        if (Label == null) throw new System.InvalidOperationException("Trying to serialize when Label is null");
        if (Value1 == null) throw new System.InvalidOperationException("Trying to serialize when Value1 is null");
        if (Value2 == null) throw new System.InvalidOperationException("Trying to serialize when Value2 is null");
        var json = new JsonObject();
        json.Add("label", Label);
        if (Color != -1)
        {
            json.Add("color", Espmon.ScreenController.GetJsonColorString(Color));
        }
        json.Add("value1", Value1.ToJson());
        json.Add("value2", Value2.ToJson());
        return json;
    }
    internal static ScreenValuesController FromJson(ScreenController parent, JsonObject json)
    {
        var result = new ScreenValuesController(parent);
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
        if (json.TryGetValue("label", out var label))
        {
            if (label is string str)
            {
                result.Label = str;
            }
            else
            {
                throw new ScreenParseException($"Screen entry \"label\" field must be a string.", 0, 0, 0);
            }
        }
        else
        {
            throw new ScreenParseException($"Screen entry must have a \"label\" field.", 0, 0, 0);
        }
        if (json.TryGetValue("value1", out var value1))
        {
            if (value1 is JsonObject obj)
            {
                result.Value1 = ScreenValueController.FromJson(result, obj);
            }
            else
            {
                throw new ScreenParseException($"Screen value entry \"value1\" field must be a valid JSON object.", 0, 0, 0);
            }
        }
        else
        {
            throw new ScreenParseException($"Screen value entry must have a \"value1\" entry.", 0, 0, 0);
        }
        if (json.TryGetValue("value2", out var value2))
        {
            if (value2 is JsonObject obj)
            {
                result.Value2 = ScreenValueController.FromJson(result, obj);
            }
            else
            {
                throw new ScreenParseException($"Screen value entry \"value2\" field must be a valid JSON object.", 0, 0, 0);
            }
        }
        else
        {
            throw new ScreenParseException($"Screen value entry must have a \"value2\" entry.", 0, 0, 0);
        }

        return result;
    }
}
