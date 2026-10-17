using System.Buffers.Binary;
using System.Text;

namespace PopFs.Localization;

internal static class ForgeRebuilder
{
    public static void Rebuild(string sourcePath, string packageDirectory, string outputPath,
        bool forceAppend = false)
    {
        var source = File.ReadAllBytes(sourcePath);
        var entries = ForgeReader.ListEntries(sourcePath).OrderBy(e => e.Offset).ToArray();
        if (entries.Length == 0) throw new InvalidDataException("Forge has no FILEDATA entries.");
        var firstBlock = checked((int)(entries[0].Offset - 440));
        using var output = new MemoryStream(source.Length + 1024 * 1024);
        output.Write(source);
        var patches = new List<(ForgeEntry Entry, int NewOffset, int NewLength)>();
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var oldStart = checked((int)(entry.Offset - 440));
            var oldEnd = index + 1 < entries.Length ?
                checked((int)(entries[index + 1].Offset - 440)) : source.Length;
            var oldBlockSize = oldEnd - oldStart;
            var package = Path.Combine(packageDirectory,
                Path.GetFileName(sourcePath) + "__" + entry.Name + ".data");
            var newData = File.Exists(package) ? File.ReadAllBytes(package) : null;
            var newLength = newData?.Length ?? entry.Length;
            var newStart = oldStart;
            if (newData is not null)
            {
                var header = source.AsSpan(oldStart, 440).ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(395), checked((uint)newLength));
                var minimum = checked(440 + newLength + 1);
                if (minimum > oldBlockSize || forceAppend)
                {
                    // Keep every other FILEDATA block at its original address.
                    // The game may use addresses outside the directory table.
                    output.Position = oldStart;
                    output.Write(new byte[8]); // hide the superseded FILEDATA header
                    output.Position = output.Length;
                    if (output.Position % 2048 != 0)
                        throw new InvalidDataException("Forge end is not block aligned.");
                    newStart = checked((int)output.Position);
                }
                else output.Position = oldStart;
                output.Write(header);
                output.Write(newData);
                output.WriteByte(0);
                var blockSize = newStart == oldStart ? oldBlockSize :
                    (minimum + 2047) & ~2047;
                output.Write(new byte[blockSize - minimum]);
            }
            patches.Add((entry, newStart, newLength));
        }
        var bytes = output.ToArray();
        foreach (var patch in patches)
        {
            var oldStart = checked((int)(patch.Entry.Offset - 440));
            var key = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(oldStart + 0x187));
            var central = FindCentral(source.AsSpan(0, firstBlock), oldStart, key, patch.Entry.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central), checked((uint)patch.NewOffset));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 12), checked((uint)patch.NewLength));
            if (patch.NewLength != patch.Entry.Length && patch.Entry.Name.Length > 0)
            {
                var namedSize = FindNamedSize(source.AsSpan(0, firstBlock), patch.Entry.Name,
                    patch.Entry.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(namedSize),
                    checked((uint)patch.NewLength));
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllBytes(outputPath, bytes);
        var check = ForgeReader.ListEntries(outputPath);
        if (check.Count != patches.Count ||
            patches.Any(p => !check.Any(entry => entry.Name == p.Entry.Name &&
                entry.Length == p.NewLength && entry.Offset == p.NewOffset + 440)))
            throw new InvalidDataException($"Rebuilt forge entry directory mismatch: {outputPath}.");
        foreach (var patch in patches.Where(p => p.NewLength != p.Entry.Length ||
            File.Exists(Path.Combine(packageDirectory,
                Path.GetFileName(sourcePath) + "__" + p.Entry.Name + ".data"))))
        {
            var package = Path.Combine(packageDirectory,
                Path.GetFileName(sourcePath) + "__" + patch.Entry.Name + ".data");
            if (!ForgeReader.ReadEntry(outputPath, patch.Entry.Name).AsSpan()
                .SequenceEqual(File.ReadAllBytes(package)))
                throw new InvalidDataException($"Rebuilt forge payload mismatch: {patch.Entry.Name}.");
        }
        Console.WriteLine($"Forge rebuilt: {Path.GetFileName(sourcePath)}, {source.Length} -> {bytes.Length} bytes, {patches.Count} entries.");
    }

    private static int FindCentral(ReadOnlySpan<byte> prefix, int oldOffset, uint key, int oldLength)
    {
        var found = -1;
        for (var i = 0; i + 16 <= prefix.Length; i++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(prefix[i..]) != oldOffset ||
                BinaryPrimitives.ReadUInt32LittleEndian(prefix[(i + 8)..]) != key ||
                BinaryPrimitives.ReadUInt32LittleEndian(prefix[(i + 12)..]) != oldLength) continue;
            if (found >= 0) throw new InvalidDataException($"Duplicate central record at 0x{i:X}.");
            found = i;
        }
        return found >= 0 ? found : throw new InvalidDataException($"Central record missing for 0x{oldOffset:X}.");
    }

    private static int FindNamedSize(ReadOnlySpan<byte> prefix, string name, int oldLength)
    {
        var needle = Encoding.ASCII.GetBytes(name);
        var found = -1;
        for (var i = 0; i + needle.Length <= prefix.Length; i++)
        {
            var sizeOffset = i - 0x2C;
            if (sizeOffset < 0 || !prefix.Slice(i, needle.Length).SequenceEqual(needle) ||
                BinaryPrimitives.ReadUInt32LittleEndian(prefix[sizeOffset..]) != oldLength)
                continue;
            if (found >= 0) throw new InvalidDataException($"Duplicate named record for {name}.");
            found = sizeOffset;
        }
        return found >= 0 ? found : throw new InvalidDataException($"Named record missing for {name}.");
    }
}
