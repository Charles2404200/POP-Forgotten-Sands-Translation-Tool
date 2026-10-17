using BCnEncoder.Encoder;
using BCnEncoder.Shared;

namespace PopFs.Localization;

internal static class TextureMapRebuilder
{
    public static byte[] Rebuild(ScimitarResource source, RasterAtlas atlas)
    {
        var original = FontAtlas.Read(source);
        if (original.Width != atlas.Width || original.Height != atlas.Height)
            throw new InvalidDataException($"Atlas dimensions changed: {source.Name}.");
        // Scimitar stores the image bottom-up. FireFontDescriptor UVs use the
        // opposite vertical origin, so the stored glyphs must appear inverted
        // when viewed as a conventional top-down PNG.
        var storedRgba = new byte[atlas.Rgba.Length];
        var stride = checked(atlas.Width * 4);
        for (var y = 0; y < atlas.Height; y++)
            atlas.Rgba.AsSpan(y * stride, stride).CopyTo(
                storedRgba.AsSpan((atlas.Height - 1 - y) * stride, stride));
        byte[] pixels;
        if (original.Format == 0) pixels = storedRgba;
        else
        {
            var encoder = new BcEncoder(original.Format == 4 ? CompressionFormat.Bc2 : CompressionFormat.Bc3);
            encoder.OutputOptions.GenerateMipMaps = false;
            encoder.OutputOptions.Quality = CompressionQuality.BestQuality;
            var encoded = encoder.EncodeToRawBytes(storedRgba, atlas.Width, atlas.Height, PixelFormat.Rgba32);
            if (encoded.Length != 1) throw new InvalidDataException("Unexpected mip count from BC encoder.");
            pixels = encoded[0];
        }
        var result = source.Data.ToArray();
        if (pixels.Length != original.Pixels.Length)
            throw new InvalidDataException($"Encoded pixel length changed: {source.Name}.");
        pixels.CopyTo(result.AsSpan(result.Length - 60 - pixels.Length, pixels.Length));
        var check = new ScimitarResource(source.Index, source.Hash, source.Type, source.Name, result);
        var readback = FontAtlas.Read(check);
        if (readback.Width != atlas.Width || readback.Height != atlas.Height ||
            !readback.Pixels.AsSpan().SequenceEqual(pixels))
            throw new InvalidDataException($"Atlas roundtrip failed: {source.Name}.");
        return result;
    }
}
