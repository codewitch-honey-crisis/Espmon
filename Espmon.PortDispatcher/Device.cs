using HWKit;

using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Espmon;

[Flags]
public enum DeviceInputType
{
    None = 0,
    Touch = 1,
    Button = 2
}
public partial class Device : Component, INotifyPropertyChanged
{
    public Device()
    {
        InitializeComponent();
    }

    public Device(IContainer container)
    {
        container.Add(this);
        InitializeComponent();
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    private HardwareInfoCollection? _hardwareInfo = null;
    public HardwareInfoCollection? HardwareInfo
    {
        get
        {
            return _hardwareInfo;
        }
        set
        {
            _hardwareInfo = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HardwareInfo)));
        }
    }
    int _id = -1;
    public int Id
    {
        get
        {
            return _id;
        }
        set
        {
            if (_id!= value)
            {
                _id = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Id)));
            }
        }
    }
    string? _displayName = null;
    public string? DisplayName
    {
        get
        {
            return _displayName;
        }
        set
        {
            if (_displayName != value)
            {
                _displayName= value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
            }
        }
    }
    int _horizontalResolution=0;
    public int HorizontalResolution
    {
        get
        {
            return _horizontalResolution;
        }
        set
        {
            if (_horizontalResolution != value)
            {
                _horizontalResolution = value;
                PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(HorizontalResolution)));
            }
        }
    }
    int _verticalResolution=0;
    public int VerticalResolution
    {
        get
        {
            return _verticalResolution;
        }
        set
        {
            if (_verticalResolution != value)
            {
                _verticalResolution = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VerticalResolution)));
            }
        }
    }
    bool _isMonochrome=false;
    public bool IsMonochrome
    {
        get
        {
            return _isMonochrome;
        }
        set
        {
            if (_isMonochrome != value)
            {
                _isMonochrome = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMonochrome)));
            }
        }
    }
    float _dpi = 0f;
    public float Dpi
    {
        get
        {
            return _dpi;
        }
        set
        {
            if (_dpi != value)
            {
                _dpi = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Dpi)));
            }
        }
    }
    float _pixelSize = 0f;
    public float PixelSize
    {
        get
        {
            return _pixelSize;
        }
        set
        {
            if (_pixelSize != value)
            {
                _pixelSize = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PixelSize)));
            }
        }
    }
    DeviceInputType _inputType=DeviceInputType.None;
    public DeviceInputType InputType
    {
        get
        {
            return _inputType;
        }
        set
        {
            if (_inputType != value)
            {
                _inputType = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InputType)));
            }
        }
    }
    byte[] _macAddress=new byte[6];
    public byte[] MacAddress
    {
        get
        {
            return _macAddress;
        }
        set
        {
            if (_macAddress != value)
            {
                _macAddress = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MacAddress)));
            }
        }
    }
    ObservableCollection<string> _screens = new ObservableCollection<string>();
    public ObservableCollection<string> Screens 
    { 
        get {  return _screens; }
        set
        {
            if (_screens != value)
            {
                _screens = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Screens)));
            }
        }
    
    }

    internal JsonObject ToJson()
    {
        if (MacAddress== null) throw new System.InvalidOperationException("Trying to serialize when MAC address is null");
        if (Id == -1) throw new System.InvalidOperationException("Trying to serialize when id is -1");
        if (HorizontalResolution== 0) throw new System.InvalidOperationException("Trying to serialize when hres is 0");
        if (VerticalResolution== 0) throw new System.InvalidOperationException("Trying to serialize when vres is 0");
        var json = new JsonObject();
        json.Add("id", (double)Id);
        if(!string.IsNullOrWhiteSpace(DisplayName))
        {
            json.Add("display", DisplayName);
        }
        json.Add("hres", (double)HorizontalResolution);
        json.Add("vres", (double)VerticalResolution);
        json.Add("is_monochrome", IsMonochrome);
        json.Add("input_type", InputType.ToString().ToLowerInvariant());
        json.Add("mac", MacToString(MacAddress));
        var screens = new JsonArray();
        for(var i = 0; i < _screens.Count;++i)
        {
            screens.Add(_screens[i]);
        }
        json.Add("screens", screens);
        return json;
    }
    static byte[] MacParse(string mac)
    {
        string[] parts = mac.Split(':');
        byte[] bytes = new byte[6];

        for (int i = 0; i < 6; i++)
        {
            bytes[i] = Convert.ToByte(parts[i], 16);
        }

        return bytes;
    }
    static string MacToString(byte[] mac)
    {
        return string.Join(":", mac.Select(b => b.ToString("X2")));
    }
    internal static Device FromJson(JsonObject json)
    {
        var result = new Device();

        if (json.TryGetValue("id", out var id))
        {
            if (id is double did)
            {
                result.Id = (int)did;
            }
            else
            {
                throw new ScreenParseException($"Device \"id\" field must be an integer.", 0, 0, 0);
            }
        }
        else
        {
            throw new ScreenParseException($"Device must have an \"id\" field.", 0, 0, 0);
        }
        if (json.TryGetValue("display", out var display))
        {
            if (display is string sdisplay)
            {
                result.DisplayName = sdisplay;
            }
            else
            {
                throw new ScreenParseException($"Device \"display\" field must be a string.", 0, 0, 0);
            }
        }
        if (json.TryGetValue("hres", out var hres))
        {
            if (hres is double dhres)
            {
                result.HorizontalResolution = (int)dhres;
            }
            else
            {
                throw new ScreenParseException($"Device \"hres\" field must be an integer.", 0, 0, 0);
            }
        }
        else
        {
            throw new ScreenParseException($"Device must have a \"hres\" field.", 0, 0, 0);
        }
        if (json.TryGetValue("vres", out var vres))
        {
            if (vres is double dvres)
            {
                result.VerticalResolution= (int)dvres;
            }
            else
            {
                throw new ScreenParseException($"Device \"vres\" field must be an integer.", 0, 0, 0);
            }
        }
        else
        {
            throw new ScreenParseException($"Device must have a \"vres\" field.", 0, 0, 0);
        }
        if (json.TryGetValue("is_monochrome", out var ismono))
        {
            if (ismono is bool bismono)
            {
                result.IsMonochrome=bismono;
            }
            else
            {
                throw new ScreenParseException($"Device \"is_monochrome\" field must be a boolean.", 0, 0, 0);
            }
        }
        if (json.TryGetValue("input_type", out var itype))
        {
            if (itype is string sitype)
            {
                result.InputType = (DeviceInputType)Enum.Parse(typeof(DeviceInputType), sitype, true);
            }
            else
            {
                throw new ScreenParseException($"Device \"input_type\" field must be a string.", 0, 0, 0);
            }
        }
        if (json.TryGetValue("mac", out var mac))
        {
            if (mac is string smac)
            {
                if(smac.Length!=17)
                {
                    throw new ScreenParseException("Device \"mac\" does not indicate a MAC address",0,0,0);
                }
                result.MacAddress= MacParse(smac);
            }
            else
            {
                throw new ScreenParseException($"Device \"vres\" field must be an integer.", 0, 0, 0);
            }
        }
        else
        {
            throw new ScreenParseException($"Device must have a \"vres\" field.", 0, 0, 0);
        }
        if (json.TryGetValue("screens", out var screens))
        {
            if (screens is JsonArray ascreens)
            {
                for (var i = 0; i < ascreens.Count; ++i)
                {
                    if (ascreens[i] is string sscreen)
                    {
                        result.Screens.Add(sscreen);
                    } else
                    {
                        throw new ScreenParseException("The screen was not a valid string", 0, 0, 0);
                    }
                }
            }
            else
            {
                throw new ScreenParseException($"Device \"screens\" field must be a string array.", 0, 0, 0);
            }
        }
        return result;
    }
    public void WriteTo(TextWriter writer, bool minimized = false)
    {
        var json = ToJson();
        json.WriteTo(writer, minimized);
    }
    public static Device ReadFrom(TextReader reader)
    {
        var json = JsonObject.ReadFrom(reader);
        if (json is JsonObject obj)
        {
            return Device.FromJson(obj);
        }
        throw new ArgumentException("The JSON content doesn't represent a device", nameof(reader));
    }
}
