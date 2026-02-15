using HWKit;

using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace Espmon;

public partial class Screen : Component, INotifyPropertyChanged
{
    private static readonly System.Collections.Generic.Dictionary<string, int> _colors = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["AliceBlue"] = unchecked((int)0xFFF0F8FF),
        ["AntiqueWhite"] = unchecked((int)0xFFFAEBD7),
        ["Aqua"] = unchecked((int)0xFF00FFFF),
        ["Aquamarine"] = unchecked((int)0xFF7FFFD4),
        ["Azure"] = unchecked((int)0xFFF0FFFF),
        ["Beige"] = unchecked((int)0xFFF5F5DC),
        ["Bisque"] = unchecked((int)0xFFFFE4C4),
        ["Black"] = unchecked((int)0xFF000000),
        ["BlanchedAlmond"] = unchecked((int)0xFFFFEBCD),
        ["Blue"] = unchecked((int)0xFF0000FF),
        ["BlueViolet"] = unchecked((int)0xFF8A2BE2),
        ["Brown"] = unchecked((int)0xFFA52A2A),
        ["BurlyWood"] = unchecked((int)0xFFDEB887),
        ["CadetBlue"] = unchecked((int)0xFF5F9EA0),
        ["Chartreuse"] = unchecked((int)0xFF7FFF00),
        ["Chocolate"] = unchecked((int)0xFFD2691E),
        ["Coral"] = unchecked((int)0xFFFF7F50),
        ["CornflowerBlue"] = unchecked((int)0xFF6495ED),
        ["Cornsilk"] = unchecked((int)0xFFFFF8DC),
        ["Crimson"] = unchecked((int)0xFFDC143C),
        ["Cyan"] = unchecked((int)0xFF00FFFF),
        ["DarkBlue"] = unchecked((int)0xFF00008B),
        ["DarkCyan"] = unchecked((int)0xFF008B8B),
        ["DarkGoldenrod"] = unchecked((int)0xFFB8860B),
        ["DarkGray"] = unchecked((int)0xFFA9A9A9),
        ["DarkGreen"] = unchecked((int)0xFF006400),
        ["DarkKhaki"] = unchecked((int)0xFFBDB76B),
        ["DarkMagenta"] = unchecked((int)0xFF8B008B),
        ["DarkOliveGreen"] = unchecked((int)0xFF556B2F),
        ["DarkOrange"] = unchecked((int)0xFFFF8C00),
        ["DarkOrchid"] = unchecked((int)0xFF9932CC),
        ["DarkRed"] = unchecked((int)0xFF8B0000),
        ["DarkSalmon"] = unchecked((int)0xFFE9967A),
        ["DarkSeaGreen"] = unchecked((int)0xFF8FBC8F),
        ["DarkSlateBlue"] = unchecked((int)0xFF483D8B),
        ["DarkSlateGray"] = unchecked((int)0xFF2F4F4F),
        ["DarkTurquoise"] = unchecked((int)0xFF00CED1),
        ["DarkViolet"] = unchecked((int)0xFF9400D3),
        ["DeepPink"] = unchecked((int)0xFFFF1493),
        ["DeepSkyBlue"] = unchecked((int)0xFF00BFFF),
        ["DimGray"] = unchecked((int)0xFF696969),
        ["DodgerBlue"] = unchecked((int)0xFF1E90FF),
        ["Firebrick"] = unchecked((int)0xFFB22222),
        ["FloralWhite"] = unchecked((int)0xFFFFFAF0),
        ["ForestGreen"] = unchecked((int)0xFF228B22),
        ["Fuchsia"] = unchecked((int)0xFFFF00FF),
        ["Gainsboro"] = unchecked((int)0xFFDCDCDC),
        ["GhostWhite"] = unchecked((int)0xFFF8F8FF),
        ["Gold"] = unchecked((int)0xFFFFD700),
        ["Goldenrod"] = unchecked((int)0xFFDAA520),
        ["Gray"] = unchecked((int)0xFF808080),
        ["Green"] = unchecked((int)0xFF008000),
        ["GreenYellow"] = unchecked((int)0xFFADFF2F),
        ["Honeydew"] = unchecked((int)0xFFF0FFF0),
        ["HotPink"] = unchecked((int)0xFFFF69B4),
        ["IndianRed"] = unchecked((int)0xFFCD5C5C),
        ["Indigo"] = unchecked((int)0xFF4B0082),
        ["Ivory"] = unchecked((int)0xFFFFFFF0),
        ["Khaki"] = unchecked((int)0xFFF0E68C),
        ["Lavender"] = unchecked((int)0xFFE6E6FA),
        ["LavenderBlush"] = unchecked((int)0xFFFFF0F5),
        ["LawnGreen"] = unchecked((int)0xFF7CFC00),
        ["LemonChiffon"] = unchecked((int)0xFFFFFACD),
        ["LightBlue"] = unchecked((int)0xFFADD8E6),
        ["LightCoral"] = unchecked((int)0xFFF08080),
        ["LightCyan"] = unchecked((int)0xFFE0FFFF),
        ["LightGoldenrodYellow"] = unchecked((int)0xFFFAFAD2),
        ["LightGray"] = unchecked((int)0xFFD3D3D3),
        ["LightGreen"] = unchecked((int)0xFF90EE90),
        ["LightPink"] = unchecked((int)0xFFFFB6C1),
        ["LightSalmon"] = unchecked((int)0xFFFFA07A),
        ["LightSeaGreen"] = unchecked((int)0xFF20B2AA),
        ["LightSkyBlue"] = unchecked((int)0xFF87CEFA),
        ["LightSlateGray"] = unchecked((int)0xFF778899),
        ["LightSteelBlue"] = unchecked((int)0xFFB0C4DE),
        ["LightYellow"] = unchecked((int)0xFFFFFFE0),
        ["Lime"] = unchecked((int)0xFF00FF00),
        ["LimeGreen"] = unchecked((int)0xFF32CD32),
        ["Linen"] = unchecked((int)0xFFFAF0E6),
        ["Magenta"] = unchecked((int)0xFFFF00FF),
        ["Maroon"] = unchecked((int)0xFF800000),
        ["MediumAquamarine"] = unchecked((int)0xFF66CDAA),
        ["MediumBlue"] = unchecked((int)0xFF0000CD),
        ["MediumOrchid"] = unchecked((int)0xFFBA55D3),
        ["MediumPurple"] = unchecked((int)0xFF9370DB),
        ["MediumSeaGreen"] = unchecked((int)0xFF3CB371),
        ["MediumSlateBlue"] = unchecked((int)0xFF7B68EE),
        ["MediumSpringGreen"] = unchecked((int)0xFF00FA9A),
        ["MediumTurquoise"] = unchecked((int)0xFF48D1CC),
        ["MediumVioletRed"] = unchecked((int)0xFFC71585),
        ["MidnightBlue"] = unchecked((int)0xFF191970),
        ["MintCream"] = unchecked((int)0xFFF5FFFA),
        ["MistyRose"] = unchecked((int)0xFFFFE4E1),
        ["Moccasin"] = unchecked((int)0xFFFFE4B5),
        ["NavajoWhite"] = unchecked((int)0xFFFFDEAD),
        ["Navy"] = unchecked((int)0xFF000080),
        ["OldLace"] = unchecked((int)0xFFFDF5E6),
        ["Olive"] = unchecked((int)0xFF808000),
        ["OliveDrab"] = unchecked((int)0xFF6B8E23),
        ["Orange"] = unchecked((int)0xFFFFA500),
        ["OrangeRed"] = unchecked((int)0xFFFF4500),
        ["Orchid"] = unchecked((int)0xFFDA70D6),
        ["PaleGoldenrod"] = unchecked((int)0xFFEEE8AA),
        ["PaleGreen"] = unchecked((int)0xFF98FB98),
        ["PaleTurquoise"] = unchecked((int)0xFFAFEEEE),
        ["PaleVioletRed"] = unchecked((int)0xFFDB7093),
        ["PapayaWhip"] = unchecked((int)0xFFFFEFD5),
        ["PeachPuff"] = unchecked((int)0xFFFFDAB9),
        ["Peru"] = unchecked((int)0xFFCD853F),
        ["Pink"] = unchecked((int)0xFFFFC0CB),
        ["Plum"] = unchecked((int)0xFFDDA0DD),
        ["PowderBlue"] = unchecked((int)0xFFB0E0E6),
        ["Purple"] = unchecked((int)0xFF800080),
        ["RebeccaPurple"] = unchecked((int)0xFF663399),
        ["Red"] = unchecked((int)0xFFFF0000),
        ["RosyBrown"] = unchecked((int)0xFFBC8F8F),
        ["RoyalBlue"] = unchecked((int)0xFF4169E1),
        ["SaddleBrown"] = unchecked((int)0xFF8B4513),
        ["Salmon"] = unchecked((int)0xFFFA8072),
        ["SandyBrown"] = unchecked((int)0xFFF4A460),
        ["SeaGreen"] = unchecked((int)0xFF2E8B57),
        ["SeaShell"] = unchecked((int)0xFFFFF5EE),
        ["Sienna"] = unchecked((int)0xFFA0522D),
        ["Silver"] = unchecked((int)0xFFC0C0C0),
        ["SkyBlue"] = unchecked((int)0xFF87CEEB),
        ["SlateBlue"] = unchecked((int)0xFF6A5ACD),
        ["SlateGray"] = unchecked((int)0xFF708090),
        ["Snow"] = unchecked((int)0xFFFFFAFA),
        ["SpringGreen"] = unchecked((int)0xFF00FF7F),
        ["SteelBlue"] = unchecked((int)0xFF4682B4),
        ["Tan"] = unchecked((int)0xFFD2B48C),
        ["Teal"] = unchecked((int)0xFF008080),
        ["Thistle"] = unchecked((int)0xFFD8BFD8),
        ["Tomato"] = unchecked((int)0xFFFF6347),
        ["Transparent"] = unchecked((int)0x00FFFFFF),
        ["Turquoise"] = unchecked((int)0xFF40E0D0),
        ["Violet"] = unchecked((int)0xFFEE82EE),
        ["Wheat"] = unchecked((int)0xFFF5DEB3),
        ["White"] = unchecked((int)0xFFFFFFFF),
        ["WhiteSmoke"] = unchecked((int)0xFFF5F5F5),
        ["Yellow"] = unchecked((int)0xFFFFFF00),
        ["YellowGreen"] = unchecked((int)0xFF9ACD32),
    };
    private static string ToJsonColor(string col)
    {
        var sb = new StringBuilder(col.Length+5);
        sb.Append(col[0]);
        for(var i = 1;i<col.Length;++i)
        {
            if(char.IsUpper(col[i]))
            {
                sb.Append('-');
                sb.Append(char.ToLowerInvariant(col[i]));
            } else
            {
                sb.Append(col[i]); 
            }
        }
        return sb.ToString();
    }
    public static string GetJsonColorString(int color)
    {
        foreach(var kvp in _colors)
        {
            if(kvp.Value==color)
            {
                return ToJsonColor(kvp.Key);
            }
        }
        byte a = (byte)(color >> 24);
        byte r = (byte)((color >> 16)&0xFF);
        byte g = (byte)((color >> 8) & 0xFF);
        byte b = (byte)((color >> 0) & 0xFF);
        if (a<255)
        {
            return $"#{a:X2}{r:X2}{g:X2}{b:X2}";
        }
        return $"#{r:X2}{g:X2}{b:X2}";
    }
    public static bool TryGetColor(string str, out int color)
    {
        if (str.StartsWith("#"))
        {
            var r = byte.Parse(str.Substring(1, 2), NumberStyles.HexNumber);
            var g = byte.Parse(str.Substring(3, 2), NumberStyles.HexNumber);
            var b = byte.Parse(str.Substring(5, 2), NumberStyles.HexNumber);
            byte a = 0xFF;
            if (str.Length > 7)
            {
                var num = byte.Parse(str.Substring(7, 2), NumberStyles.HexNumber);
                a = r;
                r = g;
                g = b;
                b = num;
            }
            color = (a << 24) | (r << 16) | (g << 8) | b;
            return true;
        }
        var sb = new StringBuilder(str.Length);
        for (var i = 0; i < str.Length; i++)
        {
            if (char.IsLetterOrDigit(str[i]))
            {
                sb.Append(str[i]);
            }
        }
        // Fast path: direct lookup with case-insensitive comparison
        if (_colors.TryGetValue(sb.ToString(), out color))
            return true;


        color = default;
        return false;
    }
    public Screen()
    {
        InitializeComponent();
    }

    public Screen(IContainer container)
    {
        container.Add(this);
        InitializeComponent();
    }
    public Screen(Device? parent)
    {
        _parent = parent;
        InitializeComponent();
        
    }

    public Screen(Device? parent, IContainer container)
    {
        container.Add(this);
        _parent = parent;
        InitializeComponent();
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    private HardwareInfoCollection? _hardwareInfo;
    public HardwareInfoCollection? HardwareInfo
    {
        get
        {
            if(_parent!=null && _parent.HardwareInfo!=null)
            {
                return _parent.HardwareInfo;
            }
            return _hardwareInfo;
        }
        set
        {
            _hardwareInfo = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HardwareInfo)));
        }
    }
    private Device? _parent = null;
    public Device? Parent
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

    public void Refresh()
    {
        if (Top != null)
        {
            Top.Refresh();
        }
        if (Bottom != null)
        {
            Bottom.Refresh();
        }
    }
    
    private HardwareInfoExpression? _requires;
    public HardwareInfoExpression? Requires
    {
        get { return _requires; }
        set
        {
            if (_requires != value)
            {
                _requires = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Requires)));
            }
        }
    }
    
    private ScreenEntry? _top;
    public ScreenEntry? Top
    {
        get { return _top; }
        set {
            if (_top != value)
            {
                _top = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Top)));
            }
        }
    }
    private ScreenEntry? _bottom;
    public ScreenEntry? Bottom
    {
        get { return _bottom; }
        set
        {
            if (_bottom != value)
            {
                _bottom = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Bottom)));
            }
        }
    }
   
    public static Screen Default
    {
        get { return CreateDefault(); }
    }
    private static Screen CreateDefault()
    {
        var result = new Screen();

        var top = new ScreenEntry(result);

        var topValue1 = new ScreenValueEntry(top);
        if (TryGetColor("Green", out int topValue1Color)) { topValue1.Color = topValue1Color; }
        topValue1.HasGradient = false;
        topValue1.ValueExpression = HardwareInfoExpression.Parse("round(avg('^/coretemp/cpu/[0-9]+/core/[0-9]+/load$'))");
        topValue1.MinExpression = new HardwareInfoLiteralExpression(0);
        topValue1.MaxExpression = new HardwareInfoLiteralExpression(100);
        var topValue2 = new ScreenValueEntry(top);
        if (TryGetColor("Orange", out int topValue2Color)) { topValue2.Color = topValue2Color; }
        topValue2.HasGradient = true;
        topValue2.ValueExpression = HardwareInfoExpression.Parse("round(max('^/coretemp/cpu/[0-9]+/core/[0-9]+/temperature$'))");
        topValue2.MinExpression = new HardwareInfoLiteralExpression(0);
        topValue2.MaxExpression = HardwareInfoExpression.Parse("min('^/coretemp/cpu/[0-9]+/tjmax$')");

        
        top.Label = "CPU";
        if (TryGetColor("LightBlue", out int topLabelColor)) { top.Color = topLabelColor; }
        top.Value1 = topValue1;
        top.Value2 = topValue2;

        var bottom = new ScreenEntry(result);

        var bottomValue1 = new ScreenValueEntry(bottom);
        if (TryGetColor("White", out int bottomValue1Color)) { bottomValue1.Color = bottomValue1Color; }
        bottomValue1.HasGradient = false;
        bottomValue1.ValueExpression = HardwareInfoExpression.Parse("round(avg('^/.+/gpu/[0-9]+/load$'))");
        bottomValue1.MinExpression = new HardwareInfoLiteralExpression(0);
        bottomValue1.MaxExpression = new HardwareInfoLiteralExpression(100);
        var bottomValue2 = new ScreenValueEntry(bottom);
        if (TryGetColor("Purple", out int bottomValue2Color)) { bottomValue2.Color = bottomValue2Color; }
        bottomValue2.HasGradient = true;
        bottomValue2.ValueExpression = HardwareInfoExpression.Parse("round(max('^/.+/gpu/[0-9]+/temperature$'))");
        bottomValue2.MinExpression = new HardwareInfoLiteralExpression(0);
        bottomValue2.MaxExpression = new HardwareInfoLiteralExpression(90);

        bottom.Label = "GPU";
        if (TryGetColor("LightSalmon", out int bottomLabelColor)) { bottom.Color = bottomLabelColor; }
        bottom.Value1 = bottomValue1;
        bottom.Value2 = bottomValue2;

        
        result.Top = top;
        result.Bottom = bottom;
        result.Requires = new HardwareInfoPathExpression("/coretemp/cpu/clock");
        return result;

    }
    internal JsonObject ToJson()
    {
        if (Top == null) throw new System.InvalidOperationException("Trying to serialize when Top is null");
        if (Bottom == null) throw new System.InvalidOperationException("Trying to serialize when Bottom is null");
        var json = new JsonObject();
        json.Add("top", Top.ToJson());
        json.Add("bottom", Bottom.ToJson());
        return json;
    }
    internal static Screen FromJson(Device? parent,JsonObject json)
    {
        var result = new Screen(parent);
        if (json.TryGetValue("top", out var top))
        {
            if (top is JsonObject obj)
            {
                result.Top = ScreenEntry.FromJson(result, obj);
            }
            else
            {
                throw new ScreenParseException($"Screen \"top\" field must be a valid JSON object.", 0, 0, 0);
            }
        }
        else
        {
            throw new ScreenParseException($"Screen must have a \"top\" entry.", 0, 0, 0);
        }
        if (json.TryGetValue("bottom", out var bottom))
        {
            if (bottom is JsonObject obj)
            {
                result.Bottom = ScreenEntry.FromJson(result, obj);
            }
            else
            {
                throw new ScreenParseException($"Screen \"bottom\" field must be a valid JSON object.", 0, 0, 0);
            }
        }
        else
        {
            throw new ScreenParseException($"Screen must have a \"bottom\" entry.", 0, 0, 0);
        }

        return result;
    }
    public void WriteTo(TextWriter writer, bool minimized=false)
    {
        var json = ToJson();
        json.WriteTo(writer, minimized);
    }
    public static Screen ReadFrom(TextReader reader)
    {
        var json = JsonObject.ReadFrom(reader);
        if(json is JsonObject obj)
        {
            return Screen.FromJson(null,obj);
        }
        throw new ArgumentException("The JSON content doesn't represent a screen", nameof(reader));
    }
}
