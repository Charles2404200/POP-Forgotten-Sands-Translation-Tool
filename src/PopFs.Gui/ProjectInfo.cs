using System.Security.Cryptography;
using System.Text.Json;

namespace PopFs.Gui;

internal sealed class ProjectInfo
{
    public string GameDirectory { get; set; } = "";
    public string Language { get; set; } = "English";
    public Dictionary<string, string> ExpectedHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static ProjectInfo Load(string directory)
    {
        var path = Path.Combine(directory, "project.json");
        return JsonSerializer.Deserialize<ProjectInfo>(File.ReadAllText(path)) ??
            throw new InvalidDataException("Invalid project.json.");
    }

    public void Save(string directory)
    {
        var path = Path.Combine(directory, "project.json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }

    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
