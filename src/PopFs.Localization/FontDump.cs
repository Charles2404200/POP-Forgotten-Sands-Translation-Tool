using System.Text;
using System.Security.Cryptography;

namespace PopFs.Localization;

internal static class FontDump
{
    public static void Write(string gameDirectory, string outputDirectory)
    {
        if (!Directory.Exists(gameDirectory)) throw new DirectoryNotFoundException(gameDirectory);
        var maps = new Dictionary<string, (ScimitarResource Resource, string Source)>(StringComparer.Ordinal);
        var fonts = new Dictionary<string, (ScimitarResource Resource, string Source)>(StringComparer.Ordinal);

        var bootstrapForge = Path.Combine(gameDirectory, "DataPC.forge");
        Collect(ScimitarResources.Read(ScimitarData.Read(
            ForgeReader.ReadEntry(bootstrapForge, "Game Bootstrap"))), "DataPC.forge / Game Bootstrap");

        var interfaceForge = Path.Combine(gameDirectory, "DataPC_interface.forge");
        foreach (var entry in ForgeReader.ListEntries(interfaceForge))
        {
            var raw = ForgeReader.ReadEntry(interfaceForge, entry.Name);
            if (ScimitarData.HasMagic(raw))
                Collect(ScimitarResources.Read(ScimitarData.Read(raw)),
                    $"DataPC_interface.forge / {entry.Name}");
        }

        // Interface archives contain many unrelated TextureMaps. Match their names
        // against actual FireFontDescriptors before exporting them.
        var fontMaps = maps.Where(pair =>
            pair.Key.StartsWith("Arial_POPTM_", StringComparison.Ordinal) ||
            pair.Key.StartsWith("POPJP_", StringComparison.Ordinal) ||
            pair.Key.StartsWith("MDChamGothic_", StringComparison.Ordinal) ||
            pair.Key.Contains("Sava_POPTM", StringComparison.OrdinalIgnoreCase) ||
            fonts.Keys.Any(name => pair.Key.StartsWith(name + "_", StringComparison.Ordinal)))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();

        var atlasDirectory = Path.Combine(outputDirectory, "atlases");
        var previewDirectory = Path.Combine(outputDirectory, "previews");
        var metricsDirectory = Path.Combine(outputDirectory, "metrics");
        Directory.CreateDirectory(atlasDirectory);
        Directory.CreateDirectory(previewDirectory);
        Directory.CreateDirectory(metricsDirectory);
        using var manifest = new StreamWriter(Path.Combine(outputDirectory, "fonts.tsv"), false, new UTF8Encoding(false));
        manifest.WriteLine("Kind\tName\tWidth\tHeight\tFormat\tCount\tSource\tResourceIndex");

        foreach (var (name, item) in fontMaps)
        {
            var atlas = FontAtlas.Read(item.Resource);
            atlas.WriteDds(Path.Combine(atlasDirectory, name + ".dds"));
            PngWriter.Write(atlas, Path.Combine(atlasDirectory, name + ".png"));
            PngWriter.WritePreviews(atlas, Path.Combine(previewDirectory, name + ".png"),
                Path.Combine(previewDirectory, name + "_alpha.png"),
                Path.Combine(previewDirectory, name + "_ink.png"));
            manifest.WriteLine($"Atlas\t{name}\t{atlas.Width}\t{atlas.Height}\t{atlas.Format}\t\t{item.Source}\t{item.Resource.Index}");
            Console.WriteLine($"Atlas {name}: {atlas.Width}x{atlas.Height}, format {atlas.Format}");
        }
        foreach (var (name, item) in fonts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            File.WriteAllBytes(Path.Combine(metricsDirectory, name + ".bin"), item.Resource.Data.ToArray());
            var font = FontDescriptor.Read(item.Resource);
            font.WriteTsv(Path.Combine(metricsDirectory, name + ".tsv"), item.Resource);
            manifest.WriteLine($"Descriptor\t{name}\t\t\t\t{font.Glyphs.Count}\t{item.Source}\t{item.Resource.Index}");
            Console.WriteLine($"Metrics {name}: {font.Glyphs.Count} glyphs");
        }
        using (var hashes = new StreamWriter(Path.Combine(outputDirectory, "assets.sha256.tsv"), false,
            new UTF8Encoding(false)))
        {
            hashes.WriteLine("Path\tSHA256");
            foreach (var path in Directory.GetFiles(atlasDirectory).Where(path =>
                         Path.GetExtension(path) is ".dds" or ".png")
                     .Concat(Directory.GetFiles(metricsDirectory, "*.tsv"))
                     .OrderBy(path => path, StringComparer.Ordinal))
                hashes.WriteLine($"{Path.GetRelativePath(outputDirectory, path).Replace('\\', '/')}\t" +
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        }
        Console.WriteLine($"Font dump: {fontMaps.Count} atlases, {fonts.Count} descriptors -> {outputDirectory}");

        void Collect(IReadOnlyList<ScimitarResource> resources, string source)
        {
            foreach (var resource in resources)
            {
                if (resource.Type == 0x58E38D86)
                    fonts.TryAdd(resource.Name, (resource, source));
                else if (resource.Type == 0xA2B7E917 &&
                         resource.Name.EndsWith("_Map", StringComparison.Ordinal))
                    maps.TryAdd(resource.Name, (resource, source));
            }
        }
    }
}
