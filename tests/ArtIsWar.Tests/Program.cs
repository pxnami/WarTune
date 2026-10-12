using ArtIsWar.Core;
using System.Text;

string testRoot = Path.Combine(Path.GetTempPath(), "ArtIsWar-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
int passed = 0;
void Test(string name, Action check) { check(); passed++; Console.WriteLine("PASS " + name); }
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
var settings = new AppSettings { DeviceId = "{0.0.0.00000000}.{12345678-1234-1234-1234-123456789abc}", Headphones = "Manual headset" };
string plugin = Path.Combine(testRoot, "test-plugin.dll"); File.WriteAllBytes(plugin, [1,2,3]);
try
{
    Test("APO 1.4 Device Selector and legacy Configurator detection", () => {
        string dir = Path.Combine(testRoot, "apo-install"); Directory.CreateDirectory(Path.Combine(dir, "config"));
        Assert(!ApoInstallation.IsInstalled(dir), "Config folder alone was treated as installed");
        string legacy = Path.Combine(dir, "Configurator.exe"), modern = Path.Combine(dir, "DeviceSelector.exe");
        File.WriteAllBytes(legacy, []);
        Assert(ApoInstallation.IsInstalled(dir) && ApoInstallation.DeviceSelector(dir) == legacy, "Legacy detection failed");
        File.WriteAllBytes(modern, []);
        Assert(ApoInstallation.DeviceSelector(dir) == modern, "Modern selector was not preferred");
        File.Delete(legacy);
        Assert(ApoInstallation.IsInstalled(dir), "APO 1.4 was not detected without Configurator.exe");
        Directory.Delete(Path.Combine(dir, "config"));
        Assert(!ApoInstallation.IsInstalled(dir), "Incomplete installation accepted");
    });
    Test("ON/OFF and byte-exact restoration across encodings", () => {
        foreach (var encoding in new Encoding[] { new UTF8Encoding(false), new UTF8Encoding(true), Encoding.Unicode, Encoding.BigEndianUnicode })
        foreach (string original in new[] { "# existing\nPreamp: -2 dB\n", "# existing\r\nPreamp: -2 dB", "" })
        {
            string dir = Path.Combine(testRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            byte[] bytes = [..encoding.GetPreamble(), ..encoding.GetBytes(original)]; File.WriteAllBytes(Path.Combine(dir, "config.txt"), bytes);
            var apo = new ApoConfiguration(dir, plugin); apo.Enable(settings); Assert(apo.IsEnabled, "Not enabled");
            apo.Enable(settings); Assert(File.ReadAllText(Path.Combine(dir, "config.txt")).Split(ApoConfiguration.Begin).Length == 2, "Duplicate include");
            apo.Disable(); Assert(!apo.IsEnabled, "Not bypassed"); apo.Restore();
            Assert(File.ReadAllBytes(Path.Combine(dir, "config.txt")).SequenceEqual(bytes), "Original bytes changed");
        }
    });
    Test("Restore preserves unrelated edits; remove preserves unknown files", () => {
        string dir = Path.Combine(testRoot, "preserve"); Directory.CreateDirectory(dir);
        string root = Path.Combine(dir, "config.txt"); File.WriteAllText(root, "Preamp: -3 dB\n");
        var apo = new ApoConfiguration(dir, plugin); apo.Enable(settings); File.AppendAllText(root, "Include: my-other-eq.txt\n");
        File.WriteAllText(Path.Combine(apo.ManagedDirectory, "user-notes.txt"), "keep"); apo.Remove();
        Assert(File.ReadAllText(root).Contains("my-other-eq.txt"), "Unrelated edit lost");
        Assert(File.Exists(Path.Combine(apo.ManagedDirectory, "user-notes.txt")), "Unowned file deleted");
    });
    Test("Failure recovery restores previous root and chain", () => {
        string dir = Path.Combine(testRoot, "failure"); Directory.CreateDirectory(dir);
        string root = Path.Combine(dir, "config.txt"); File.WriteAllText(root, ApoConfiguration.Begin + "\nBROKEN\n");
        string original = File.ReadAllText(root); var apo = new ApoConfiguration(dir, plugin);
        try { apo.Enable(settings); throw new Exception("Expected failure"); } catch (InvalidDataException) { }
        Assert(File.ReadAllText(root) == original && !apo.IsEnabled, "Failure left enabled config");
        File.WriteAllText(root, "Preamp: -4 dB\n");
        using (var held = new FileStream(root, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            try { apo.Enable(settings); throw new Exception("Expected locked-root failure"); } catch (IOException) { }
        }
        Assert(File.ReadAllText(root) == "Preamp: -4 dB\n" && !apo.IsEnabled, "Late failure changed root or left active chain");
    });
    Test("Missing plug-in cannot enable configuration", () => {
        var apo = new ApoConfiguration(testRoot, "missing.dll");
        try { apo.Enable(settings); throw new Exception("Expected missing plug-in failure"); } catch (FileNotFoundException) { }
    });
    Test("Local profiles persist and corrupt settings are quarantined", () => {
        var store = new SettingsStore(Path.Combine(testRoot, "settings")); settings.SavedProfiles.Add(new("Test", 12, 34, 56, 78)); store.Save(settings);
        var loaded = store.Load(); Assert(loaded.SavedProfiles.Single().Intensity == 78 && loaded.Headphones == "Manual headset", "Persistence failed");
        File.WriteAllText(Path.Combine(testRoot, "settings", "settings.json"), "{bad"); store.Load(); Assert(store.Warning != null, "Corrupt settings not reported");
    });
    Test("AutoEQ import is strict and validates ranges", () => {
        var result = PeqImport.Parse("Preamp: -5.2 dB\nFilter 1: ON PK Fc 1200 Hz Gain 2.5 dB Q 1.1\nFilter 2: OFF HS Fc 8000 Hz Gain -3 dB Q 0.707");
        Assert(result.Filters.Count == 2 && !result.Filters[1].Enabled && result.Preamp == -5.2, "Import failed");
        try { PeqImport.Parse("Include: unrelated.txt"); throw new Exception("Unsafe line accepted"); } catch (InvalidDataException) { }
        try { new EqFilter(FilterType.PK, double.NaN, 0, 1).Validate(); throw new Exception("NaN accepted"); } catch (InvalidDataException) { }
    });
    Test("APO scope, format guard, dynamics and locale-independent decimals", () => {
        System.Globalization.CultureInfo.CurrentCulture = new("de-DE"); string rendered = ApoConfiguration.Render(settings, plugin);
        Assert(rendered.Contains("Device: {12345678-1234-1234-1234-123456789abc}") && !rendered.Contains("{0.0.0.00000000}"), "Incorrect endpoint scope");
        Assert(rendered.Contains("outputChannelCount == 2") && rendered.Contains("VSTPlugin:") && rendered.Contains("Thresh ") && !rendered.Contains("0,707"), "Missing dynamics/guard or locale mismatch");
    });
    Test("Frequency response matches center gain and disabled bypass", () => {
        double gain = EqResponse.Decibels([new(FilterType.PK, 1000, 6, 1)], 1000, 48000);
        Assert(Math.Abs(gain - 6) < .0001, "Response is incorrect");
        Assert(EqResponse.Decibels([new(FilterType.PK, 1000, 6, 1, false)], 1000, 48000) == 0, "Disabled filter affects response");
    });
    Test("APO frequencies cannot be mistaken for thousands-separated values", () => {
        foreach (string culture in new[] { "de-DE", "en-US" }) {
            System.Globalization.CultureInfo.CurrentCulture = new(culture);
            Assert(new EqFilter(FilterType.HPQ, 35, 0, .707).ToApo(1).Contains("Fc 35 Hz"), "High-pass frequency is ambiguous");
            Assert(new EqFilter(FilterType.PK, 1800, 2, 1).ToApo(2).Contains("Fc 1800 Hz"), "Peaking frequency is ambiguous");
            var fractional = new EqFilter(FilterType.PK, 1000.5, 6, 1);
            Assert(fractional.ToApo(3).Contains("Fc 1001 Hz"), "Fractional frequency was not quantized safely");
            Assert(Math.Abs(EqResponse.Decibels([fractional], 1001, 48000) - 6) < .0001, "Graph and applied frequency disagree");
        }
    });
    Console.WriteLine($"All {passed} test groups passed."); return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally { Directory.Delete(testRoot, true); }
