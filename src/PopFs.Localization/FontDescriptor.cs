using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace PopFs.Localization;

internal sealed record GlyphMetric(int Codepoint, int AtlasPage, float U0, float V0, float U1, float V1,
    short OffsetX, short OffsetY, short Width, short Height);

internal sealed record FontDescriptor(string Name, IReadOnlyList<GlyphMetric> Glyphs)
{
    public static FontDescriptor Read(ScimitarResource resource)
    {
        if (resource.Type != 0x58E38D86) throw new InvalidDataException("Not a FireFontDescriptor.");
        var data = resource.Data.Span;
        var marker = FindAtlasReference(data);
        var records = marker + 11; // ASCII "0x" + eight hex digits + NUL
        int count;
        try { count = FindGlyphCount(data, records); }
        catch (InvalidDataException ex) { throw new InvalidDataException($"{resource.Name}: {ex.Message} (records={records}, size={data.Length})", ex); }
        var glyphs = new List<GlyphMetric>(count);
        for (var i = 0; i < count; i++)
        {
            var p = records + 27 * i;
            var codepoint = BinaryPrimitives.ReadUInt16LittleEndian(data[p..]);
            glyphs.Add(new GlyphMetric(codepoint, data[p + 2],
                BitConverter.ToSingle(data[(p + 3)..]), BitConverter.ToSingle(data[(p + 7)..]),
                BitConverter.ToSingle(data[(p + 11)..]), BitConverter.ToSingle(data[(p + 15)..]),
                BinaryPrimitives.ReadInt16LittleEndian(data[(p + 19)..]),
                BinaryPrimitives.ReadInt16LittleEndian(data[(p + 21)..]),
                BinaryPrimitives.ReadInt16LittleEndian(data[(p + 23)..]),
                BinaryPrimitives.ReadInt16LittleEndian(data[(p + 25)..])));
        }
        return new FontDescriptor(resource.Name, glyphs);
    }

    public void WriteTsv(string path, ScimitarResource resource)
    {
        var layout = FontDescriptorLayout.Read(resource);
        if (layout.Count != Glyphs.Count)
            throw new InvalidDataException($"Glyph count changed while exporting {Name}.");
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("Codepoint\tAtlasPage\tU0\tV0\tU1\tV1\tRotated\tOffsetX\tOffsetY\tWidth\tHeight\tAdvance");
        for (var i = 0; i < Glyphs.Count; i++)
        {
            var g = Glyphs[i];
            var fixedAdvance = BinaryPrimitives.ReadUInt16LittleEndian(
                resource.Data.Span.Slice(layout.AdvanceOffset + 2 + i * 2, 2));
            var advance = (fixedAdvance / 256m).ToString(CultureInfo.InvariantCulture);
            writer.WriteLine(FormattableString.Invariant(
                $"U+{g.Codepoint:X4}\t{g.AtlasPage}\t{g.U0:R}\t{g.V0:R}\t{g.U1:R}\t{g.V1:R}\t{(g.U1 < g.U0 ? 1 : 0)}\t{g.OffsetX}\t{g.OffsetY}\t{g.Width}\t{g.Height}\t{advance}"));
        }
    }

    private static int FindAtlasReference(ReadOnlySpan<byte> data)
    {
        var found = -1;
        for (var i = 0; i + 11 <= data.Length; i++)
        {
            if (data[i] != '0' || data[i + 1] != 'x' || data[i + 10] != 0) continue;
            var valid = true;
            for (var j = 2; j < 10; j++)
                valid &= (data[i + j] is >= (byte)'0' and <= (byte)'9') ||
                         (data[i + j] is >= (byte)'A' and <= (byte)'F') ||
                         (data[i + j] is >= (byte)'a' and <= (byte)'f');
            if (valid) found = i;
        }
        if (found >= 0) return found;
        throw new InvalidDataException("Font atlas reference not found.");
    }

    private static int FindGlyphCount(ReadOnlySpan<byte> data, int records)
    {
        // The embedded font name is a one-byte length including its NUL,
        // immediately followed by the glyph count (u16).
        for (var i = 60; i < Math.Min(256, records - 4); i++)
        {
            var length = data[i];
            if (length is < 3 or > 63 || i + length + 2 >= records) continue;
            var name = data.Slice(i + 1, length - 1);
            if (data[i + length] != 0 || !name.ToArray().All(c =>
                    c is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or
                         >= (byte)'0' and <= (byte)'9' or (byte)'_' or (byte)'+' or (byte)'-'))
                continue;
            var count = BinaryPrimitives.ReadUInt16LittleEndian(data[(i + length + 1)..]);
            if (count == 0 || count > 4096 || records + count * 27 > data.Length) continue;
            var firstPage = data[records + 2];
            if (firstPage < 16) return count;
        }
        throw new InvalidDataException("Font glyph count not found.");
    }
}
