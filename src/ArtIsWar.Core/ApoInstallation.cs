namespace ArtIsWar.Core;

public static class ApoInstallation
{
    public static string? DeviceSelector(string directory)
    {
        foreach (var executable in new[] { "DeviceSelector.exe", "Configurator.exe" })
        {
            string path = Path.Combine(directory, executable);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public static bool IsInstalled(string directory) => DeviceSelector(directory) != null
        && Directory.Exists(Path.Combine(directory, "config"));
}
