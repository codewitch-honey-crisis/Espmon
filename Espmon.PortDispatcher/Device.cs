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
    string[] _serialNumbers = Array.Empty<string>();
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
                _serialNumbers = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SerialNumbers)));
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
        var json = new JsonObject();
        if(SerialNumbers.Length>0)
        {
            json.Add("serial_numbers", new JsonArray(SerialNumbers));
        }
        json.Add("mac", _MacToString(MacAddress));
        var screens = new JsonArray();
        for(var i = 0; i < _screens.Count;++i)
        {
            screens.Add(_screens[i]);
        }
        json.Add("screens", screens);
        return json;
    }
    internal static Device FromJson(JsonObject json)
    {
        var result = new Device();
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
                result.SerialNumbers = arr;
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
                result.MacAddress = _MacParse(smac);
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
