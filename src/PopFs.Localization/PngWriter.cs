using System.Buffers.Binary;
using System.IO.Compression;

namespace PopFs.Localization;

internal static class PngWriter
{
    public static void Write(FontAtlas atlas, string path)
    {
        var rgba = atlas.Format == 0 ? atlas.Pixels : DecodeBlocks(atlas);
        WriteRgba(atlas.Width, atlas.Height, rgba, path);
    }

    public static void WritePreviews(FontAtlas atlas, string compositePath, string alphaPath,
        string inkPath)
    {
        var rgba = atlas.Format == 0 ? atlas.Pixels : DecodeBlocks(atlas);
        var composite = new byte[rgba.Length];
        var alpha = new byte[rgba.Length];
        var ink = new byte[rgba.Length];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var coverage = rgba[i + 3];
            for (var channel = 0; channel < 3; channel++)
            {
                composite[i + channel] = (byte)((rgba[i + channel] * coverage +
                    128 * (255 - coverage) + 127) / 255);
                alpha[i + channel] = coverage;
            }
            composite[i + 3] = alpha[i + 3] = 255;
            var brightness = (54 * rgba[i] + 183 * rgba[i + 1] + 19 * rgba[i + 2] + 128) >> 8;
            var inkCoverage = Math.Min(255, 2 * (brightness * coverage + 127) / 255);
            ink[i] = ink[i + 1] = ink[i + 2] = (byte)(255 - inkCoverage);
            ink[i + 3] = 255;
        }
        WriteRgba(atlas.Width, atlas.Height, composite, compositePath);
        WriteRgba(atlas.Width, atlas.Height, alpha, alphaPath);
        WriteRgba(atlas.Width, atlas.Height, ink, inkPath);
    }

    public static void WriteRgba(int width, int height, byte[] rgba, string path)
    {
        var rowLength = checked(width * 4);
        var scanlines = new byte[checked((rowLength + 1) * height)];
        for (var y = 0; y < height; y++)
            rgba.AsSpan(y * rowLength, rowLength).CopyTo(
                scanlines.AsSpan(y * (rowLength + 1) + 1, rowLength));

        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8; // 8-bit RGBA
        header[9] = 6;
        WriteChunk(file, "IHDR"u8, header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            zlib.Write(scanlines);
        WriteChunk(file, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(file, "IEND"u8, []);
    }

    private static byte[] DecodeBlocks(FontAtlas atlas)
    {
        var rgba = new byte[checked(atlas.Width * atlas.Height * 4)];
        var blocksWide = (atlas.Width + 3) / 4;
        var blocksHigh = (atlas.Height + 3) / 4;
        Span<byte> colors = stackalloc byte[12];
        Span<byte> alphas = stackalloc byte[8];
        for (var by = 0; by < blocksHigh; by++)
        for (var bx = 0; bx < blocksWide; bx++)
        {
            var block = atlas.Pixels.AsSpan((by * blocksWide + bx) * 16, 16);
            Expand565(BinaryPrimitives.ReadUInt16LittleEndian(block[8..]), colors);
            Expand565(BinaryPrimitives.ReadUInt16LittleEndian(block[10..]), colors[3..]);
            for (var channel = 0; channel < 3; channel++)
            {
                colors[6 + channel] = (byte)((2 * colors[channel] + colors[3 + channel] + 1) / 3);
                colors[9 + channel] = (byte)((colors[channel] + 2 * colors[3 + channel] + 1) / 3);
            }
            var colorIndex = BinaryPrimitives.ReadUInt32LittleEndian(block[12..]);
            var alphaBits = BinaryPrimitives.ReadUInt64LittleEndian(block);
            if (atlas.Format == 5)
            {
                alphas[0] = block[0];
                alphas[1] = block[1];
                if (alphas[0] > alphas[1])
                    for (var i = 2; i < 8; i++)
                        alphas[i] = (byte)(((8 - i) * alphas[0] + (i - 1) * alphas[1]) / 7);
                else
                {
                    for (var i = 2; i < 6; i++)
                        alphas[i] = (byte)(((6 - i) * alphas[0] + (i - 1) * alphas[1]) / 5);
                    alphas[6] = 0;
                    alphas[7] = 255;
                }
            }
            for (var py = 0; py < 4; py++)
            for (var px = 0; px < 4; px++)
            {
                var x = bx * 4 + px;
                var y = by * 4 + py;
                if (x >= atlas.Width || y >= atlas.Height) continue;
                var pixel = py * 4 + px;
                var palette = (int)((colorIndex >> (pixel * 2)) & 3);
                var target = (y * atlas.Width + x) * 4;
                colors.Slice(palette * 3, 3).CopyTo(rgba.AsSpan(target, 3));
                rgba[target + 3] = atlas.Format == 4 ?
                    (byte)(((alphaBits >> (pixel * 4)) & 15) * 17) :
                    alphas[(int)((alphaBits >> (16 + pixel * 3)) & 7)];
            }
        }
        return rgba;
    }

    private static void Expand565(ushort value, Span<byte> rgb)
    {
        var red = (value >> 11) & 31;
        var green = (value >> 5) & 63;
        var blue = value & 31;
        rgb[0] = (byte)((red << 3) | (red >> 2));
        rgb[1] = (byte)((green << 2) | (green >> 4));
        rgb[2] = (byte)((blue << 3) | (blue >> 2));
    }

    private static void WriteChunk(Stream file, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        file.Write(word);
        file.Write(type);
        file.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (var b in type) crc = CrcByte(crc, b);
        foreach (var b in data) crc = CrcByte(crc, b);
        BinaryPrimitives.WriteUInt32BigEndian(word, ~crc);
        file.Write(word);
    }

    private static uint CrcByte(uint crc, byte value)
    {
        crc ^= value;
        for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
        return crc;
    }
}
