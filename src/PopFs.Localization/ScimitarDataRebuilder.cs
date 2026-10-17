using System.Buffers.Binary;

namespace PopFs.Localization;

internal static class ScimitarDataRebuilder
{
    private const int ChunkSize = 0x8000;

    public static byte[] Rebuild(byte[] original, IReadOnlyList<byte[]> replacement,
        Lzo1CCompressor compressor)
    {
        var oldRaw = ScimitarData.Read(original);
        if (replacement.Count != 2) throw new ArgumentException("Expected two containers.");
        var source = original.AsSpan();
        var position = 0;
        using var result = new MemoryStream(original.Length);
        for (var container = 0; container < 2; container++)
        {
            var header = source.Slice(position, 15).ToArray();
            var count = BinaryPrimitives.ReadUInt16LittleEndian(source[(position + 15)..]);
            position += 17;
            var lengths = new (int Raw, int Packed)[count];
            for (var i = 0; i < count; i++)
            {
                lengths[i] = (BinaryPrimitives.ReadUInt16LittleEndian(source[position..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(source[(position + 2)..]));
                position += 4;
            }
            var oldFrames = new byte[count][];
            for (var i = 0; i < count; i++)
            {
                position += 4; // checksum
                oldFrames[i] = source.Slice(position, lengths[i].Packed).ToArray();
                position += lengths[i].Packed;
            }
            var raw = replacement[container];
            var frameCount = checked((raw.Length + ChunkSize - 1) / ChunkSize);
            if (frameCount is < 1 or > ushort.MaxValue)
                throw new InvalidDataException("Scimitar container chunk count is out of range.");
            var frames = new byte[frameCount][];
            var rawLengths = new int[frameCount];
            var oldRawOffset = 0;
            var changed = 0;
            for (var i = 0; i < frameCount; i++)
            {
                var offset = i * ChunkSize;
                var length = Math.Min(ChunkSize, raw.Length - offset);
                rawLengths[i] = length;
                var unchanged = i < count && lengths[i].Raw == length &&
                    oldRaw[container].AsSpan(oldRawOffset, length).SequenceEqual(raw.AsSpan(offset, length));
                if (unchanged) frames[i] = oldFrames[i];
                else
                {
                    var packed = compressor.Compress(raw.AsSpan(offset, length));
                    frames[i] = packed.Length < length ? packed : raw.AsSpan(offset, length).ToArray();
                    changed++;
                }
                if (i < count) oldRawOffset += lengths[i].Raw;
            }
            result.Write(header);
            WriteU16(result, checked((ushort)frameCount));
            for (var i = 0; i < frameCount; i++)
            {
                WriteU16(result, checked((ushort)rawLengths[i]));
                WriteU16(result, checked((ushort)frames[i].Length));
            }
            foreach (var frame in frames)
            {
                WriteU32(result, Adler32Zero(frame));
                result.Write(frame);
            }
            Console.WriteLine($"Container {container + 1}: {changed}/{frameCount} chunks changed.");
        }
        if (position != original.Length)
            throw new InvalidDataException("Original Scimitar data has trailing bytes.");
        var rebuilt = result.ToArray();
        var decoded = ScimitarData.Read(rebuilt);
        if (!decoded[0].AsSpan().SequenceEqual(replacement[0]) ||
            !decoded[1].AsSpan().SequenceEqual(replacement[1]))
            throw new InvalidDataException("Rebuilt Scimitar data failed roundtrip verification.");
        return rebuilt;
    }

    private static uint Adler32Zero(ReadOnlySpan<byte> bytes)
    {
        const uint modulus = 65521;
        uint a = 0, b = 0;
        foreach (var value in bytes)
        {
            a = (a + value) % modulus;
            b = (b + a) % modulus;
        }
        return (b << 16) | a;
    }

    private static void WriteU16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }
}
