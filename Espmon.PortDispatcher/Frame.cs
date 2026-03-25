namespace Espmon;

public readonly struct Frame
{
    public byte Cmd { get; }
    public byte[] Payload { get; }
    public Frame(byte cmd, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload, nameof(payload));
        Cmd = cmd;
        Payload = payload;
    }
}