using System.Globalization;
using System.Text;

namespace PopFs.Localization;

internal static class TextDumpReader
{
    public static IReadOnlyDictionary<uint, string> Read(string path, LocalizationPackage original)
    {
        var texts = new Dictionary<uint, string>();
        using var reader = new StreamReader(path, new UTF8Encoding(false, true), true);
        string? line;
        var lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            var equal = line.IndexOf('=');
            if (equal < 1 || !uint.TryParse(line.AsSpan(0, equal), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var id))
                throw new InvalidDataException($"Invalid ID at line {lineNumber}.");
            if (!texts.TryAdd(id, Unescape(line.AsSpan(equal + 1), lineNumber)))
                throw new InvalidDataException($"Duplicate ID {id} at line {lineNumber}.");
        }

        var expected = original.Entries.Select(entry => entry.Id).ToHashSet();
        if (texts.Count != original.Entries.Count || !expected.SetEquals(texts.Keys))
        {
            var missing = expected.Except(texts.Keys).Take(5);
            var extra = texts.Keys.Except(expected).Take(5);
            throw new InvalidDataException($"TXT IDs differ from {original.Name}: " +
                $"missing [{string.Join(", ", missing)}], extra [{string.Join(", ", extra)}].");
        }
        return texts;
    }

    private static string Unescape(ReadOnlySpan<char> value, int lineNumber)
    {
        var text = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\') { text.Append(value[i]); continue; }
            if (++i == value.Length)
                throw new InvalidDataException($"Unfinished escape at line {lineNumber}.");
            text.Append(value[i] switch
            {
                '\\' => '\\', 'r' => '\r', 'n' => '\n', 't' => '\t',
                _ => throw new InvalidDataException($"Unknown escape at line {lineNumber}.")
            });
        }
        return text.ToString();
    }
}
