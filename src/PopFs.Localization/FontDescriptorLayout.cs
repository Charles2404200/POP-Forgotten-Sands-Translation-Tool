using System.Buffers.Binary;

namespace PopFs.Localization;

// The descriptor stores sorted Unicode codepoints, fixed-point advances,
// kerning pairs, and atlas rectangles in separate arrays.
internal sealed record FontDescriptorLayout(int Body, int Count, int LookupWidth, int CountOffset,
    int SecondCountOffset, int LookupOffset, int CodepointOffset, int AdvanceOffset,
    int SixByteOffset, int ScaleOffset, int KerningOffset, int ReferenceOffset,
    int GlyphOffset, int FooterOffset)
{
    public static FontDescriptorLayout Read(ScimitarResource resource)
    {
        var data = resource.Data.Span;
        var nameLength = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        var outerBody = checked(13 + nameLength);
        var body = outerBody;
        for (var p = outerBody; p < Math.Min(outerBody + 24, data.Length - 8); p++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(data[p..]) == resource.Type)
            {
                body = p + 8;
                break;
            }
        if (body + 49 >= data.Length)
            throw new InvalidDataException($"Invalid descriptor wrapper: {resource.Name}.");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data[(body + 4)..]);
        var embeddedLength = data[body + 48];
        var secondCount = body + 49 + embeddedLength;
        if (secondCount + 2 > data.Length ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[secondCount..]) != count)
            throw new InvalidDataException($"Glyph count mismatch: {resource.Name}.");
        var lookup = secondCount + 2;
        var metrics = FontDescriptor.Read(resource).Glyphs;
        var codepoints = -1;
        foreach (var width in new[] { 2, 4 })
        {
            var candidate = lookup + count * width + width;
            if (candidate + count * 2 > data.Length) continue;
            var matches = true;
            for (var i = 0; i < count; i++)
                if (metrics[i].Codepoint != BinaryPrimitives.ReadUInt16LittleEndian(data[(candidate + i * 2)..]))
                {
                    matches = false;
                    break;
                }
            if (matches) { codepoints = candidate; break; }
        }
        if (codepoints < 0)
            throw new InvalidDataException($"Codepoint table not found: {resource.Name}.");
        var lookupWidth = (codepoints - lookup) / (count + 1);
        var advances = codepoints + count * 2;
        var sixBytes = advances + count * 2;
        var scales = sixBytes + 6;
        var kerning = scales + count * 2;
        var kernCount = BinaryPrimitives.ReadUInt16LittleEndian(data[kerning..]);
        var reference = kerning + 2 + kernCount * 6;
        var glyphs = reference;
        var pages = data[body + 3];
        for (var page = 0; page < pages; page++)
        {
            while (glyphs < data.Length && data[glyphs] != 0) glyphs++;
            glyphs++;
        }
        var footer = glyphs + count * 27;
        if (pages is < 1 or > 8 || footer > data.Length || reference >= data.Length ||
            glyphs != FindReferenceEnd(data))
            throw new InvalidDataException($"Unexpected descriptor layout: {resource.Name}; body={body}, count={count}, pages={pages}, kerning={kernCount}, ref={reference}, glyphs={glyphs}, footer={footer}, size={data.Length}.");
        for (var i = 0; i < count; i++)
            if (metrics[i].Codepoint != BinaryPrimitives.ReadUInt16LittleEndian(data[(codepoints + i * 2)..]))
                throw new InvalidDataException($"Codepoint table mismatch: {resource.Name}, glyph {i}.");
        return new FontDescriptorLayout(body, count, lookupWidth, body + 4, secondCount, lookup,
            codepoints, advances, sixBytes, scales, kerning, reference, glyphs, footer);
    }

    private static int FindReferenceEnd(ReadOnlySpan<byte> data)
    {
        var found = -1;
        for (var i = 0; i + 11 <= data.Length; i++)
            if (data[i] == '0' && data[i + 1] == 'x' && data[i + 10] == 0)
                found = i + 11;
        return found;
    }
}
