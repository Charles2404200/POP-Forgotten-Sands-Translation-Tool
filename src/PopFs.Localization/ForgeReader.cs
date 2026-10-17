using System.Buffers.Binary;
using System.Text;

namespace PopFs.Localization;

internal static class ForgeReader
{
    private static readonly byte[] FileDataMagic = "FILEDATA"u8.ToArray();

    public static byte[] ReadEntry(string path, string entryName)
    {
        using var file = File.OpenRead(path);
        var entry = ListEntries(file).FirstOrDefault(candidate =>
            candidate.Name.Equals(entryName, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Forge entry not found: {entryName}", path);
        var payload = new byte[entry.Length];
        file.Position = entry.Offset;
        file.ReadExactly(payload);
        return payload;
    }

    public static IReadOnlyList<ForgeEntry> ListEntries(string path)
    {
        using var file = File.OpenRead(path);
        return ListEntries(file);
    }

    private static IReadOnlyList<ForgeEntry> ListEntries(FileStream file)
    {
        Span<byte> header = stackalloc byte[21];
        file.ReadExactly(header);
        if (!header[..9].SequenceEqual("scimitar\0"u8) || BinaryPrimitives.ReadInt32LittleEndian(header[9..13]) != 26)
            throw new InvalidDataException("Expected a Scimitar v26 forge.");

        var entries = new List<ForgeEntry>();
        Span<byte> record = stackalloc byte[440];
        for (long offset = 0; offset + record.Length <= file.Length; offset += 2048)
        {
            file.Position = offset;
            file.ReadExactly(record[..8]);
            if (!record[..8].SequenceEqual(FileDataMagic)) continue;

            file.ReadExactly(record[8..]);
            var nameLength = record[8..136].IndexOf((byte)0);
            if (nameLength < 0) continue;
            var name = Encoding.ASCII.GetString(record.Slice(8, nameLength));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(record[395..399]);
            var payloadOffset = offset + record.Length;
            if (length > int.MaxValue || payloadOffset + length > file.Length)
                throw new InvalidDataException($"Invalid FILEDATA length for {name}.");
            entries.Add(new ForgeEntry(name, payloadOffset, (int)length));
        }

        return entries;
    }
}

internal sealed record ForgeEntry(string Name, long Offset, int Length);
