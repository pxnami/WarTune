using System.Globalization;
using System.Text.RegularExpressions;

namespace ArtIsWar.Core;

public enum FilterType { PK, LSC, HSC, HPQ, LPQ }
public sealed record EqFilter(FilterType Type, double Frequency, double Gain, double Q, bool Enabled = true)
{
    internal double AppliedFrequency => Math.Round(Frequency, MidpointRounding.AwayFromZero);
    public void Validate()
    {
        if (!Enum.IsDefined(Type) || !double.IsFinite(Frequency) || Frequency is < 20 or > 20000 ||
            !double.IsFinite(Gain) || Gain is < -24 or > 12 || !double.IsFinite(Q) || Q is < .1 or > 20)
            throw new InvalidDataException("Filters require 20–20,000 Hz, −24 to +12 dB and Q 0.1–20.");
    }
    public string ToApo(int index)
    {
        Validate();
        var gain = Type is FilterType.HPQ or FilterType.LPQ ? "" : FormattableString.Invariant($" Gain {Gain:F3} dB");
        return FormattableString.Invariant($"Filter {index}: {(Enabled ? "ON" : "OFF")} {Type} Fc {AppliedFrequency:F0} Hz{gain} Q {Q:F3}");
    }
}

public sealed record AudioProfile(string Name, int Footsteps, int Gunshots, int Explosions, int Intensity)
{
    public override string ToString() => Name;
    public static readonly AudioProfile[] Presets = [
        new("Balanced", 45, 40, 40, 45), new("Competitive", 65, 65, 60, 70),
        new("Maximum Footsteps", 90, 85, 80, 95), new("Late Night", 55, 95, 85, 85)];
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 120 || Name.Any(char.IsControl) || new[] { Footsteps, Gunshots, Explosions, Intensity }.Any(v => v is < 0 or > 100))
            throw new InvalidDataException("Profile controls must be between 0 and 100.");
    }
    public List<EqFilter> Filters()
    {
        Validate();
        double strength = Intensity / 100.0;
        return [new(FilterType.HPQ, 35, 0, .707),
            new(FilterType.LSC, 150, -8 * Explosions / 100.0 * strength, .707),
            new(FilterType.PK, 350, -2.5 * strength, .8),
            new(FilterType.PK, 1800, 3 * Footsteps / 100.0 * strength, .9),
            new(FilterType.PK, 3300, 2 * Footsteps / 100.0 * strength, 1.1),
            new(FilterType.HSC, 7500, -2 * Gunshots / 100.0 * strength, .707)];
    }
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public string DeviceId { get; set; } = "";
    public string Headphones { get; set; } = "Unspecified — select manually";
    public AudioProfile Profile { get; set; } = AudioProfile.Presets[1];
    public List<AudioProfile> SavedProfiles { get; set; } = [];
    public List<EqFilter> HeadphoneFilters { get; set; } = [];
    public List<EqFilter> AdvancedFilters { get; set; } = [];
    public List<HeadphoneProfile> HeadphoneProfiles { get; set; } = [];
    public double HeadphonePreamp { get; set; }
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool Multiband { get; set; }
    public void Validate()
    {
        if (Profile == null || SavedProfiles == null || HeadphoneFilters == null || AdvancedFilters == null || HeadphoneProfiles == null || DeviceId == null || Headphones == null)
            throw new InvalidDataException("Settings contain missing values.");
        if (SavedProfiles.Any(p => p is null) || HeadphoneFilters.Any(f => f is null) || AdvancedFilters.Any(f => f is null) || HeadphoneProfiles.Any(p => p is null))
            throw new InvalidDataException("Settings contain empty profile/filter entries.");
        if (Version != 1) throw new InvalidDataException("Unsupported settings version.");
        Profile.Validate();
        if (Headphones.Contains('\n') || DeviceId.Contains('\n') || DeviceId.Contains('"') || !double.IsFinite(HeadphonePreamp) || HeadphonePreamp is < -30 or > 0)
            throw new InvalidDataException("Invalid audio settings.");
        if (HeadphoneFilters.Count + AdvancedFilters.Count > 64) throw new InvalidDataException("At most 64 custom filters are supported.");
        foreach (var f in HeadphoneFilters.Concat(AdvancedFilters)) f.Validate();
        foreach (var p in SavedProfiles) p.Validate();
        foreach (var p in HeadphoneProfiles) {
            if (string.IsNullOrWhiteSpace(p.Name) || p.Filters == null || p.Filters.Any(f => f is null) || !double.IsFinite(p.Preamp) || p.Preamp is < -30 or > 0)
                throw new InvalidDataException("Invalid headphone profile.");
            foreach (var f in p.Filters) f.Validate();
        }
    }
}
public sealed record HeadphoneProfile(string Name, List<EqFilter> Filters, double Preamp)
{
    public override string ToString() => Name;
}

public static partial class PeqImport
{
    [GeneratedRegex(@"^Filter\s*\d*:\s*(ON|OFF)\s+(PK|PEQ|LS|LSC|HS|HSC|HP|HPQ|LP|LPQ)\s+Fc\s+([\d.]+)\s+Hz(?:\s+Gain\s+([-+\d.]+)\s+dB)?(?:\s+Q\s+([\d.]+))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex FilterPattern();
    public static (List<EqFilter> Filters, double Preamp) Parse(string text)
    {
        List<EqFilter> filters = [];
        double preamp = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("Preamp:", StringComparison.OrdinalIgnoreCase))
            {
                preamp = double.Parse(line[7..].Replace("dB", "", StringComparison.OrdinalIgnoreCase).Trim(), CultureInfo.InvariantCulture);
                if (!double.IsFinite(preamp) || preamp is < -30 or > 0) throw new InvalidDataException("Imported preamp must be −30 to 0 dB.");
                continue;
            }
            var match = FilterPattern().Match(line);
            if (!match.Success) throw new InvalidDataException($"Unsupported PEQ line: {line}");
            var type = match.Groups[2].Value.ToUpperInvariant() switch { "PEQ" => "PK", "LS" => "LSC", "HS" => "HSC", "HP" => "HPQ", "LP" => "LPQ", var t => t };
            var f = new EqFilter(Enum.Parse<FilterType>(type), double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                match.Groups[4].Success ? double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) : 0,
                match.Groups[5].Success ? double.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture) : .707,
                match.Groups[1].Value.Equals("ON", StringComparison.OrdinalIgnoreCase));
            f.Validate(); filters.Add(f);
        }
        if (filters.Count is 0 or > 32) throw new InvalidDataException("Import requires 1–32 supported filters.");
        return (filters, preamp);
    }
}
