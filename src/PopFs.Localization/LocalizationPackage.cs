using System.Buffers.Binary;
using System.Text;

namespace PopFs.Localization;

internal sealed record LocalizationEntry(int Index, uint Id, string Text);
internal sealed record LocalizationPackage(string Name, IReadOnlyList<LocalizationEntry> Entries)
{
    private const uint PackageMarker = 0x6E3C9C6F;
    private const uint StringMarker = 0x5FC9CE4D;
    private static readonly Encoding Utf16 = new UnicodeEncoding(false, false, true);

    public static LocalizationPackage Read(IReadOnlyList<byte[]> containers)
    {
        var toc = containers[0].AsSpan();
        var data = containers[1].AsSpan();
        if (toc.Length != 14 || ReadU16(toc, 0) != 0 || ReadU16(toc, 2) != 1 ||
            ReadU32(toc, 8) != data.Length || ReadU16(toc, 12) != 0xFFFF)
            throw new InvalidDataException("Expected a single-resource localization TOC.");

        if (data.Length < 45 || ReadU32(data, 0) != PackageMarker)
            throw new InvalidDataException("Invalid localization package header.");
        var bodyLength = ReadU32(data, 4);
        var nameLength = ReadI32(data, 8);
        if (nameLength is < 1 or > 127 || 12 + nameLength + 1 > data.Length)
            throw new InvalidDataException("Invalid localization package name length.");
        var name = Encoding.ASCII.GetString(data.Slice(12, nameLength));
        if (!name.StartsWith("Localization", StringComparison.Ordinal) ||
            !name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_') ||
            data[12 + nameLength] != 0 || bodyLength != data.Length - (13 + nameLength))
            throw new InvalidDataException("Invalid localization package wrapper.");

        var position = 13 + nameLength;
        var resourceHash = ReadU32(data, position);
        if (resourceHash != ReadU32(toc, 4) || ReadU32(data, position + 4) != PackageMarker)
            throw new InvalidDataException("Localization package hash or marker mismatch.");
        position += 13; // hash, marker, locale identifier, package flag
        var count = ReadI32(data, position);
        if (count < 0 || count > 100_000)
            throw new InvalidDataException("Invalid string count.");
        position += 4;
        if (data.Slice(position, 5).IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidDataException("Unexpected localization header fields.");
        position += 5;

        var entries = new List<LocalizationEntry>(count);
        for (var index = 0; index < count; index++)
        {
            if (position + 12 > data.Length || ReadU32(data, position) != StringMarker)
                throw new InvalidDataException($"Invalid string record {index} at 0x{position:X}.");
            var id = ReadU32(data, position + 4);
            var characters = ReadI32(data, position + 8);
            if (characters < 0 || characters > (data.Length - position - 12) / 2)
                throw new InvalidDataException($"Invalid string length at record {index}.");
            position += 12;
            var text = Utf16.GetString(data.Slice(position, characters * 2));
            position += characters * 2;
            if (characters > 0)
            {
                if (position + 2 > data.Length || ReadU16(data, position) != 0)
                    throw new InvalidDataException($"Missing string terminator at record {index}.");
                position += 2;
            }
            entries.Add(new LocalizationEntry(index, id, text));

            if (index == count - 1) continue;
            if (position + 5 > data.Length || data.Slice(position, 5).IndexOfAnyExcept((byte)0) >= 0)
                throw new InvalidDataException($"Invalid separator after record {index}.");
            position += 5;
        }

        if (position != data.Length)
            throw new InvalidDataException($"Unexpected {data.Length - position} trailing bytes.");
        return new LocalizationPackage(name, entries);
    }

    private static ushort ReadU16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

    private static uint ReadU32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    private static int ReadI32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
}
