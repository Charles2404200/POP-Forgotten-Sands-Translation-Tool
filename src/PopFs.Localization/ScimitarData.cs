using System.Buffers.Binary;

namespace PopFs.Localization;

internal static class ScimitarData
{
    private static readonly byte[] Magic = [0x33, 0xAA, 0xFB, 0x57, 0x99, 0xFA, 0x04, 0x10];

    public static bool HasMagic(ReadOnlySpan<byte> data) =>
        data.Length >= Magic.Length && data[..Magic.Length].SequenceEqual(Magic);

    public static IReadOnlyList<byte[]> Read(ReadOnlySpan<byte> data)
    {
        var containers = new List<byte[]>(2);
        var position = 0;
        while (position < data.Length)
        {
            if (data.Length - position < 17 || !data.Slice(position, 8).SequenceEqual(Magic))
                throw new InvalidDataException($"Invalid container at 0x{position:X}.");
            var version = BinaryPrimitives.ReadUInt16LittleEndian(data[(position + 8)..]);
            var algorithm = data[position + 10];
            var chunkCount = BinaryPrimitives.ReadUInt16LittleEndian(data[(position + 15)..]);
            if (version != 1 || algorithm != 5 || chunkCount == 0)
                throw new InvalidDataException($"Unsupported container version or algorithm at 0x{position:X}.");
            position += 17;
            var chunks = new (int outputLength, int inputLength)[chunkCount];
            var totalLength = 0;
            for (var i = 0; i < chunkCount; i++)
            {
                if (position + 4 > data.Length) throw new InvalidDataException("Truncated chunk table.");
                var outputLength = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
                var inputLength = BinaryPrimitives.ReadUInt16LittleEndian(data[(position + 2)..]);
                chunks[i] = (outputLength, inputLength);
                totalLength = checked(totalLength + outputLength);
                position += 4;
            }

            var output = new byte[totalLength];
            var outputPosition = 0;
            for (var chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                if (position + 4 + chunk.inputLength > data.Length)
                    throw new InvalidDataException("Truncated chunk data.");
                // The source container keeps a 4-byte checksum before each chunk.
                position += 4;
                var source = data.Slice(position, chunk.inputLength);
                var destination = output.AsSpan(outputPosition, chunk.outputLength);
                if (chunk.inputLength == chunk.outputLength)
                    source.CopyTo(destination);
                else
                {
                    try { Lzo1C.Decompress(source, destination); }
                    catch (InvalidDataException error)
                    {
                        throw new InvalidDataException($"Container {containers.Count + 1}, chunk {chunkIndex}, compressed {chunk.inputLength}, output {chunk.outputLength}: {error.Message}", error);
                    }
                }
                position += chunk.inputLength;
                outputPosition += chunk.outputLength;
            }
            containers.Add(output);
        }

        if (containers.Count != 2) throw new InvalidDataException($"Expected two containers, got {containers.Count}.");
        return containers;
    }
}
