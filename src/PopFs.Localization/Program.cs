using PopFs.Localization;
using System.Security.Cryptography;

if (args.Length == 2 && args[0].Equals("check-font-duplicates", StringComparison.OrdinalIgnoreCase))
{
    foreach (var entry in ForgeReader.ListEntries(args[1]))
    {
        var raw = ForgeReader.ReadEntry(args[1], entry.Name);
        if (!ScimitarData.HasMagic(raw)) continue;
        foreach (var resource in ScimitarResources.Read(ScimitarData.Read(raw)))
            if (resource.Type == 0x58E38D86 &&
                (resource.Name.StartsWith("Arial", StringComparison.Ordinal) ||
                 resource.Name.StartsWith("Sava", StringComparison.Ordinal)))
                Console.WriteLine($"{entry.Name}\t{resource.Name}\t{resource.Hash:X8}\t{resource.Data.Length}\t{Convert.ToHexString(SHA256.HashData(resource.Data.Span))}");
    }
    return 0;
}

if (args.Length == 3 && args[0].Equals("find-resource", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        foreach (var entry in ForgeReader.ListEntries(args[1]))
        {
            var raw = ForgeReader.ReadEntry(args[1], entry.Name);
            if (!ScimitarData.HasMagic(raw)) continue;
            foreach (var resource in ScimitarResources.Read(ScimitarData.Read(raw)))
                if (resource.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine($"{entry.Name}\t{resource.Type:X8}\t{resource.Name}");
        }
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); return 1; }
}

if (args.Length == 5 && args[0].Equals("extract-resource", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        var resource = ScimitarResources.Read(ScimitarData.Read(
            ForgeReader.ReadEntry(args[1], args[2]))).First(x => x.Name == args[3]);
        File.WriteAllBytes(args[4], resource.Data.ToArray());
        Console.WriteLine($"{resource.Name}: {resource.Data.Length} bytes");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); return 1; }
}

if (args.Length == 4 && args[0].Equals("check-data-roundtrip", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        var raw = ForgeReader.ReadEntry(args[1], args[2]);
        using var compressor = new Lzo1CCompressor(args[3]);
        var rebuilt = ScimitarDataRebuilder.Rebuild(raw, ScimitarData.Read(raw), compressor);
        Console.WriteLine($"Byte-for-byte identical: {raw.AsSpan().SequenceEqual(rebuilt)}; {raw.Length}/{rebuilt.Length}");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); return 1; }
}

if (args.Length == 4 && args[0].Equals("rebuild-forge", StringComparison.OrdinalIgnoreCase))
{
    try { ForgeRebuilder.Rebuild(args[1], args[2], args[3]); return 0; }
    catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); return 1; }
}

if (args.Length is 6 or 7 && args[0].Equals("import-text", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        if (args.Length == 7 && !args[6].Equals("--keep-case", StringComparison.Ordinal))
            throw new ArgumentException("The only supported import option is --keep-case.");
        var raw = ForgeReader.ReadEntry(args[1], args[2]);
        var containers = ScimitarData.Read(raw);
        var package = LocalizationPackage.Read(containers);
        if (!package.Name.Equals(args[2], StringComparison.Ordinal))
            throw new InvalidDataException($"Entry name and package name differ: {args[2]}.");
        var input = TextDumpReader.Read(args[3], package);
        var texts = args.Length == 7 ? input : input.ToDictionary(
            pair => pair.Key, pair => DisplayTextCase.Uppercase(pair.Value));
        var replacement = LocalizationPackageRebuilder.Replace(containers, texts);
        using var compressor = new Lzo1CCompressor(args[5]);
        var result = ScimitarDataRebuilder.Rebuild(raw, replacement, compressor);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[4]))!);
        File.WriteAllBytes(args[4], result);
        Console.WriteLine($"{package.Name}: {package.Entries.Count} strings" +
            (args.Length == 7 ? " (case preserved)" : " (display text uppercased)") +
            $" -> {args[4]}");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); return 1; }
}

if (args.Length == 4 && args[0].Equals("rebuild-forge-append", StringComparison.OrdinalIgnoreCase))
{
    try { ForgeRebuilder.Rebuild(args[1], args[2], args[3], true); return 0; }
    catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); return 1; }
}

if (args.Length == 5 && args[0].Equals("pack-fonts", StringComparison.OrdinalIgnoreCase))
{
    try { FontPackageBuild.Write(args[1], args[2], args[3], args[4]); return 0; }
    catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); return 1; }
}

if (args.Length == 5 && args[0].Equals("import-fonts", StringComparison.OrdinalIgnoreCase))
{
    try { FontAssetImport.Write(args[1], args[2], args[3], args[4]); return 0; }
    catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); return 1; }
}

if (args.Length == 2 && args[0].Equals("list-forge", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        foreach (var entry in ForgeReader.ListEntries(args[1]))
            Console.WriteLine($"{entry.Name}\t0x{entry.Offset:X}\t{entry.Length}");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); return 1; }
}

if (args.Length == 5 && args[0].Equals("build-fonts", StringComparison.OrdinalIgnoreCase))
{
    try { FontBuild.Write(args[1], args[2], args[3], args[4]); return 0; }
    catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); return 1; }
}

if (args.Length == 2 && args[0].Equals("check-font-roundtrip", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        var checkedCount = 0;
        foreach (var entry in ForgeReader.ListEntries(args[1]))
        {
            var raw = ForgeReader.ReadEntry(args[1], entry.Name);
            if (!ScimitarData.HasMagic(raw)) continue;
            foreach (var resource in ScimitarResources.Read(ScimitarData.Read(raw)))
            {
                if (resource.Type != 0x58E38D86 ||
                    (!resource.Name.StartsWith("Arial", StringComparison.Ordinal) &&
                     !resource.Name.StartsWith("Sava", StringComparison.Ordinal))) continue;
                var glyphs = FontDescriptor.Read(resource).Glyphs
                    .Select(g => new RasterGlyph(g.Codepoint, 0, 0, 1, 1, 0, 0, 0)).ToArray();
                var atlas = new RasterAtlas(1, 1, [255, 255, 255, 0], glyphs);
                var rebuilt = FontDescriptorRebuilder.Rebuild(resource, atlas, true);
                if (!rebuilt.AsSpan().SequenceEqual(resource.Data.Span))
                {
                    var source = resource.Data.Span;
                    var first = 0;
                    while (first < Math.Min(rebuilt.Length, source.Length) && rebuilt[first] == source[first]) first++;
                    throw new InvalidDataException($"Descriptor changed on no-op rebuild: {entry.Name}/{resource.Name}; first={first}, old={source[first]:X2}, new={rebuilt[first]:X2}, lengths={source.Length}/{rebuilt.Length}.");
                }
                checkedCount++;
            }
        }
        Console.WriteLine($"Roundtrip OK: {checkedCount} font descriptors.");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); return 1; }
}

if (args.Length == 2 && args[0].Equals("check-font-layout", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        foreach (var entry in ForgeReader.ListEntries(args[1]))
        {
            var raw = ForgeReader.ReadEntry(args[1], entry.Name);
            if (!ScimitarData.HasMagic(raw)) continue;
            foreach (var resource in ScimitarResources.Read(ScimitarData.Read(raw)))
            {
                if (resource.Type != 0x58E38D86) continue;
                var layout = FontDescriptorLayout.Read(resource);
                Console.WriteLine($"{entry.Name}\t{resource.Name}\t{layout.Count}\t{layout.GlyphOffset}");
            }
        }
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); return 1; }
}

if (args.Length == 6 && args[0].Equals("render-font", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        var atlas = FontRasterizer.Render(args[1], VietnameseGlyphs.BasicSet(),
            int.Parse(args[3]), int.Parse(args[4]), float.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture));
        PngWriter.WriteRgba(atlas.Width, atlas.Height, atlas.Rgba, args[2]);
        var stem = Path.Combine(Path.GetDirectoryName(args[2]) ?? ".",
            Path.GetFileNameWithoutExtension(args[2]));
        PngWriter.WritePreviews(new FontAtlas(stem, atlas.Width, atlas.Height, 0, atlas.Rgba),
            stem + "-gray.png", stem + "-alpha.png", stem + "-ink.png");
        Console.WriteLine($"{atlas.Glyphs.Count} glyphs -> {args[2]}");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); return 1; }
}

if (args.Length == 3 && args[0].Equals("list-resources", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        var resources = ScimitarResources.Read(ScimitarData.Read(
            ForgeReader.ReadEntry(args[1], args[2])));
        foreach (var resource in resources)
            Console.WriteLine($"{resource.Index}\t{resource.Type:X8}\t{resource.Data.Length}\t{resource.Name}");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); return 1; }
}

if (args.Length == 3 && args[0].Equals("dump-fonts", StringComparison.OrdinalIgnoreCase))
{
    try { FontDump.Write(args[1], args[2]); return 0; }
    catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); return 1; }
}

if (args.Length is < 3 or > 4 || !args[0].Equals("dump", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage:\n  popfs dump <game-directory|forge|data> <output-directory> [language]\n  popfs import-text <source.forge> <entry-name> <edited.txt> <output.data> <lzo2_w64.dll> [--keep-case]\n  popfs dump-fonts <game-directory> <output-directory>\n  popfs import-fonts <source-directory> <exported-font-directory> <output-package-directory> <lzo2_w64.dll>\n  popfs build-fonts <game-directory> <output-directory> <SVN-Arial-directory> <Grenze-directory>\n  popfs pack-fonts <game-directory> <asset-directory> <output-directory> <lzo2_w64.dll>\n  popfs rebuild-forge <source.forge> <package-directory> <output.forge>\n  popfs list-resources <forge> <entry-name>\n  popfs render-font <ttf> <output.png> <width> <height> <size>");
    return 2;
}

try
{
    var source = args[1];
    var outputDirectory = args[2];
    var language = args.Length == 4 ? args[3] : "English";
    if (language.Length == 0 || !language.All(char.IsLetter))
        throw new ArgumentException("Language must contain letters only.");

    var packages = new List<LocalizationPackage>();
    if (Path.GetExtension(source).Equals(".data", StringComparison.OrdinalIgnoreCase))
    {
        packages.Add(LocalizationPackage.Read(ScimitarData.Read(File.ReadAllBytes(source))));
    }
    else
    {
        var forge = Directory.Exists(source) ? Path.Combine(source, "DataPC_LOD.forge") : source;
        foreach (var kind in new[] { "Common", "Menu" })
        {
            var name = $"Localization{kind}Package_{language}";
            packages.Add(LocalizationPackage.Read(ScimitarData.Read(ForgeReader.ReadEntry(forge, name))));
            if (!packages[^1].Name.Equals(name, StringComparison.Ordinal))
                throw new InvalidDataException($"Wrong package in forge entry {name}.");
        }
    }

    Directory.CreateDirectory(outputDirectory);
    foreach (var package in packages)
    {
        var path = Path.Combine(outputDirectory, package.Name + ".txt");
        TextDumpWriter.Write(package, path);
        Console.WriteLine($"{package.Name}: {package.Entries.Count} strings -> {path}");
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
