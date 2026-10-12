using System.Text.Json;

namespace ArtIsWar.App.ViewModels;

public sealed partial class MainViewModel
{
    private string profileSearch = "";
    public string ProfileSearch { get => profileSearch; set { profileSearch = value; Notify(); Notify(nameof(ProfileLibrary)); } }
    public IEnumerable<ArtIsWar.Core.AudioProfile> ProfileLibrary => Profiles.Where(p => p.Name.Contains(ProfileSearch, StringComparison.OrdinalIgnoreCase));
    public string ReferencePageTitle => Page switch { 3 => "AutoEQ", 5 => "Setup", _ => PageTitle };
    public bool SetupDetailsExpanded => setupStep is >= 1 and <= 5;
    public IEnumerable<SetupStage> ReferenceSetupStages
    {
        get
        {
            int current = setupStep switch { 0 => 0, <= 3 => 1, <= 5 => 2, _ => 3 };
            string[] names = ["Detect", "Software", "Configure", "Complete"];
            return names.Select((name, index) => new SetupStage(index < current || setupStep == 6 ? "✓" : "", name,
                index < current || setupStep == 6 ? "#40C584" : index == current ? "#FFD600" : "#3A3A3A"));
        }
    }
    public sealed record SetupStage(string Number, string Name, string Color);
    public sealed record DiagnosticRow(string Check, string Value);

    public IEnumerable<SetupStage> SetupStages => SetupSteps.Select((name, index) =>
        new SetupStage(index < setupStep ? "✓" : (index + 1).ToString(), name,
            index < setupStep ? "#419B69" : index == setupStep ? "#94282E" : "#343438"));

    public string EqChainSummary
    {
        get
        {
            var filters = ResponseFilters.Where(f => f.Enabled).ToArray();
            double preamp = -Math.Max(3, filters.Sum(f => Math.Max(0, f.Gain)) + 1) + settings.HeadphonePreamp;
            return $"DRAFT · PREAMP {preamp:F1} dB · PEQ {filters.Length} · 48 kHz";
        }
    }
    public IEnumerable<DiagnosticRow> DiagnosticRows
    {
        get
        {
            int end = Diagnostics.IndexOf("\n\nWindows audio drivers", StringComparison.Ordinal);
            string report = end < 0 ? Diagnostics : Diagnostics[..end];
            if (!report.TrimStart().StartsWith('{')) return [new("Report", "Not checked — run diagnostics")];
            try
            {
                using var document = JsonDocument.Parse(report);
                return document.RootElement.EnumerateObject()
                    .Where(p => p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                    .Select(p => new DiagnosticRow(p.Name, p.Value.ToString())).ToArray();
            }
            catch (JsonException) { return [new("Report", "See diagnostic console")]; }
        }
    }

    private void InitializePresentation()
    {
        Profiles.CollectionChanged += (_, _) => Notify(nameof(ProfileLibrary));
        PropertyChanged += (_, e) =>
        {
        if (e.PropertyName == nameof(Page)) Notify(nameof(ReferencePageTitle));
        if (e.PropertyName == nameof(SetupProgress)) { Notify(nameof(SetupStages)); Notify(nameof(ReferenceSetupStages)); Notify(nameof(SetupDetailsExpanded)); }
        if (e.PropertyName == nameof(ResponseFilters)) Notify(nameof(EqChainSummary));
        if (e.PropertyName == nameof(Diagnostics)) Notify(nameof(DiagnosticRows));
        };
    }
}
