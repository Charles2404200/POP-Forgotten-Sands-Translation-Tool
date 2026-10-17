namespace PopFs.Localization;

internal static class FontPackageBuild
{
    public static void Write(string gameDirectory, string assetDirectory, string outputDirectory,
        string lzoLibrary)
    {
        Directory.CreateDirectory(outputDirectory);
        using var compressor = new Lzo1CCompressor(lzoLibrary);
        foreach (var forgeName in new[] { "DataPC.forge", "DataPC_interface.forge" })
        {
            var forge = Path.Combine(gameDirectory, forgeName);
            var entries = ForgeReader.ListEntries(forge).OrderBy(x => x.Offset).ToArray();
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                if (forgeName == "DataPC.forge" && entry.Name != "Game Bootstrap") continue;
                var raw = ForgeReader.ReadEntry(forge, entry.Name);
                if (!ScimitarData.HasMagic(raw)) continue;
                var containers = ScimitarData.Read(raw);
                var resources = ScimitarResources.Read(containers);
                var replacements = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                foreach (var resource in resources)
                {
                    var suffix = resource.Type switch
                    {
                        0x58E38D86 => ".descriptor.bin",
                        0xA2B7E917 => ".texture.bin",
                        _ => string.Empty
                    };
                    if (suffix.Length == 0) continue;
                    var path = Path.Combine(assetDirectory, resource.Name + suffix);
                    if (!File.Exists(path)) continue;
                    var fontData = File.ReadAllBytes(path);
                    if (forgeName == "DataPC_interface.forge" && entry.Name == "DeathLoad" &&
                        resource.Name == "Sava_POPTM_infos_Efigs+")
                        fontData = DeathLoadFontAliases.RestoreLowercase(resource with { Data = fontData });
                    replacements.Add(resource.Name, fontData);
                }
                if (replacements.Count == 0) continue;
                var rebuiltResources = ScimitarResources.Replace(containers, replacements);
                var data = ScimitarDataRebuilder.Rebuild(raw, rebuiltResources, compressor);
                var output = Path.Combine(outputDirectory, forgeName + "__" + entry.Name + ".data");
                File.WriteAllBytes(output, data);
                var blockStart = entry.Offset - 440;
                var nextStart = index + 1 < entries.Length ? entries[index + 1].Offset - 440 :
                    new FileInfo(forge).Length;
                var capacity = nextStart - blockStart - 441;
                var status = data.Length <= capacity || index + 1 == entries.Length ? "fits" : "OVERFLOW";
                Console.WriteLine($"{forgeName}/{entry.Name}: {replacements.Count} resources, {raw.Length} -> {data.Length} bytes, slot {capacity}, {status}");
            }
        }
    }
}
