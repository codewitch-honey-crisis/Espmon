namespace Espmon;

internal sealed class DevicePortEntry
{
    public string PortName { get; }
    public string[] SerialNumbers { get; }
    public byte[] MacAddress { get; }

    public DevicePortEntry(string portName, string[] serialNumbers, byte[] macAddress)
    {
        PortName = portName;
        SerialNumbers = serialNumbers;
        MacAddress = macAddress;
    }
}
