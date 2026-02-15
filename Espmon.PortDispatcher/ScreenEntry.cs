using HWKit;

using System.ComponentModel;

namespace Espmon;

public partial class ScreenEntry : Component, INotifyPropertyChanged
{
    public ScreenEntry(Screen parent)
    {
        _parent = parent;
        InitializeComponent();
    }
    private Screen _parent;
    public Screen Parent
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
    public HardwareInfoCollection? HardwareInfo
    {
        get
        {
            return _parent?.HardwareInfo;
        }
    }
    public ScreenEntry(Screen parent, IContainer container)
    {
        _parent = parent;
        container.Add(this);

        InitializeComponent();
    }
    public void Refresh()
    {
        if(Value1!=null)
        {
            Value1.Refresh();
        }
        if(Value2!=null)
        {
            Value2.Refresh();
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public string _label = string.Empty;

    public string Label
    {
        get { return _label; }
        set
        {
            if (value != _label)
            {
                _label = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
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
                _color = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color)));
            }
        }
    }
    private ScreenValueEntry? _value1;
    public ScreenValueEntry? Value1
    {
        get { return _value1; }
        set {
            if (value != _value1)
            {
                _value1 = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value1)));
            }
        }
    }
    private ScreenValueEntry? _value2;
    public ScreenValueEntry? Value2
    {
        get { return _value2; }
        set
        {
            if (value != _value2)
            {
                _value2 = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value2)));
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
            json.Add("color", Espmon.Screen.GetJsonColorString(Color));
        }
        json.Add("value1", Value1.ToJson());
        json.Add("value2", Value2.ToJson());
        return json;
    }
    internal static ScreenEntry FromJson(Screen parent, JsonObject json)
    {
        ScreenEntry result = new ScreenEntry(parent);
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
            if(value1 is JsonObject obj)
            {
                result.Value1 = ScreenValueEntry.FromJson(result, obj);
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
                result.Value2 = ScreenValueEntry.FromJson(result, obj);
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
