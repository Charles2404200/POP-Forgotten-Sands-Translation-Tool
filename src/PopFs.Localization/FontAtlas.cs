using System.Buffers.Binary;

namespace PopFs.Localization;

internal sealed record FontAtlas(string Name, int Width, int Height, int Format, byte[] Pixels)
{
    public static FontAtlas Read(ScimitarResource resource)
    {
        if (resource.Type != 0xA2B7E917) throw new InvalidDataException("Not a TextureMap.");
        var data = resource.Data.Span;
        var nameLength = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        var body = 13 + nameLength;
        var width = BinaryPrimitives.ReadInt32LittleEndian(data[(body + 8)..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(data[(body + 12)..]);
        var format = BinaryPrimitives.ReadInt32LittleEndian(data[(body + 20)..]);
        var mipCount = BinaryPrimitives.ReadInt32LittleEndian(data[(body + 24)..]);
        var pixelOffset = body + 83;
        if (width is <= 0 or > 8192 || height is <= 0 or > 8192 || mipCount != 1 ||
            format is not (0 or 4 or 5) || pixelOffset > data.Length)
            throw new InvalidDataException($"Unsupported texture layout in {resource.Name}.");
        var length = format == 0 ? checked(width * height * 4) :
            checked(((width + 3) / 4) * ((height + 3) / 4) * 16);
        // The 60-byte texture trailer follows the pixel payload. Treating it as
        // pixels shifts BC blocks by 12 bytes and overwrites engine metadata.
        if (data.Length - pixelOffset != length + 60)
            throw new InvalidDataException($"Unexpected pixel data length in {resource.Name}.");
        var pixels = data.Slice(pixelOffset, length).ToArray();
        return new FontAtlas(resource.Name, width, height, format, pixels);
    }

    public void WriteDds(string path)
    {
        var header = new byte[128];
        "DDS "u8.CopyTo(header);
        Put(4, 124); // DDS_HEADER size
        Put(8, 0x00001007u | (Format == 0 ? 0x8u : 0x80000u)); // pitch or linear size
        Put(12, (uint)Height);
        Put(16, (uint)Width);
        Put(20, Format == 0 ? (uint)(Width * 4) : (uint)Pixels.Length);
        Put(76, 32); // DDS_PIXELFORMAT size
        Put(80, Format == 0 ? 0x41u : 0x04u); // RGBA or FourCC
        if (Format == 0)
        {
            Put(88, 32);
            Put(92, 0x000000FF);
            Put(96, 0x0000FF00);
            Put(100, 0x00FF0000);
            Put(104, 0xFF000000);
        }
        else Put(84, Format == 4 ? 0x33545844u : 0x35545844u); // DXT3 / DXT5
        Put(108, 0x1000); // DDSCAPS_TEXTURE
        using var file = File.Create(path);
        file.Write(header);
        file.Write(Pixels);

        void Put(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(offset), value);
    }
}
