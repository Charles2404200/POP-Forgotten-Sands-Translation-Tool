using System.Buffers.Binary;
using System.Text;

namespace PopFs.Localization;

internal static class LocalizationPackageRebuilder
{
    private const uint StringMarker = 0x5FC9CE4D;
    private static readonly Encoding Utf16 = new UnicodeEncoding(false, false, true);

    public static IReadOnlyList<byte[]> Replace(IReadOnlyList<byte[]> source,
        IReadOnlyDictionary<uint, string> texts)
    {
        var original = LocalizationPackage.Read(source);
        var data = source[1];
        var nameLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8));
        var recordsStart = checked(35 + nameLength);
        using var output = new MemoryStream(data.Length);
        output.Write(data.AsSpan(0, recordsStart));
        for (var i = 0; i < original.Entries.Count; i++)
        {
            var entry = original.Entries[i];
            var value = texts[entry.Id];
            WriteU32(output, StringMarker);
            WriteU32(output, entry.Id);
            WriteI32(output, value.Length); // UTF-16 code units, as stored by the game
            if (value.Length > 0)
            {
                output.Write(Utf16.GetBytes(value));
                WriteU16(output, 0);
            }
            if (i + 1 < original.Entries.Count) output.Write(new byte[5]);
        }

        var rebuilt = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(rebuilt.AsSpan(4),
            checked((uint)(rebuilt.Length - 13 - nameLength)));
        var toc = source[0].ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(toc.AsSpan(8), checked((uint)rebuilt.Length));
        IReadOnlyList<byte[]> result = [toc, rebuilt];
        var check = LocalizationPackage.Read(result);
        if (check.Name != original.Name || check.Entries.Count != original.Entries.Count ||
            check.Entries.Any(entry => entry.Text != texts[entry.Id]))
            throw new InvalidDataException("Localization package did not roundtrip.");
        return result;
    }

    private static void WriteU16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteI32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }
}
