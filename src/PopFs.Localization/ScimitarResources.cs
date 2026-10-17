using System.Buffers.Binary;
using System.Text;

namespace PopFs.Localization;

internal sealed record ScimitarResource(int Index, uint Hash, uint Type,
    string Name, ReadOnlyMemory<byte> Data);

internal static class ScimitarResources
{
    public static IReadOnlyList<byte[]> Replace(IReadOnlyList<byte[]> original,
        IReadOnlyDictionary<string, byte[]> replacements)
    {
        var resources = Read(original);
        using var table = new MemoryStream(4 + resources.Count * 10);
        using var content = new MemoryStream(original[1].Length);
        Span<byte> value = stackalloc byte[10];
        table.Write([0, 0]);
        BinaryPrimitives.WriteUInt16LittleEndian(value, checked((ushort)resources.Count));
        table.Write(value[..2]);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            var bytes = replacements.TryGetValue(resource.Name, out var replacement) ? replacement :
                resource.Data.ToArray();
            if (replacement is not null) used.Add(resource.Name);
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != resource.Type)
                throw new InvalidDataException($"Resource type changed: {resource.Name}.");
            BinaryPrimitives.WriteUInt32LittleEndian(value, resource.Hash);
            BinaryPrimitives.WriteUInt32LittleEndian(value[4..], checked((uint)bytes.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(value[8..], 0xFFFF);
            table.Write(value);
            content.Write(bytes);
        }
        if (used.Count != replacements.Count)
            throw new InvalidDataException("Some replacement resources were not found.");
        var rebuilt = new[] { table.ToArray(), content.ToArray() };
        _ = Read(rebuilt);
        return rebuilt;
    }

    public static IReadOnlyList<ScimitarResource> Read(IReadOnlyList<byte[]> containers)
    {
        var toc = containers[0].AsSpan();
        var bytes = containers[1];
        if (toc.Length < 4 || BinaryPrimitives.ReadUInt16LittleEndian(toc) != 0)
            throw new InvalidDataException("Invalid resource table header.");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(toc[2..]);
        if (toc.Length != 4 + count * 10)
            throw new InvalidDataException("Invalid resource table length.");

        var resources = new List<ScimitarResource>(count);
        var offset = 0;
        for (var index = 0; index < count; index++)
        {
            var entry = toc.Slice(4 + index * 10, 10);
            var hash = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            if (BinaryPrimitives.ReadUInt16LittleEndian(entry[8..]) != 0xFFFF ||
                length < 12 || length > bytes.Length - offset)
                throw new InvalidDataException($"Invalid resource entry {index}.");
            var data = bytes.AsMemory(offset, (int)length);
            var span = data.Span;
            var nameLength = BinaryPrimitives.ReadInt32LittleEndian(span[8..]);
            var name = string.Empty;
            if (nameLength >= 0 && nameLength <= 127 && 13 + nameLength <= span.Length &&
                span[12 + nameLength] == 0)
                name = Encoding.ASCII.GetString(span.Slice(12, nameLength));
            resources.Add(new ScimitarResource(index, hash,
                BinaryPrimitives.ReadUInt32LittleEndian(span), name, data));
            offset += (int)length;
        }
        if (offset != bytes.Length)
            throw new InvalidDataException("Resource sizes do not consume the data container.");
        return resources;
    }
}
