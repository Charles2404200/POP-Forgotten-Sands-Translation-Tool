using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using SkiaSharp;

namespace PopFs.Localization;

internal static class FontAssetImport
{
    public static void Write(string sourceDirectory, string assetDirectory, string outputDirectory,
        string lzoLibrary)
    {
        var hashes = ReadHashes(assetDirectory);
        Directory.CreateDirectory(outputDirectory);
        using var compressor = new Lzo1CCompressor(lzoLibrary);
        var total = 0;
        foreach (var forgeName in new[] { "DataPC.forge", "DataPC_interface.forge" })
        {
            var forge = Path.Combine(sourceDirectory, forgeName);
            foreach (var entry in ForgeReader.ListEntries(forge))
            {
                if (forgeName == "DataPC.forge" && entry.Name != "Game Bootstrap") continue;
                var raw = ForgeReader.ReadEntry(forge, entry.Name);
                if (!ScimitarData.HasMagic(raw)) continue;
                var containers = ScimitarData.Read(raw);
                var replacements = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                foreach (var resource in ScimitarResources.Read(containers))
                {
                    if (resource.Type == 0xA2B7E917)
                    {
                        var dds = $"atlases/{resource.Name}.dds";
                        var png = $"atlases/{resource.Name}.png";
                        var ddsChanged = Changed(dds);
                        var pngChanged = Changed(png);
                        if (ddsChanged && pngChanged)
                            throw new InvalidDataException($"Edit either PNG or DDS, not both: {resource.Name}.");
                        if (ddsChanged || pngChanged)
                            replacements.Add(resource.Name, ImportAtlas(resource,
                                Path.Combine(assetDirectory, (pngChanged ? png : dds).Replace('/',
                                    Path.DirectorySeparatorChar)), pngChanged));
                    }
                    else if (resource.Type == 0x58E38D86)
                    {
                        var metric = $"metrics/{resource.Name}.tsv";
                        if (Changed(metric))
                        {
                            var fontData = ImportMetrics(resource,
                                Path.Combine(assetDirectory, metric.Replace('/', Path.DirectorySeparatorChar)));
                            if (forgeName == "DataPC_interface.forge" && entry.Name == "DeathLoad" &&
                                resource.Name == "Sava_POPTM_infos_Efigs+")
                                fontData = DeathLoadFontAliases.RestoreLowercase(resource with { Data = fontData });
                            replacements.Add(resource.Name, fontData);
                        }
                    }
                }
                if (replacements.Count == 0) continue;
                var rebuilt = ScimitarResources.Replace(containers, replacements);
                var data = ScimitarDataRebuilder.Rebuild(raw, rebuilt, compressor);
                var output = Path.Combine(outputDirectory, forgeName + "__" + entry.Name + ".data");
                File.WriteAllBytes(output, data);
                Console.WriteLine($"{forgeName}/{entry.Name}: {replacements.Count} font resources -> {output}");
                total += replacements.Count;
            }
        }
        Console.WriteLine($"Imported {total} edited atlas/metric resources.");

        bool Changed(string relative)
        {
            if (!hashes.TryGetValue(relative, out var original)) return false;
            var path = Path.Combine(assetDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) throw new FileNotFoundException($"Exported font asset is missing: {relative}", path);
            var current = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            return !current.Equals(original, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, string> ReadHashes(string directory)
    {
        var path = Path.Combine(directory, "assets.sha256.tsv");
        if (!File.Exists(path)) throw new FileNotFoundException(
            "Font export manifest is missing. Export fonts with this version of the tool first.", path);
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0 || lines[0] != "Path\tSHA256")
            throw new InvalidDataException("Invalid font asset manifest.");
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(1))
        {
            var fields = line.Split('\t');
            if (fields.Length != 2 || fields[1].Length != 64 || !hashes.TryAdd(fields[0], fields[1]))
                throw new InvalidDataException($"Invalid font asset manifest row: {line}");
        }
        return hashes;
    }

    private static byte[] ImportAtlas(ScimitarResource resource, string path, bool isPng)
    {
        var atlas = FontAtlas.Read(resource);
        byte[] pixels;
        if (isPng)
        {
            using var bitmap = SKBitmap.Decode(path) ?? throw new InvalidDataException($"Cannot read PNG: {path}");
            if (bitmap.Width != atlas.Width || bitmap.Height != atlas.Height)
                throw new InvalidDataException($"Atlas dimensions changed: {resource.Name}.");
            var rgba = new byte[checked(atlas.Width * atlas.Height * 4)];
            for (var y = 0; y < atlas.Height; y++)
            for (var x = 0; x < atlas.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                var offset = (y * atlas.Width + x) * 4;
                rgba[offset] = color.Red;
                rgba[offset + 1] = color.Green;
                rgba[offset + 2] = color.Blue;
                rgba[offset + 3] = color.Alpha;
            }
            if (atlas.Format == 0) pixels = rgba;
            else
            {
                var encoder = new BcEncoder(atlas.Format == 4 ? CompressionFormat.Bc2 : CompressionFormat.Bc3);
                encoder.OutputOptions.GenerateMipMaps = false;
                encoder.OutputOptions.Quality = CompressionQuality.BestQuality;
                var encoded = encoder.EncodeToRawBytes(rgba, atlas.Width, atlas.Height, PixelFormat.Rgba32);
                if (encoded.Length != 1) throw new InvalidDataException("Unexpected PNG mip count.");
                pixels = encoded[0];
            }
        }
        else
        {
            var dds = File.ReadAllBytes(path);
            if (dds.Length != 128 + atlas.Pixels.Length || !dds.AsSpan(0, 4).SequenceEqual("DDS "u8) ||
                BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(12)) != atlas.Height ||
                BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(16)) != atlas.Width ||
                BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(84)) !=
                    (atlas.Format == 4 ? 0x33545844u : atlas.Format == 5 ? 0x35545844u : 0u))
                throw new InvalidDataException($"DDS dimensions, format or length changed: {resource.Name}.");
            pixels = dds[128..];
        }
        if (pixels.Length != atlas.Pixels.Length)
            throw new InvalidDataException($"Atlas pixel size changed: {resource.Name}.");
        var result = resource.Data.ToArray();
        pixels.CopyTo(result.AsSpan(result.Length - 60 - pixels.Length, pixels.Length));
        var check = FontAtlas.Read(resource with { Data = result });
        if (!check.Pixels.AsSpan().SequenceEqual(pixels))
            throw new InvalidDataException($"Atlas verification failed: {resource.Name}.");
        return result;
    }

    private static byte[] ImportMetrics(ScimitarResource resource, string path)
    {
        var layout = FontDescriptorLayout.Read(resource);
        var lines = File.ReadAllLines(path);
        const string header =
            "Codepoint\tAtlasPage\tU0\tV0\tU1\tV1\tRotated\tOffsetX\tOffsetY\tWidth\tHeight";
        var hasAdvance = lines.Length > 0 && lines[0] == header + "\tAdvance";
        if (lines.Length != layout.Count + 1 || (!hasAdvance && lines[0] != header))
            throw new InvalidDataException($"Metric row count or header changed: {resource.Name}.");
        var result = resource.Data.ToArray();
        var pages = result[layout.Body + 3];
        var previousCodepoint = -1;
        result.AsSpan(layout.LookupOffset, layout.Count * layout.LookupWidth).Clear();
        for (var i = 0; i < layout.Count; i++)
        {
            var fields = lines[i + 1].Split('\t');
            if (fields.Length != (hasAdvance ? 12 : 11) ||
                !fields[0].StartsWith("U+", StringComparison.Ordinal) ||
                !int.TryParse(fields[0].AsSpan(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var codepoint) ||
                codepoint is < 1 or > ushort.MaxValue || codepoint <= previousCodepoint)
                throw new InvalidDataException($"Invalid or unsorted glyph at {resource.Name} row {i + 2}.");
            previousCodepoint = codepoint;
            var page = byte.Parse(fields[1], CultureInfo.InvariantCulture);
            var u0 = float.Parse(fields[2], CultureInfo.InvariantCulture);
            var v0 = float.Parse(fields[3], CultureInfo.InvariantCulture);
            var u1 = float.Parse(fields[4], CultureInfo.InvariantCulture);
            var v1 = float.Parse(fields[5], CultureInfo.InvariantCulture);
            var rotated = int.Parse(fields[6], CultureInfo.InvariantCulture);
            var offsetX = short.Parse(fields[7], CultureInfo.InvariantCulture);
            var offsetY = short.Parse(fields[8], CultureInfo.InvariantCulture);
            var width = short.Parse(fields[9], CultureInfo.InvariantCulture);
            var height = short.Parse(fields[10], CultureInfo.InvariantCulture);
            if (hasAdvance)
            {
                var advance = decimal.Parse(fields[11], NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture) * 256m;
                if (advance < 0 || advance > ushort.MaxValue ||
                    advance != decimal.Truncate(advance))
                    throw new InvalidDataException($"Invalid advance at {resource.Name} row {i + 2}.");
                BinaryPrimitives.WriteUInt16LittleEndian(
                    result.AsSpan(layout.AdvanceOffset + 2 + i * 2), (ushort)advance);
            }
            if (page >= pages || !ValidUv(u0) || !ValidUv(v0) || !ValidUv(u1) ||
                !ValidUv(v1) || rotated != (u1 < u0 ? 1 : 0) || width < 0 || height < 0)
                throw new InvalidDataException($"Invalid UV or size at {resource.Name} row {i + 2}.");
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(layout.CodepointOffset + i * 2),
                (ushort)codepoint);
            var glyph = result.AsSpan(layout.GlyphOffset + i * 27, 27);
            BinaryPrimitives.WriteUInt16LittleEndian(glyph, (ushort)codepoint);
            glyph[2] = page;
            BinaryPrimitives.WriteSingleLittleEndian(glyph[3..], u0);
            BinaryPrimitives.WriteSingleLittleEndian(glyph[7..], v0);
            BinaryPrimitives.WriteSingleLittleEndian(glyph[11..], u1);
            BinaryPrimitives.WriteSingleLittleEndian(glyph[15..], v1);
            BinaryPrimitives.WriteInt16LittleEndian(glyph[19..], offsetX);
            BinaryPrimitives.WriteInt16LittleEndian(glyph[21..], offsetY);
            BinaryPrimitives.WriteInt16LittleEndian(glyph[23..], width);
            BinaryPrimitives.WriteInt16LittleEndian(glyph[25..], height);
        }
        var checkedResource = resource with { Data = result };
        _ = FontDescriptorLayout.Read(checkedResource);
        return result;
    }

    private static bool ValidUv(float value) => float.IsFinite(value) && value is >= 0 and <= 1;
}
