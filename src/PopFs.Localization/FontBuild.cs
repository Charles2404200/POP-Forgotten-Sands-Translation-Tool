namespace PopFs.Localization;

internal static class FontBuild
{
    public static void Write(string gameDirectory, string outputDirectory, string arialDirectory,
        string grenzeDirectory)
    {
        const string keyLabels = "Sava_POPTM_Keylabels_Efigs+";
        var atlasSources = new Dictionary<string, ScimitarResource>(StringComparer.Ordinal);
        var descriptorSources = new Dictionary<string, ScimitarResource>(StringComparer.Ordinal);
        Collect(Path.Combine(gameDirectory, "DataPC.forge"), "Game Bootstrap");
        var interfaceForge = Path.Combine(gameDirectory, "DataPC_interface.forge");
        foreach (var entry in ForgeReader.ListEntries(interfaceForge)) Collect(interfaceForge, entry.Name);

        // Key labels contain the game's keyboard symbols and must retain their
        // original codepoint map. Prefer the original archives when rebuilding
        // from a game installation that already has translated fonts.
        var backup = Path.Combine(gameDirectory, "backup");
        if (File.Exists(Path.Combine(backup, "DataPC.forge")) &&
            File.Exists(Path.Combine(backup, "DataPC_interface.forge")))
        {
            var originalMaps = ScimitarResources.Read(ScimitarData.Read(ForgeReader.ReadEntry(
                Path.Combine(backup, "DataPC.forge"), "Game Bootstrap")));
            var originalFonts = ScimitarResources.Read(ScimitarData.Read(ForgeReader.ReadEntry(
                Path.Combine(backup, "DataPC_interface.forge"), "POPFS_MainMenu")));
            atlasSources[keyLabels + "_1_Map"] = originalMaps.Single(resource =>
                resource.Name == keyLabels + "_1_Map" && resource.Type == 0xA2B7E917);
            descriptorSources[keyLabels] = originalFonts.Single(resource =>
                resource.Name == keyLabels && resource.Type == 0x58E38D86);
        }

        Directory.CreateDirectory(outputDirectory);
        var regular = Path.Combine(arialDirectory, "SVN-Arial Regular.ttf");
        var bold = Path.Combine(arialDirectory, "SVN-Arial Bold.ttf");
        var grenze = Path.Combine(grenzeDirectory, "static", "Grenze-Regular.ttf");
        using var manifest = new StreamWriter(Path.Combine(outputDirectory, "fonts-built.tsv"));
        manifest.WriteLine("Descriptor\tAtlas\tFont\tGlyphs\tPixelSize\tWidth\tHeight\tFormat");
        foreach (var source in descriptorSources.Values.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            var atlasName = source.Name + "_1_Map";
            if (!atlasSources.TryGetValue(atlasName, out var texture))
                throw new InvalidDataException($"Missing atlas for {source.Name}.");
            var target = FontAtlas.Read(texture);
            if (source.Name == keyLabels)
            {
                var keyCodes = FontDescriptor.Read(source).Glyphs
                    .Select(glyph => glyph.Codepoint).ToHashSet();
                if (Enumerable.Range('a', 26).Any(code => !keyCodes.Contains(code)))
                    throw new InvalidDataException("Original key label font is required in the game backup.");
                File.WriteAllBytes(Path.Combine(outputDirectory, source.Name + ".descriptor.bin"),
                    source.Data.ToArray());
                File.WriteAllBytes(Path.Combine(outputDirectory, atlasName + ".texture.bin"),
                    texture.Data.ToArray());
                FontDescriptor.Read(source).WriteTsv(
                    Path.Combine(outputDirectory, source.Name + ".metrics.tsv"), source);
                PngWriter.Write(target, Path.Combine(outputDirectory, atlasName + ".png"));
                PngWriter.WritePreviews(target,
                    Path.Combine(outputDirectory, atlasName + "-gray.png"),
                    Path.Combine(outputDirectory, atlasName + "-alpha.png"),
                    Path.Combine(outputDirectory, atlasName + "-ink.png"));
                manifest.WriteLine($"{source.Name}\t{atlasName}\toriginal\t{keyCodes.Count}\t\t{target.Width}\t{target.Height}\t{target.Format}");
                Console.WriteLine($"{source.Name}: original key label font preserved");
                continue;
            }
            var font = source.Name.StartsWith("Arial_bold", StringComparison.Ordinal) ? bold :
                source.Name.StartsWith("Arial", StringComparison.Ordinal) ? regular : grenze;
            var style = source.Name switch
            {
                "Arial_POPTM_InputText_Default" => FontVisualStyle.CyanGlow,
                "Sava_POPTM_EvoNum_NumericFont" => FontVisualStyle.GoldGlow,
                "Sava_POPTM_Keylabels_Efigs+" => FontVisualStyle.KeyLabel,
                "Sava_POPTM_infosBlur_Efigs+" => FontVisualStyle.Soft,
                "Sava_POPTM_infos_Efigs+" => FontVisualStyle.Embossed,
                _ => FontVisualStyle.Plain
            };
            var originalCodes = FontDescriptor.Read(source).Glyphs.Select(x => x.Codepoint).ToArray();
            var plan = source.Name.Contains("NumericFont", StringComparison.Ordinal) ?
                FontGlyphPlan.Create(originalCodes, Array.Empty<int>()) :
                FontGlyphPlan.Create(originalCodes, VietnameseGlyphs.UppercaseSet());
            var codes = plan.Codes;
            using (var mapping = new StreamWriter(Path.Combine(outputDirectory,
                source.Name + ".replacements.tsv")))
            {
                mapping.WriteLine("Replaced\tAdded");
                foreach (var (oldCode, newCode) in plan.Replacements)
                    mapping.WriteLine($"U+{oldCode:X4}\tU+{newCode:X4}");
            }
            // Match the source atlas's visible ink height, not its glyph cell.
            // The original cells include substantial transparent padding.
            var initialSize = source.Name switch
            {
                "Arial_POPTM_InputText_Default" => 26,
                "Sava_POPTM_EvoNum_NumericFont" => 44,
                "Sava_POPTM_Keylabels_Efigs+" => 18,
                "Sava_POPTM_infosBlur_Efigs+" or "Sava_POPTM_infos_Efigs+" => 42,
                _ => 32
            };
            RasterAtlas? atlas = null;
            var selectedSize = 0;
            for (var size = initialSize; size >= 14; size -= 2)
            {
                try
                {
                    atlas = FontRasterizer.Render(font, codes, target.Width, target.Height, size,
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                            "Fonts", font == grenze ? "segoeui.ttf" : "arial.ttf"), style,
                        source.Name.Contains("infos", StringComparison.Ordinal) ? 0.9f : 1f);
                    selectedSize = size;
                    break;
                }
                catch (InvalidDataException ex) when (ex.Message.Contains("does not fit", StringComparison.Ordinal)) { }
            }
            if (atlas is null) throw new InvalidDataException($"Cannot fit glyphs in {atlasName}.");
            // The original advances were measured for another typeface and
            // produce irregular gaps with these replacement glyphs. A steady
            // advance keeps the uppercase menu and subtitles legible.
            var spacing = source.Name.Contains("infos", StringComparison.Ordinal) ? selectedSize :
                selectedSize + (selectedSize >= 26 ? 4 : 2);
            var infosFont = source.Name is "Sava_POPTM_infos_Efigs+" or
                "Sava_POPTM_infosBlur_Efigs+";
            var spacingAtlas = atlas with { Glyphs = atlas.Glyphs
                .Select(glyph => glyph with
                {
                    Advance = checked((byte)(spacing + (infosFont ? glyph.Codepoint switch
                    {
                        0x1EDA => 6,
                        0x0043 or 0x01A0 => 3,
                        0x00CC or 0x00CD => 4,
                        _ => 0
                    } : 0))),
                    // Position the infos glyphs without changing atlas pixels.
                    // Accented I also needs room before the following glyph.
                    OffsetX = checked((short)(glyph.OffsetX + (infosFont ? glyph.Codepoint switch
                    {
                        0x0049 => 3,                // Plain I: a small shift from the source position.
                        0x0043 => 3,                // C: add left space without reducing right space.
                        0x01A0 => 3,               // O with horn: give the preceding glyph room.
                        0x1EDA => 6,               // O with horn and acute needs more left space.
                        0x00CD => 11,               // I with acute: balance left and right spacing.
                        0x00CC => 9,                // I with grave: balance left and right spacing.
                        0x1EBE or 0x1EC2 => -3,     // E with acute/hook: balanced against the preceding glyph.
                        _ => 0
                    } : 0)))
                })
                .ToArray() };
            var descriptor = FontDescriptorRebuilder.Rebuild(source, spacingAtlas);
            var textureData = TextureMapRebuilder.Rebuild(texture, atlas);
            File.WriteAllBytes(Path.Combine(outputDirectory, source.Name + ".descriptor.bin"), descriptor);
            var builtResource = new ScimitarResource(source.Index, source.Hash, source.Type,
                source.Name, descriptor);
            FontDescriptor.Read(builtResource).WriteTsv(
                Path.Combine(outputDirectory, source.Name + ".metrics.tsv"), builtResource);
            File.WriteAllBytes(Path.Combine(outputDirectory, atlasName + ".texture.bin"), textureData);
            PngWriter.WriteRgba(atlas.Width, atlas.Height, atlas.Rgba,
                Path.Combine(outputDirectory, atlasName + ".png"));
            var preview = new FontAtlas(atlasName, atlas.Width, atlas.Height, 0, atlas.Rgba);
            PngWriter.WritePreviews(preview,
                Path.Combine(outputDirectory, atlasName + "-gray.png"),
                Path.Combine(outputDirectory, atlasName + "-alpha.png"),
                Path.Combine(outputDirectory, atlasName + "-ink.png"));
            manifest.WriteLine($"{source.Name}\t{atlasName}\t{Path.GetFileName(font)}\t{codes.Length}\t{selectedSize}\t{target.Width}\t{target.Height}\t{target.Format}");
            Console.WriteLine($"{source.Name}: {codes.Length} glyphs, {selectedSize}px, {target.Width}x{target.Height}, {Path.GetFileName(font)}");
        }

        void Collect(string forge, string entry)
        {
            var raw = ForgeReader.ReadEntry(forge, entry);
            if (!ScimitarData.HasMagic(raw)) return;
            foreach (var resource in ScimitarResources.Read(ScimitarData.Read(raw)))
            {
                if (resource.Name.StartsWith("Arial", StringComparison.Ordinal) ||
                    resource.Name.StartsWith("Sava", StringComparison.Ordinal))
                {
                    if (resource.Type == 0x58E38D86)
                        descriptorSources.TryAdd(resource.Name, resource);
                    if (resource.Type == 0xA2B7E917)
                        atlasSources.TryAdd(resource.Name, resource);
                }
            }
        }
    }
}
