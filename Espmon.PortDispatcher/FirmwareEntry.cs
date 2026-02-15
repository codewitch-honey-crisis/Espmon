namespace Espmon;

public struct FirmwareOffsets
{
    public uint Bootloader { get; }
    public uint Partitiions { get; }
    public uint Firmware { get; }

    public FirmwareOffsets(uint bootloader, uint partitiions,uint firmware)
    {
        Bootloader = bootloader;
        Partitiions = partitiions;
        Firmware = firmware;
    }
}
public sealed class FirmwareEntry
{
    public string DisplayName { get;  }
    public string Slug { get; }
    public FirmwareOffsets Offset { get; }

    public FirmwareEntry(string displayName, string slug, FirmwareOffsets offsets)
    {
        DisplayName = displayName;
        Slug = slug;
        Offset = offsets;
    }
}
