using System.Text.Json;

namespace ArtIsWar.Core;

public static class AtomicFile
{
    public static void Write(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(data); file.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void WriteText(string path, string text) => Write(path, System.Text.Encoding.UTF8.GetBytes(text));
}

public sealed class SettingsStore(string directory)
{
    private readonly string path = Path.Combine(directory, "settings.json");
    public string? Warning { get; private set; }
    public AppSettings Load()
    {
        if (!File.Exists(path)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty settings.");
            settings.Validate(); return settings;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or FormatException)
        {
            File.Copy(path, path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), true);
            Warning = "Invalid settings preserved as a .corrupt backup. Defaults loaded.";
            return new();
        }
    }
    public void Save(AppSettings settings)
    {
        settings.Validate();
        AtomicFile.WriteText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
