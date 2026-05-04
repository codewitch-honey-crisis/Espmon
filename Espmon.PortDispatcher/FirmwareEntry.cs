using System.Reflection;
using System.Text;

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
    public FirmwareOffsets Offsets { get; }

    public FirmwareEntry(string displayName, string slug, FirmwareOffsets offsets)
    {
        DisplayName = displayName;
        Slug = slug;
        Offsets = offsets;
    }
    public static FirmwareEntry[] GetFirmwareEntries()
    {
        using var stm = Assembly.GetExecutingAssembly().GetManifestResourceStream("Espmon.firmware.boards.json");
        if (stm == null) throw new InvalidProgramException("The boards resource could not be found");
        var reader = new StreamReader(stm, Encoding.UTF8);
        var doc = (JsonObject?)JsonObject.ReadFrom(reader);
        if (doc == null) throw new InvalidProgramException("The boards resource is invalid");
        if (!doc.TryGetValue("boards", out var boards) || !(boards is JsonArray boardsArray))
        {
            throw new InvalidProgramException("The boards resource is invalid");
        }
        var firmwareEntrys = new List<FirmwareEntry>();
        foreach (var board in boardsArray)
        {

            if (!(board is JsonObject boardObj)) { throw new InvalidProgramException("The boards resource is invalid"); }
            if (!boardObj.TryGetValue("name", out var name) || !(name is string displayName)) { throw new InvalidProgramException("The boards resource is invalid"); }
            if (!boardObj.TryGetValue("slug", out var sluggo) || !(sluggo is string slug)) { throw new InvalidProgramException("The boards resource is invalid"); }
            if (!boardObj.TryGetValue("offsets", out var offsetso) || !(offsetso is JsonObject offsetsObj)) { throw new InvalidProgramException("The boards resource is invalid"); }
            if (!offsetsObj.TryGetValue("bootloader", out var bootloader) || !(bootloader is double bootloaderObj)) { throw new InvalidProgramException("The boards resource is invalid"); }
            if (!offsetsObj.TryGetValue("partitions", out var partitions) || !(partitions is double partitionsObj)) { throw new InvalidProgramException("The boards resource is invalid"); }
            if (!offsetsObj.TryGetValue("firmware", out var firmware) || !(firmware is double firmwareObj)) { throw new InvalidProgramException("The boards resource is invalid"); }
            var entry = new FirmwareEntry(displayName, slug, new FirmwareOffsets((uint)bootloaderObj, (uint)partitionsObj, (uint)firmwareObj));
            firmwareEntrys.Add(entry);
        }
        return firmwareEntrys.ToArray();
    }
}
