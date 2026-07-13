using System.Collections.ObjectModel;

namespace Espmon;

[Flags]
public enum DeviceInputType
{
    None = 0,
    Touch = 1,
    Button = 2
}
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

    int _screenIndex = -1;
    public int ScreenIndex
    {
        get
        {
            return _screenIndex;
        }
        set
        {
            if (_screenIndex != value)
            {
                UpdateProperty(nameof(ScreenIndex), () => _screenIndex = value);
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
    public ObservableCollection<ScreenController> Screens { get; } = [];

    internal JsonObject ToJson()
    {
        if (MacAddress == null) throw new System.InvalidOperationException("Trying to serialize when MAC address is null");
        if (MacAddress.Length != 6)
        {
            throw new System.InvalidOperationException("Trying to serialize when MAC is invalid");
        }
        var nonZero = false;
        for (var i = 0;i<MacAddress.Length;++i)
        {
            if (MacAddress[i]!=0)
            {
                nonZero = true;
                break;
            }
        }
        if(!nonZero)
        {
            throw new System.InvalidOperationException("Trying to serialize when MAC is zeroed");
        }
        var json = new JsonObject();
        if (SerialNumbers.Length > 0)
        {
            json.Add("serial_numbers", new JsonArray(SerialNumbers));
        }
        json.Add("mac", _MacToString(MacAddress));
        var screens = new JsonArray();
        for (var i = 0; i < Screens.Count; ++i)
        {
            screens.Add(Screens[i].Name);
        }
        json.Add("screens", screens);
        if(ScreenIndex>0 && ScreenIndex<screens.Count)
        {
            json.Add("screen_index", _screenIndex);
        }
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
                        // silently discard screens that aren't found so we can still load the device
                        //var found = false;
                        foreach(var scr in parent.Screens)
                        {
                            if(scr.Name.Equals(sscreen, StringComparison.OrdinalIgnoreCase))
                            {
                                //found = true;
                                result.Screens.Add(scr);
                                break;
                            }
                            
                        }
                        //if(!found)
                        //{
                            // throw new ScreenParseException("A screen entry does not exist in the list of screens", 0, 0, 0);
                        //}
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
        if (json.TryGetValue("screen_index", out var screenIndex))
        {
            if (screenIndex is double sd)
            {
                var i = (int)sd;
                if (i > -1 && i < result.Screens.Count)
                {
                    result.ScreenIndex = i;
                }
            }
            else if (screenIndex is int si)
            {
                if (si > -1 && si < result.Screens.Count)
                {
                    result.ScreenIndex = si;
                }
            }
            else
            {
                throw new ScreenParseException($"Device \"screen_index\" field must be a number.", 0, 0, 0);
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
