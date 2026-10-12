using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using ArtIsWar.Core;
using ArtIsWar.App.Services;

namespace ArtIsWar.App.ViewModels;

public sealed class AsyncCommand(Func<Task> execute, Action<Exception> onError) : ICommand
{
    private bool busy;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !busy;
    public async void Execute(object? parameter)
    {
        if (busy) return; busy = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(); } catch (Exception ex) { onError(ex); }
        finally { busy = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}

public sealed partial class MainViewModel : INotifyPropertyChanged, IDisposable
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArtIsWarAudio");
    private readonly SettingsStore store = new(DataDirectory);
    private readonly WindowsAudio audio = new();
    private readonly DispatcherTimer meter = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer refreshDelay = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private EndpointNotifications? notifications;
    private FileSystemWatcher? configWatcher;
    private CancellationTokenSource? testCancellation;
    private ScanResult? scan;
    private AppSettings settings;
    private ApoConfiguration? apo;
    private PlaybackDevice? selectedDevice;
    private AudioProfile? selectedProfile;
    private EqFilter? selectedFilter;
    private string message = "Scan your PC, select an output, then configure Equalizer APO.";
    private bool messageIsError;
    private string diagnostics = "Run diagnostics to inspect this PC. Reports contain allowlisted technical fields only.";
    private string headphones;
    private int page, setupStep, footsteps, gunshots, explosions, intensity;
    private string dspStatus = "Not observed", profileName = "My profile", filterTarget = "Advanced EQ";
    private double peak;
    private bool enabled;
    private bool testDspVerified, testRunning;
    private string headphoneSearch = "";
    public ObservableCollection<HeadphoneProfile> HeadphoneLibrary { get; } = [];
    public HeadphoneProfile? SelectedHeadphone { get; set; }
    public string HeadphoneSearch { get => headphoneSearch; set { headphoneSearch = value; RefreshHeadphones(); Notify(); } }
    public string PluginPath => Path.Combine(AppContext.BaseDirectory, "ArtIsWar.Dsp.dll");
    public string[] Navigation { get; } = ["Home", "Warzone", "Profiles", "Headphones", "Equalizer", "Audio Setup", "Diagnostics", "Settings"];
    public string[] FilterTypes { get; } = Enum.GetNames<FilterType>();
    public string[] FilterTargets { get; } = ["Advanced EQ", "Headphone correction"];
    public string[] TestKinds { get; } = ["Left", "Right", "Stereo", "Movement", "Dynamic range", "Peak protection"];
    public string TestKind { get; set; } = "Left";
    public ObservableCollection<PlaybackDevice> Devices { get; } = [];
    public ObservableCollection<AudioProfile> Profiles { get; } = [];
    public ObservableCollection<EqFilter> Filters { get; } = [];
    public string FilterTypeName { get; set; } = "PK";
    public double FilterFrequency { get; set; } = 1000;
    public double FilterGain { get; set; }
    public double FilterQ { get; set; } = 1;
    public bool FilterEnabled { get; set; } = true;
    public int Page { get => page; set { page = value; Notify(); Notify(nameof(PageTitle)); Notify(nameof(PageDescription)); } }
    public string PageTitle => Navigation[Math.Clamp(Page, 0, Navigation.Length - 1)];
    public string PageDescription => Page switch {
        0 => "Your listening setup, at a glance.",
        1 => "Find the balance between movement detail and loud peaks.",
        2 => "Keep a listening profile for every session.",
        3 => "Correct your headphones independently of game tuning.",
        4 => "Shape the response with precise parametric filters.",
        5 => "Connect your output and verify the signal path.",
        6 => "Inspect your audio setup and resolve configuration issues.",
        _ => "Make the utility fit the way you use Windows." };
    public bool CanDeleteProfile => SelectedProfile != null && settings.SavedProfiles.Contains(SelectedProfile);
    public int Footsteps { get => footsteps; set { footsteps = value; Notify(); Notify(nameof(ResponseFilters)); } }
    public int Gunshots { get => gunshots; set { gunshots = value; Notify(); Notify(nameof(ResponseFilters)); } }
    public int Explosions { get => explosions; set { explosions = value; Notify(); Notify(nameof(ResponseFilters)); } }
    public int Intensity { get => intensity; set { intensity = value; Notify(); Notify(nameof(ResponseFilters)); } }
    public string Headphones { get => headphones; set { headphones = value; Notify(); } }
    public string ProfileName { get => profileName; set { profileName = value; Notify(); } }
    public string FilterTarget { get => filterTarget; set { filterTarget = value; RefreshFilters(); Notify(); } }
    public EqFilter? SelectedFilter
    {
        get => selectedFilter;
        set
        {
            selectedFilter = value; Notify();
            if (value == null) return;
            FilterTypeName = value.Type.ToString(); FilterFrequency = value.Frequency; FilterGain = value.Gain; FilterQ = value.Q; FilterEnabled = value.Enabled;
            foreach (var name in new[] { nameof(FilterTypeName), nameof(FilterFrequency), nameof(FilterGain), nameof(FilterQ), nameof(FilterEnabled) }) Notify(name);
        }
    }
    public PlaybackDevice? SelectedDevice { get => selectedDevice; set { selectedDevice = value; if (value != null) settings.DeviceId = value.Id; Notify(); Notify(nameof(OutputSummary)); } }
    public AudioProfile? SelectedProfile
    {
        get => selectedProfile;
        set { selectedProfile = value; Notify(); Notify(nameof(CanDeleteProfile)); if (value != null) { Footsteps = value.Footsteps; Gunshots = value.Gunshots; Explosions = value.Explosions; Intensity = value.Intensity; Notify(nameof(ProfileSummary)); } }
    }
    public bool Multiband { get => settings.Multiband; set { settings.Multiband = value; Notify(); } }
    public bool StartWithWindows
    {
        get => settings.StartWithWindows;
        set { try { SystemServices.SetStartup(value); settings.StartWithWindows = value; store.Save(settings); Notify(); } catch (Exception ex) { Error(ex); } }
    }
    public bool MinimizeToTray { get => settings.MinimizeToTray; set { settings.MinimizeToTray = value; store.Save(settings); Notify(); } }
    public bool MessageIsError => messageIsError;
    public string Message { get => message; private set { message = value; messageIsError = false; Notify(); Notify(nameof(MessageIsError)); } }
    public string Diagnostics { get => diagnostics; private set { diagnostics = value; Notify(); } }
    public string HardwareSummary => scan == null ? "Not scanned" : $"{scan.Windows}\n{scan.Cpu}\n{scan.Architecture} · {scan.AvailableMemory / 1073741824.0:F1} GiB RAM available";
    public string OutputSummary => SelectedDevice == null ? "No output selected" : $"{SelectedDevice.Name}\n{SelectedDevice.SampleRate / 1000.0:g} kHz · {SelectedDevice.Channels} channels";
    public string SupportedRates => SelectedDevice?.SupportedRates ?? "Select an output first";
    public string DefaultOutput => scan?.Devices.FirstOrDefault(d => d.Id == scan.DefaultId)?.Name ?? "Unavailable";
    public string ProfileSummary => settings.Profile.Name;
    public bool IsOn => enabled;
    public string ToggleLabel => IsOn ? "WARTUNE ON" : "WARTUNE OFF";
    public string ApoStatus => apo == null ? "Equalizer APO missing" : "Equalizer APO detected";
    public string DspStatus { get => dspStatus; private set { dspStatus = value; Notify(); } }
    public double Peak { get => peak; private set { peak = value; Notify(); } }
    public IEnumerable<EqFilter> ResponseFilters => settings.HeadphoneFilters.Concat(Draft().Filters()).Concat(settings.AdvancedFilters);
    public string[] SetupSteps { get; } = ["Scan PC", "Select output", "Select headphones", "Configure processing", "Apply Warzone profile", "Run audio test", "Complete"];
    public string SetupTitle => $"{setupStep + 1} / 7   {SetupSteps[setupStep]}";
    public double SetupProgress => (setupStep + 1) * 100.0 / SetupSteps.Length;
    public string SetupDescription => setupStep switch {
        0 => "Scan reads hardware and playback formats without changing your PC.",
        1 => "Select the playback endpoint you use in Warzone. Stereo shared-mode output is required for this release.",
        2 => "Enter your headphone model. Windows endpoint names do not reliably identify headphones. Import correction separately if available.",
        3 => "Install Equalizer APO from its official project. In Device Selector, attach it to the selected playback endpoint and reboot if requested. No silent installation.",
        4 => "Apply writes a device-scoped include after backing up config.txt. Administrator permissions may be required.",
        5 => "Start at low listening volume. Play a test. DSP activity must be observed before completion; a saved config alone is not proof.",
        _ => "Configuration and live DSP processing were verified during a test. Check stereo positioning before playing." };
    public ICommand ScanCommand { get; }
    public ICommand ToggleCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand SaveProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand SaveHeadphonesCommand { get; }
    public ICommand LoadHeadphoneCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand AddFilterCommand { get; }
    public ICommand UpdateFilterCommand { get; }
    public ICommand DeleteFilterCommand { get; }
    public ICommand GraphicCommand { get; }
    public ICommand DiagnosticsCommand { get; }
    public ICommand RepairCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand ConfiguratorCommand { get; }
    public ICommand SoundSettingsCommand { get; }
    public ICommand AdminCommand { get; }
    public ICommand TestCommand { get; }
    public ICommand StopTestCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand OpenDataCommand { get; }
    public MainViewModel()
    {
        settings = store.Load(); headphones = settings.Headphones;
        RefreshHeadphones();
        foreach (var p in AudioProfile.Presets.Concat(settings.SavedProfiles)) Profiles.Add(p);
        if (!Profiles.Any(p => p == settings.Profile)) Profiles.Add(settings.Profile);
        if (!Profiles.Any(p => p.Name == "Custom")) Profiles.Add(new("Custom", 65, 65, 60, 70));
        SelectedProfile = Profiles.First(p => p == settings.Profile);
        RefreshFilters();
        ScanCommand = Command(ScanAsync);
        ToggleCommand = Command(() => { if (IsOn) { apo!.Disable(); testDspVerified = false; Message = "WarTune bypassed. Unrelated APO processing is preserved."; RefreshStatus(); return Task.CompletedTask; } return ApplyAsync(true); });
        ApplyCommand = Command(() => ApplyAsync(true));
        RestoreCommand = Command(() => { RequireApo().Restore(); RefreshStatus(); Message = "WarTune include removed. Other audio configuration preserved."; return Task.CompletedTask; });
        RemoveCommand = Command(() => { RequireApo().Remove(); RefreshStatus(); Message = "Only WarTune audio configuration was removed."; return Task.CompletedTask; });
        SaveProfileCommand = Command(() => {
            var p = Draft(ProfileName.Trim()); p.Validate();
            if (AudioProfile.Presets.Any(x => x.Name == p.Name)) throw new InvalidOperationException("Choose a name distinct from the built-in presets.");
            settings.SavedProfiles.RemoveAll(x => x.Name == p.Name); settings.SavedProfiles.Add(p);
            foreach (var x in Profiles.Where(x => x.Name == p.Name).ToArray()) Profiles.Remove(x);
            Profiles.Add(p); SelectedProfile = p; SaveDraft(); Message = "Profile saved locally."; return Task.CompletedTask;
        });
        DeleteProfileCommand = Command(() => {
            if (SelectedProfile == null || !settings.SavedProfiles.Contains(SelectedProfile)) throw new InvalidOperationException("Only custom profiles can be deleted.");
            settings.SavedProfiles.Remove(SelectedProfile); Profiles.Remove(SelectedProfile); SelectedProfile = Profiles[0]; SaveDraft(); return Task.CompletedTask;
        });
        SaveHeadphonesCommand = Command(() => {
            if (string.IsNullOrWhiteSpace(Headphones)) throw new InvalidOperationException("Enter your headphone model or 'Unknown'.");
            settings.HeadphoneProfiles.RemoveAll(p => p.Name == Headphones.Trim());
            settings.HeadphoneProfiles.Add(new(Headphones.Trim(), settings.HeadphoneFilters.ToList(), settings.HeadphonePreamp));
            SaveDraft(); RefreshHeadphones(); Message = "Headphone profile saved. Apply to update enabled processing."; return Task.CompletedTask;
        });
        LoadHeadphoneCommand = Command(() => {
            var p = SelectedHeadphone ?? throw new InvalidOperationException("Select a saved headphone profile.");
            Headphones = p.Name; settings.HeadphoneFilters = p.Filters.ToList(); settings.HeadphonePreamp = p.Preamp;
            RefreshFilters(); SaveDraft(); Message = "Headphone correction loaded. Apply to update processing."; return Task.CompletedTask;
        });
        ImportCommand = Command(() => {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "AutoEQ / APO text|*.txt", Title = "Import headphone PEQ" };
            if (dialog.ShowDialog() == true) {
                var parsed = PeqImport.Parse(File.ReadAllText(dialog.FileName)); settings.HeadphoneFilters = parsed.Filters; settings.HeadphonePreamp = parsed.Preamp;
                FilterTarget = "Headphone correction"; SaveDraft(); Message = "Imported headphone correction. Apply to update processing.";
            }
            return Task.CompletedTask;
        });
        AddFilterCommand = Command(() => { if (settings.HeadphoneFilters.Count + settings.AdvancedFilters.Count >= 64) throw new InvalidOperationException("At most 64 custom filters are supported."); var f = EditedFilter(); FilterList().Add(f); SaveFilters(); return Task.CompletedTask; });
        UpdateFilterCommand = Command(() => {
            int index = SelectedFilter == null ? -1 : FilterList().IndexOf(SelectedFilter);
            if (index < 0) throw new InvalidOperationException("Select a filter to update.");
            FilterList()[index] = EditedFilter(); SaveFilters(); return Task.CompletedTask;
        });
        DeleteFilterCommand = Command(() => { if (SelectedFilter != null) FilterList().Remove(SelectedFilter); SaveFilters(); return Task.CompletedTask; });
        GraphicCommand = Command(() => { settings.AdvancedFilters = new[] {31,62,125,250,500,1000,2000,4000,8000,16000}.Select(f => new EqFilter(FilterType.PK, f, 0, 1.414)).ToList(); FilterTarget = "Advanced EQ"; SaveFilters(); return Task.CompletedTask; });
        DiagnosticsCommand = Command(RunDiagnosticsAsync);
        RepairCommand = Command(async () => { await ScanAsync(); ValidateOutput(); RequireApo().Restore(); await ApplyAsync(true); await RunDiagnosticsAsync(); });
        ExportCommand = Command(async () => {
            await RunDiagnosticsAsync(); var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Diagnostic report|*.json", FileName = "WarTune-diagnostics.json" };
            if (dialog.ShowDialog() == true) { AtomicFile.WriteText(dialog.FileName, DiagnosticReport.Build(scan!, settings, apo, PluginPath, DspActivity.Observed(settings.DeviceId, out _))); Message = "Redacted diagnostic report exported."; }
        });
        RestartCommand = Command(() => {
            if (MessageBox.Show("Restart Windows Audio? All applications will lose audio temporarily.", "Restart audio", System.Windows.MessageBoxButton.YesNo) == System.Windows.MessageBoxResult.Yes) SystemServices.RestartAudio();
            return Task.CompletedTask;
        });
        InstallCommand = Command(() => { SystemServices.Open("https://sourceforge.net/projects/equalizerapo/"); return Task.CompletedTask; });
        ConfiguratorCommand = Command(() => { SystemServices.ConfigureApo(); return Task.CompletedTask; });
        SoundSettingsCommand = Command(() => { SystemServices.Open("ms-settings:sound"); return Task.CompletedTask; });
        AdminCommand = Command(() => {
            SaveDraft(); Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--wait-for-process {Environment.ProcessId}") { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown(); return Task.CompletedTask;
        });
        TestCommand = Command(async () => {
            ValidateOutput(); testCancellation?.Cancel(); testCancellation = new();
            testDspVerified = false; testRunning = true;
            Message = $"Playing {TestKind} on {SelectedDevice!.Name}. Test level is limited; start at low volume.";
            try { await new AudioTests().Play(SelectedDevice.Id, TestKind, testCancellation.Token); }
            finally { testRunning = false; }
            Message = testDspVerified ? "Test finished. DSP activity was observed on this endpoint. Assess channel position by ear, then continue setup." : "Test finished. DSP activity was not observed. Check APO attachment and run diagnostics.";
        });
        StopTestCommand = Command(() => { testCancellation?.Cancel(); return Task.CompletedTask; });
        NextCommand = Command(async () => {
            switch (setupStep) {
                case 0: await ScanAsync(); break;
                case 1: ValidateOutput(); SaveDraft(); break;
                case 2: if (string.IsNullOrWhiteSpace(Headphones)) throw new InvalidOperationException("Enter a headphone model or Unknown."); SaveDraft(); break;
                case 3: RequireApo(); break;
                case 4: await ApplyAsync(true); break;
                case 5: if (!IsOn || !testDspVerified) throw new InvalidOperationException("Play a test with WarTune ON. DSP activity must be observed before continuing."); break;
            }
            if (setupStep < 6) setupStep++; Notify(nameof(SetupTitle)); Notify(nameof(SetupDescription)); Notify(nameof(SetupProgress));
        });
        BackCommand = Command(() => { setupStep = Math.Max(0, setupStep - 1); Notify(nameof(SetupTitle)); Notify(nameof(SetupDescription)); Notify(nameof(SetupProgress)); return Task.CompletedTask; });
        OpenDataCommand = Command(() => { Directory.CreateDirectory(DataDirectory); SystemServices.Open(DataDirectory); return Task.CompletedTask; });
        meter.Tick += MeterTick; meter.Start();
        refreshDelay.Tick += async (_, _) => { refreshDelay.Stop(); try { await ScanAsync(); } catch (Exception ex) { Error(ex); } };
        try { notifications = new(() => Application.Current.Dispatcher.BeginInvoke(() => { refreshDelay.Stop(); refreshDelay.Start(); })); }
        catch (System.Runtime.InteropServices.COMException ex) { Error(ex); }
        if (store.Warning != null) Message = store.Warning;
        InitializePresentation();
    }
    private ICommand Command(Func<Task> run) => new AsyncCommand(run, Error);
    public void Error(Exception ex)
    {
        Message = ex is UnauthorizedAccessException ? "Configuration access denied. Use 'Run as administrator' in Audio Setup, then retry. No audio driver is installed automatically." : ex.Message;
        messageIsError = true; Notify(nameof(MessageIsError));
    }
    public async Task ScanAsync()
    {
        scan = await Task.Run(audio.Scan); Devices.Clear(); foreach (var d in scan.Devices) Devices.Add(d);
        string storedId = settings.DeviceId;
        SelectedDevice = string.IsNullOrEmpty(storedId) ? Devices.FirstOrDefault(d => d.Id == scan.DefaultId) : Devices.FirstOrDefault(d => d.Id == storedId);
        string? path = SystemServices.ApoConfigDirectory(); apo = path == null ? null : new(path, PluginPath);
        if (path != null && configWatcher == null) {
            configWatcher = new(path, "*.txt") { IncludeSubdirectories = true, EnableRaisingEvents = true };
            FileSystemEventHandler handler = (_, _) => Application.Current.Dispatcher.BeginInvoke(() => { try { RefreshStatus(); } catch (Exception ex) { Error(ex); } });
            configWatcher.Changed += handler; configWatcher.Created += handler; configWatcher.Deleted += handler;
        }
        Notify(nameof(HardwareSummary)); Notify(nameof(DefaultOutput)); Notify(nameof(SupportedRates)); RefreshStatus();
        Message = $"Detected {Devices.Count} audio outputs. Select the device you use for playback.";
    }
    private void ValidateOutput()
    {
        if (SelectedDevice == null || !Devices.Any(d => d.Id == SelectedDevice.Id)) throw new InvalidOperationException("Select an available playback endpoint.");
        if (SelectedDevice.Channels != 2 || SelectedDevice.SampleRate is < 44100 or > 96000)
            throw new InvalidOperationException("This release requires stereo at 44.1–96 kHz. Change the Windows format, then scan again.");
    }
    private AudioProfile Draft(string? name = null)
    {
        var chosen = SelectedProfile;
        bool same = chosen != null && chosen.Footsteps == Footsteps && chosen.Gunshots == Gunshots && chosen.Explosions == Explosions && chosen.Intensity == Intensity;
        return new(name ?? (same ? chosen!.Name : "Custom"), Footsteps, Gunshots, Explosions, Intensity);
    }
    private void SaveDraft() { settings.Profile = Draft(); settings.Headphones = Headphones.Trim(); store.Save(settings); Notify(nameof(ProfileSummary)); }
    private ApoConfiguration RequireApo() => apo ?? throw new InvalidOperationException("Equalizer APO is missing. Open the official installer page, install it, and scan again.");
    public async Task ApplyAsync(bool enable)
    {
        testDspVerified = false;
        await ScanAsync(); ValidateOutput(); SaveDraft();
        if (enable) RequireApo().Enable(settings);
        RefreshStatus(); Message = "Configuration enabled. Live DSP activity must still be observed during playback. This tuning has not been acoustically validated in Black Ops 7.";
    }
    public async Task SelectPresetAsync(AudioProfile profile)
    { SelectedProfile = profile; if (IsOn) await ApplyAsync(true); else SaveDraft(); }
    private List<EqFilter> FilterList() => FilterTarget == "Headphone correction" ? settings.HeadphoneFilters : settings.AdvancedFilters;
    private EqFilter EditedFilter()
    { var f = new EqFilter(Enum.Parse<FilterType>(FilterTypeName), FilterFrequency, FilterGain, FilterQ, FilterEnabled); f.Validate(); return f; }
    private void RefreshFilters() { Filters.Clear(); foreach (var f in FilterList()) Filters.Add(f); Notify(nameof(ResponseFilters)); }
    private void SaveFilters() { settings.Validate(); store.Save(settings); RefreshFilters(); Message = "Filters saved. Apply to update enabled processing."; }
    public async Task RunDiagnosticsAsync()
    {
        await ScanAsync();
        var driver = await SystemServices.DriverReport();
        Diagnostics = DiagnosticReport.Build(scan!, settings, apo, PluginPath, DspActivity.Observed(settings.DeviceId, out _)) + "\n\nWindows audio drivers (local display only; excluded from export):\n" + driver;
    }
    private void RefreshStatus() { enabled = apo?.IsEnabled == true; Notify(nameof(IsOn)); Notify(nameof(ToggleLabel)); Notify(nameof(ApoStatus)); }
    private void RefreshHeadphones() { HeadphoneLibrary.Clear(); foreach (var p in settings.HeadphoneProfiles.Where(p => p.Name.Contains(headphoneSearch, StringComparison.OrdinalIgnoreCase))) HeadphoneLibrary.Add(p); }
    private void MeterTick(object? sender, EventArgs args)
    {
        if (!Application.Current.MainWindow.IsVisible || Application.Current.MainWindow.WindowState == System.Windows.WindowState.Minimized) return;
        bool observed = DspActivity.Observed(settings.DeviceId, out var reduction);
        if (testRunning && IsOn && observed) testDspVerified = true;
        DspStatus = IsOn && observed ? $"Processing observed · {reduction:F1} dB reduction" : IsOn ? "Enabled · processing not observed" : "Bypassed";
        if (SelectedDevice == null) { Peak = 0; return; }
        try { Peak = audio.Peak(SelectedDevice.Id) * 100; }
        catch (System.Runtime.InteropServices.COMException) { audio.Dispose(); Peak = 0; DspStatus = "Output unavailable — scan devices"; }
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Dispose() { meter.Stop(); refreshDelay.Stop(); configWatcher?.Dispose(); notifications?.Dispose(); audio.Dispose(); testCancellation?.Cancel(); testCancellation?.Dispose(); }
}
