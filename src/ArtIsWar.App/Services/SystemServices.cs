using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using ArtIsWar.Core;

namespace ArtIsWar.App.Services;

public static class SystemServices
{
    public static string? ApoDirectory()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\EqualizerAPO");
        string? path = key?.GetValue("InstallPath")?.ToString();
        path ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO");
        return ApoInstallation.IsInstalled(path) ? path : null;
    }
    public static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    public static string? ApoConfigDirectory()
    {
        string? install = ApoDirectory(); if (install == null) return null;
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\EqualizerAPO");
        return key?.GetValue("ConfigPath") is string configured && Directory.Exists(configured) ? configured : Path.Combine(install, "config");
    }
    public static void ConfigureApo()
    {
        var apo = ApoDirectory() ?? throw new InvalidOperationException("Install Equalizer APO first.");
        string selector = ApoInstallation.DeviceSelector(apo) ?? throw new FileNotFoundException("Equalizer APO device selector was not found.");
        Process.Start(new ProcessStartInfo(selector) { UseShellExecute = true, Verb = "runas" });
    }
    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("ArtIsWarAudio", $"\"{Environment.ProcessPath}\" --tray");
        else key.DeleteValue("ArtIsWarAudio", false);
    }
    public static void RestartAudio()
    {
        Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"Restart-Service Audiosrv -Force\"")
        { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
    }
    public static async Task<string> DriverReport()
    {
        using var process = Process.Start(new ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -Command \"Get-CimInstance Win32_PnPSignedDriver | Where-Object DeviceClass -eq 'MEDIA' | Select-Object DeviceName,DriverProviderName,DriverVersion | ConvertTo-Json -Compress\"")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
        var read = process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); return "Driver query timed out."; }
        return process.ExitCode == 0 ? await read : "Driver information unavailable.";
    }
    public static string AudioServiceState()
    {
        using var process = Process.Start(new ProcessStartInfo("sc.exe", "query Audiosrv")
        { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true })!;
        string result = process.StandardOutput.ReadToEnd(); process.WaitForExit();
        return result.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) ? "Running" : "Not running / unavailable";
    }
}

public static class DspActivity
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr OpenFileMapping(uint access, bool inherit, string name);
    [DllImport("kernel32.dll")] private static extern IntPtr MapViewOfFile(IntPtr handle, uint access, uint high, uint low, nuint size);
    [DllImport("kernel32.dll")] private static extern bool UnmapViewOfFile(IntPtr pointer);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    public static bool Observed(string endpointId, out float reduction)
    {
        reduction = 0;
        IntPtr handle = OpenFileMapping(4, false, DspIdentity.MappingName(endpointId));
        if (handle == IntPtr.Zero) return false;
        try
        {
            IntPtr view = MapViewOfFile(handle, 4, 0, 0, 24);
            if (view == IntPtr.Zero) return false;
            try
            {
                long tick = Marshal.ReadInt64(view); reduction = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(view, 20));
                return tick > 0 && Environment.TickCount64 - tick is >= 0 and < 2000;
            }
            finally { UnmapViewOfFile(view); }
        }
        finally { CloseHandle(handle); }
    }
}

public static class DiagnosticReport
{
    public static string Build(ScanResult scan, AppSettings settings, ApoConfiguration? apo, string plugin, bool observed)
    {
        // Allowlist fields; endpoint IDs, usernames, absolute paths and driver device IDs are excluded.
        return JsonSerializer.Serialize(new {
            TimestampUtc = DateTime.UtcNow, scan.Windows, scan.Architecture,
            AvailableRamGiB = Math.Round(scan.AvailableMemory / 1073741824.0, 1),
            DeviceCount = scan.Devices.Count,
            SelectedOutputAvailable = scan.Devices.Any(d => d.Id == settings.DeviceId),
            Formats = scan.Devices.Select(d => new { d.SampleRate, d.Channels, d.SupportedRates }),
            ApoInstalled = SystemServices.ApoDirectory() != null,
            ConfigEnabled = apo?.IsEnabled == true, PluginAvailable = File.Exists(plugin),
            ConfigRootAvailable = apo != null && Directory.Exists(apo.ManagedDirectory),
            SampleRateCompatible = scan.Devices.Any(d => d.Id == settings.DeviceId && d.Channels == 2 && d.SampleRate is >= 44100 and <= 96000),
            DspProcessingObserved = observed, DeviceAttachment = observed ? "Processing observed" : "Not verified — use APO Device Selector and audio test",
            AudioService = SystemServices.AudioServiceState(), Profile = new { settings.Profile.Footsteps, settings.Profile.Gunshots, settings.Profile.Explosions, settings.Profile.Intensity },
            HeadphoneFilterCount = settings.HeadphoneFilters.Count,
            Limitation = "No game-specific acoustic validation; shared-mode processing only."
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
