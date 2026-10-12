using System.Text;
using System.Security.Cryptography;

namespace ArtIsWar.Core;

public sealed class ApoConfiguration(string configDirectory, string pluginPath)
{
    public const string Begin = "# BEGIN ARTISWAR AUDIO MANAGED INCLUDE";
    public const string End = "# END ARTISWAR AUDIO MANAGED INCLUDE";
    private string Root => Path.Combine(configDirectory, "config.txt");
    public string ManagedDirectory => Path.Combine(configDirectory, "ArtIsWarAudio");
    private string Chain => Path.Combine(ManagedDirectory, "active.txt");
    private string Backup => Path.Combine(ManagedDirectory, "original-config.bin");
    private string OwnedPlugin => Path.Combine(ManagedDirectory, "ArtIsWar.Dsp.dll");
    public bool IsEnabled => File.Exists(Chain) && File.ReadAllText(Chain).Contains("VSTPlugin:", StringComparison.Ordinal)
        && File.Exists(Root) && File.ReadAllText(Root).Contains(Begin, StringComparison.Ordinal);

    public static string Render(AppSettings settings, string plugin)
    {
        settings.Validate();
        if (string.IsNullOrWhiteSpace(settings.DeviceId)) throw new InvalidOperationException("Select an available playback device first.");
        if (plugin.Contains('"') || plugin.Contains('\n')) throw new InvalidDataException("Invalid plug-in path.");
        var filters = settings.HeadphoneFilters.Concat(settings.Profile.Filters()).Concat(settings.AdvancedFilters).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("# ArtIsWar Audio — generated; manage through the app.");
        string deviceGuid = settings.DeviceId.Length >= 38 ? settings.DeviceId[^38..] : settings.DeviceId;
        if (!Guid.TryParse(deviceGuid, out var guid)) throw new InvalidDataException("The playback endpoint ID does not contain a valid GUID.");
        sb.AppendLine($"Device: {{{guid}}}");
        sb.AppendLine("Stage: post-mix");
        sb.AppendLine("If: outputChannelCount == 2 and sampleRate >= 44100 and sampleRate <= 96000");
        sb.AppendLine("Channel: ALL");
        double headroom = -Math.Max(3, filters.Where(f => f.Enabled).Sum(f => Math.Max(0, f.Gain)) + 1);
        sb.AppendLine(FormattableString.Invariant($"Preamp: {headroom + settings.HeadphonePreamp:F3} dB"));
        for (int i = 0; i < filters.Count; i++) sb.AppendLine(filters[i].ToApo(i + 1));
        var p = settings.Profile;
        double s = p.Intensity / 100.0;
        double movement = p.Footsteps / 100.0 * s;
        sb.AppendLine(FormattableString.Invariant($"VSTPlugin: Library \"{plugin}\" Thresh {Math.Clamp(.8 - .68 * p.Gunshots / 100.0 * s - .08 * movement, .05, .8):F6} Ratio {Math.Clamp(.15 + .75 * p.Gunshots / 100.0 * s + .08 * movement, 0, 1):F6} Makeup {(Math.Min(12, -headroom + 3 * movement) / 12):F6} Bass {(p.Explosions / 100.0 * s):F6} Multi {(settings.Multiband ? 1 : 0)} Token {(DspIdentity.Token(settings.DeviceId) / 1048575.0):F9}"));
        sb.AppendLine("EndIf:");
        sb.AppendLine("Device: all");
        sb.AppendLine("Channel: ALL");
        return sb.ToString();
    }

    public void Enable(AppSettings settings)
    {
        if (!File.Exists(pluginPath)) throw new FileNotFoundException("ArtIsWar DSP plug-in is missing. Extract the complete release package.", pluginPath);
        if (!File.Exists(Root)) throw new FileNotFoundException("Equalizer APO config.txt was not found.");
        var chain = Render(settings, OwnedPlugin);
        byte[] oldRoot = File.ReadAllBytes(Root);
        string root = Decode(oldRoot);
        ValidateManagedSection(root);
        byte[]? oldChain = File.Exists(Chain) ? File.ReadAllBytes(Chain) : null;
        byte[]? oldPlugin = File.Exists(OwnedPlugin) ? File.ReadAllBytes(OwnedPlugin) : null;
        bool pluginChanged = false;
        Directory.CreateDirectory(ManagedDirectory);
        if (!File.Exists(Backup)) AtomicFile.Write(Backup, oldRoot);
        try
        {
            byte[] plugin = File.ReadAllBytes(pluginPath);
            if (oldPlugin == null || !SHA256.HashData(oldPlugin).SequenceEqual(SHA256.HashData(plugin))) { AtomicFile.Write(OwnedPlugin, plugin); pluginChanged = true; }
            AtomicFile.WriteText(Chain, chain);
            if (!root.Contains(Begin, StringComparison.Ordinal))
            {
                if (root.Contains(End, StringComparison.Ordinal)) throw new InvalidDataException("Incomplete managed section. Restore before enabling.");
                string newline = root.Contains("\r\n") ? "\r\n" : "\n";
                string section = newline + Begin + newline + "Include: ArtIsWarAudio/active.txt" + newline + End + newline;
                if (!File.ReadAllBytes(Root).SequenceEqual(oldRoot)) throw new IOException("APO configuration changed during setup. Your edits were preserved; retry.");
                AtomicFile.Write(Root, EncodeLike(oldRoot, root + section));
            }
            else if (!root.Contains("Include: ArtIsWarAudio/active.txt", StringComparison.Ordinal) || !root.Contains(End, StringComparison.Ordinal))
                throw new InvalidDataException("Managed include is damaged. Restore before enabling.");
        }
        catch
        {
            if (oldChain != null) AtomicFile.Write(Chain, oldChain); else if (File.Exists(Chain)) File.Delete(Chain);
            if (pluginChanged) { if (oldPlugin != null) AtomicFile.Write(OwnedPlugin, oldPlugin); else if (File.Exists(OwnedPlugin)) File.Delete(OwnedPlugin); }
            throw;
        }
    }
    public void Disable()
    {
        if (File.Exists(Chain)) AtomicFile.WriteText(Chain, "# ArtIsWar Audio bypassed. No processing.\n");
    }
    public void Restore()
    {
        Disable();
        if (!File.Exists(Root)) throw new FileNotFoundException("APO root configuration is missing; backup retained.");
        byte[] bytes = File.ReadAllBytes(Root);
        string text = Decode(bytes);
        ValidateManagedSection(text);
        int start = text.IndexOf(Begin, StringComparison.Ordinal);
        if (start >= 0)
        {
            int end = text.IndexOf(End, start, StringComparison.Ordinal);
            if (end < 0) throw new InvalidDataException("Managed section is damaged; processing bypassed and backup retained.");
            end += End.Length;
            if (end < text.Length && text[end] == '\r') end++;
            if (end < text.Length && text[end] == '\n') end++;
            if (start > 0 && text[start - 1] == '\n') { start--; if (start > 0 && text[start - 1] == '\r') start--; }
            AtomicFile.Write(Root, EncodeLike(bytes, text.Remove(start, end - start)));
        }
    }
    public void Remove()
    {
        Restore();
        // Explicit names only: user files in this directory are retained.
        foreach (var file in new[] { Chain, OwnedPlugin, Backup }) if (File.Exists(file)) File.Delete(file);
        if (Directory.Exists(ManagedDirectory) && !Directory.EnumerateFileSystemEntries(ManagedDirectory).Any()) Directory.Delete(ManagedDirectory);
    }
    private static Encoding EncodingOf(byte[] bytes) => bytes.AsSpan().StartsWith(new byte[] { 255, 254 }) ? Encoding.Unicode :
        bytes.AsSpan().StartsWith(new byte[] { 254, 255 }) ? Encoding.BigEndianUnicode : new UTF8Encoding(bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }), true);
    private static string Decode(byte[] bytes)
    { var encoding = EncodingOf(bytes); var preamble = encoding.GetPreamble(); return encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length); }
    private static byte[] EncodeLike(byte[] bytes, string text)
    { var encoding = EncodingOf(bytes); return [.. encoding.GetPreamble(), .. encoding.GetBytes(text)]; }
    private static void ValidateManagedSection(string text)
    {
        int start = text.IndexOf(Begin, StringComparison.Ordinal), end = text.IndexOf(End, StringComparison.Ordinal);
        if (start < 0 && end < 0) return;
        if (start < 0 || end < start || text.IndexOf(Begin, start + Begin.Length, StringComparison.Ordinal) >= 0 ||
            text.IndexOf(End, end + End.Length, StringComparison.Ordinal) >= 0 ||
            text[(start + Begin.Length)..end].Trim() != "Include: ArtIsWarAudio/active.txt")
            throw new InvalidDataException("Managed include was edited or damaged. Processing can be bypassed, but this block needs manual review; backup retained.");
    }
}
