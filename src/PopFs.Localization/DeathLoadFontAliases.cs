using System.Buffers.Binary;

namespace PopFs.Localization;

// DeathLoad contains baked-in lowercase glyph references. The shared Vietnamese
// font reuses those slots, so this one screen needs lowercase aliases.
internal static class DeathLoadFontAliases
{
    private const int AtlasSize = 1024;

    // Rare Vietnamese capitals that are not used by DeathLoad's translated
    // actions. Keeping the descriptor's original glyph count is required.
    private static readonly int[] ReplacedCodes =
    [
        0x00D5, 0x0128, 0x0168, 0x1EA8, 0x1EAA, 0x1EAE, 0x1EB0,
        0x1EB2, 0x1EB4, 0x1EB6, 0x1EB8, 0x1EBA, 0x1EBC, 0x1EC4,
        0x1EC8, 0x1ECC, 0x1ECE, 0x1ED2, 0x1ED4, 0x1ED6, 0x1EE0,
        0x1EEA, 0x1EF2, 0x1EF4, 0x1EF6, 0x1EF8
    ];

    public static byte[] RestoreLowercase(ScimitarResource resource)
    {
        if (resource.Name != "Sava_POPTM_infos_Efigs+" || resource.Type != 0x58E38D86)
            throw new ArgumentException("DeathLoad aliasing requires its Sava infos descriptor.");

        var original = FontDescriptor.Read(resource).Glyphs;
        var byCode = original.ToDictionary(glyph => glyph.Codepoint);
        var indexes = original.Select((glyph, index) => (glyph.Codepoint, index))
            .ToDictionary(pair => pair.Codepoint, pair => pair.index);
        if (Enumerable.Range('a', 26).All(byCode.ContainsKey))
            return resource.Data.ToArray();
        if (Enumerable.Range('a', 26).Any(byCode.ContainsKey) ||
            ReplacedCodes.Length != 26 || ReplacedCodes.Any(code => !byCode.ContainsKey(code)))
            throw new InvalidDataException("Unexpected DeathLoad glyph set.");

        var removed = ReplacedCodes.ToHashSet();
        var layout = FontDescriptorLayout.Read(resource);
        var glyphs = original.Where(metric => !removed.Contains(metric.Codepoint))
            .Select(metric => ToRaster(metric, ReadAdvance(metric.Codepoint)))
            .ToList();
        for (var code = 'a'; code <= 'z'; code++)
        {
            var donor = byCode[char.ToUpperInvariant((char)code)];
            glyphs.Add(ToRaster(donor with { Codepoint = code }, ReadAdvance(donor.Codepoint)));
        }

        var atlas = new RasterAtlas(AtlasSize, AtlasSize, [], glyphs);
        var result = FontDescriptorRebuilder.Rebuild(resource, atlas,
            preserveOriginalAdvance: true);
        var parsed = FontDescriptor.Read(resource with { Data = result }).Glyphs;
        if (parsed.Count != original.Count ||
            !parsed.Skip(65).Take(26).Select(glyph => glyph.Codepoint)
                .SequenceEqual(Enumerable.Range('a', 26)))
            throw new InvalidDataException("DeathLoad lowercase aliases did not roundtrip.");
        return result;

        byte ReadAdvance(int code)
        {
            var index = indexes[code];
            var fixedPoint = BinaryPrimitives.ReadUInt16LittleEndian(
                resource.Data.Span.Slice(layout.AdvanceOffset + 2 + index * 2, 2));
            return checked((byte)Math.Clamp((fixedPoint + 128) / 256, 1, byte.MaxValue));
        }
    }

    private static RasterGlyph ToRaster(GlyphMetric metric, byte advance) =>
        new(metric.Codepoint,
            checked((int)MathF.Round(metric.U0 * AtlasSize)),
            checked((int)MathF.Round(metric.V0 * AtlasSize)),
            metric.Width, metric.Height, metric.OffsetX, metric.OffsetY, advance);
}
