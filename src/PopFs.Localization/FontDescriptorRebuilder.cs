using System.Buffers.Binary;

namespace PopFs.Localization;

internal static class FontDescriptorRebuilder
{
    public static byte[] Rebuild(ScimitarResource source, RasterAtlas atlas,
        bool keepOriginalMetrics = false, bool preserveOriginalAdvance = false)
    {
        var layout = FontDescriptorLayout.Read(source);
        var original = source.Data.Span;
        var oldGlyphs = FontDescriptor.Read(source).Glyphs.ToDictionary(g => g.Codepoint);
        var oldIndexes = FontDescriptor.Read(source).Glyphs.Select((g, i) => (g, i))
            .ToDictionary(x => x.g.Codepoint, x => x.i);
        var glyphs = atlas.Glyphs.OrderBy(g => g.Codepoint).ToArray();
        if (glyphs.Length > ushort.MaxValue)
            throw new InvalidDataException("Too many glyphs for FireFontDescriptor.");
        using var stream = new MemoryStream(original.Length + Math.Max(0, glyphs.Length - layout.Count) * 40);
        stream.Write(original[..layout.CountOffset]);
        WriteU16(stream, checked((ushort)glyphs.Length));
        stream.Write(original[(layout.CountOffset + 2)..layout.SecondCountOffset]);
        WriteU16(stream, checked((ushort)glyphs.Length));
        stream.Write(new byte[glyphs.Length * layout.LookupWidth]);
        if (layout.LookupWidth == 2) WriteU16(stream, checked((ushort)((glyphs.Length + 1) * 2)));
        else WriteU32(stream, checked((uint)((glyphs.Length + 1) * 4)));
        foreach (var glyph in glyphs) WriteU16(stream, checked((ushort)glyph.Codepoint));
        stream.Write(original.Slice(layout.AdvanceOffset, 2)); // leading entry
        foreach (var glyph in glyphs)
        {
            ushort advance;
            if ((keepOriginalMetrics || preserveOriginalAdvance) &&
                OriginalMetricIndex(glyph.Codepoint, out var oldIndex))
                advance = BinaryPrimitives.ReadUInt16LittleEndian(
                    original[(layout.AdvanceOffset + 2 + oldIndex * 2)..]);
            else advance = checked((ushort)Math.Clamp(glyph.Advance * 256, 0, ushort.MaxValue));
            WriteU16(stream, advance);
        }
        stream.Write(original.Slice(layout.SixByteOffset + 2, 4));
        foreach (var glyph in glyphs)
        {
            if ((keepOriginalMetrics || preserveOriginalAdvance) &&
                OriginalMetricIndex(glyph.Codepoint, out var oldIndex))
                stream.Write(original.Slice(layout.ScaleOffset + oldIndex * 2, 2));
            else WriteU16(stream, 8);
        }
        stream.Write(original[layout.KerningOffset..layout.GlyphOffset]);
        var baseline = atlas.Glyphs.FirstOrDefault(g => g.Codepoint == 'A')?.OffsetY ?? 0;
        var oldBaseline = oldGlyphs.TryGetValue('A', out var oldA) ? oldA.OffsetY : (short)0;
        foreach (var glyph in glyphs)
        {
            if (keepOriginalMetrics && oldIndexes.TryGetValue(glyph.Codepoint, out var oldIndex))
            {
                stream.Write(original.Slice(layout.GlyphOffset + oldIndex * 27, 27));
                continue;
            }
            var metric = new byte[27];
            BinaryPrimitives.WriteUInt16LittleEndian(metric, checked((ushort)glyph.Codepoint));
            metric[2] = 0;
            PutF32(metric, 3, (float)glyph.X / atlas.Width);
            PutF32(metric, 7, (float)glyph.Y / atlas.Height);
            PutF32(metric, 11, (float)(glyph.X + glyph.Width) / atlas.Width);
            PutF32(metric, 15, (float)(glyph.Y + glyph.Height) / atlas.Height);
            PutI16(metric, 19, glyph.OffsetX);
            PutI16(metric, 21, checked((short)(oldBaseline + glyph.OffsetY - baseline)));
            PutI16(metric, 23, checked((short)glyph.Width));
            PutI16(metric, 25, checked((short)glyph.Height));
            stream.Write(metric);
        }
        stream.Write(original[layout.FooterOffset..]);
        var result = stream.ToArray();
        var delta = result.Length - original.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4),
            checked(BinaryPrimitives.ReadUInt32LittleEndian(original[4..]) + (uint)delta));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(layout.Body - 4),
            checked(BinaryPrimitives.ReadUInt32LittleEndian(original[(layout.Body - 4)..]) + (uint)delta));
        var check = new ScimitarResource(source.Index, source.Hash, source.Type, source.Name, result);
        var parsed = FontDescriptor.Read(check);
        if (!parsed.Glyphs.Select(g => g.Codepoint).SequenceEqual(glyphs.Select(g => g.Codepoint)))
            throw new InvalidDataException($"Glyph roundtrip failed: {source.Name}.");
        _ = FontDescriptorLayout.Read(check);
        return result;

        bool OriginalMetricIndex(int codepoint, out int index)
        {
            if (oldIndexes.TryGetValue(codepoint, out index)) return true;
            var baseLetter = codepoint == 'Đ' ? 'D' :
                char.ConvertFromUtf32(codepoint).Normalize(System.Text.NormalizationForm.FormD)[0];
            return oldIndexes.TryGetValue(baseLetter, out index);
        }
    }

    private static void WriteU16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void PutF32(byte[] buffer, int offset, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), value);
    private static void PutI16(byte[] buffer, int offset, short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset), value);
}
