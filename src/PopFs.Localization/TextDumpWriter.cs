using System.Text;

namespace PopFs.Localization;

internal static class TextDumpWriter
{
    public static void Write(LocalizationPackage package, string path)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        foreach (var entry in package.Entries)
        {
            writer.Write(entry.Id);
            writer.Write('=');
            foreach (var character in entry.Text)
            {
                writer.Write(character switch
                {
                    '\\' => "\\\\",
                    '\r' => "\\r",
                    '\n' => "\\n",
                    '\t' => "\\t",
                    _ => character.ToString()
                });
            }
            writer.WriteLine();
        }
    }
}
