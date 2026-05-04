namespace Espmon;

internal sealed class DevicePortEntry
{
    public string PortName { get; }
    public string[] SerialNumbers { get; }
    public string SerialNumber { get; }
    public byte[]? MacAddress { get; } 

    public Session? Session { get; }

    public DevicePortEntry(string portName, string[] serialNumbers, string serialNumber, byte[]? macAddress, Session? session)
    {
        PortName = portName;
        SerialNumbers = serialNumbers;
        SerialNumber = serialNumber;
        MacAddress = macAddress;
        Session = session;
    }
}
