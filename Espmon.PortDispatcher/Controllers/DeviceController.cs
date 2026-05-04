using System.Collections.ObjectModel;

namespace Espmon;

public class DeviceController : ControllerBase
{
    public PortController Parent { get; }
    string _name;
    public DeviceController(PortController parent, string name) : base(parent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Parent = parent;
        _name = name;
    }
    public string Name
    {
        get
        {
            return _name;
        }
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            if (!_name.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                UpdateProperty(nameof(Name), () => _name = value);
            }
        }
    }
    
    byte[] _macAddress = new byte[6];
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
                UpdateProperty(nameof(MacAddress), () => _macAddress = value);
            }
        }
    }
    
    string[] _serialNumbers = [];
    public string[] SerialNumbers
    {
        get
        {
            return _serialNumbers;
        }
        set
        {
            if (_serialNumbers != value)
            {
                UpdateProperty(nameof(SerialNumbers), () => _serialNumbers = value);
            }
        }
    }
    public ObservableCollection<string> Screens { get; } = [];

    internal JsonObject ToJson()
    {
        if (MacAddress == null) throw new System.InvalidOperationException("Trying to serialize when MAC address is null");
        var json = new JsonObject();
        if (SerialNumbers.Length > 0)
        {
            json.Add("serial_numbers", new JsonArray(SerialNumbers));
        }
        json.Add("mac", _MacToString(MacAddress));
        var screens = new JsonArray();
        for (var i = 0; i < Screens.Count; ++i)
        {
            screens.Add(Screens[i]);
        }
        json.Add("screens", screens);
        return json;
    }
    internal static DeviceController FromJson(PortController parent, string name, JsonObject json)
    {
        var result = new DeviceController(parent,name);
        if (json.TryGetValue("serial_numbers", out var serialNumbers))
        {
            if (serialNumbers is JsonArray arrSerialNumbers)
            {
                var arr = new string[arrSerialNumbers.Count];
                for (var i = 0; i < arr.Length; i++)
                {
                    if (arrSerialNumbers[i] is string sno)
                    {
                        arr[i] = sno;
                    }
                    else
                    {
                        throw new ScreenParseException("The serial number was not a valid string", 0, 0, 0);
                    }
                }
                result._serialNumbers = arr;
            }
            else
            {
                throw new ScreenParseException($"Device \"serial_numbers\" field must be an array of strings.", 0, 0, 0);
            }
        }
        if (json.TryGetValue("mac", out var mac))
        {
            if (mac is string smac)
            {
                if (smac.Length != 17)
                {
                    throw new ScreenParseException("Device \"mac\" does not indicate a MAC address", 0, 0, 0);
                }
                result._macAddress = _MacParse(smac);
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
                    }
                    else
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
    static byte[] _MacParse(string mac)
    {
        string[] parts = mac.Split(':');
        byte[] bytes = new byte[6];

        for (int i = 0; i < 6; i++)
        {
            bytes[i] = Convert.ToByte(parts[i], 16);
        }

        return bytes;
    }
    static string _MacToString(byte[] mac)
    {
        return string.Join(":", mac.Select(b => b.ToString("X2")));
    }

}
