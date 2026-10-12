using System.Numerics;

namespace ArtIsWar.Core;

public static class EqResponse
{
    public static double Decibels(IEnumerable<EqFilter> filters, double frequency, double sampleRate)
    {
        var z = Complex.FromPolarCoordinates(1, -2 * Math.PI * frequency / sampleRate);
        double gain = 0;
        foreach (var f in filters.Where(f => f.Enabled))
        {
            f.Validate();
            double a = Math.Pow(10, f.Gain / 40), w = 2 * Math.PI * Math.Min(f.AppliedFrequency, sampleRate * .49) / sampleRate;
            double c = Math.Cos(w), alpha = Math.Sin(w) / (2 * f.Q), root = 2 * Math.Sqrt(a) * alpha;
            (double b0, double b1, double b2, double a0, double a1, double a2) = f.Type switch {
                FilterType.PK => (1 + alpha * a, -2 * c, 1 - alpha * a, 1 + alpha / a, -2 * c, 1 - alpha / a),
                FilterType.HPQ => ((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + alpha, -2 * c, 1 - alpha),
                FilterType.LPQ => ((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + alpha, -2 * c, 1 - alpha),
                FilterType.LSC => (a * ((a + 1) - (a - 1) * c + root), 2 * a * ((a - 1) - (a + 1) * c), a * ((a + 1) - (a - 1) * c - root), (a + 1) + (a - 1) * c + root, -2 * ((a - 1) + (a + 1) * c), (a + 1) + (a - 1) * c - root),
                FilterType.HSC => (a * ((a + 1) + (a - 1) * c + root), -2 * a * ((a - 1) + (a + 1) * c), a * ((a + 1) + (a - 1) * c - root), (a + 1) - (a - 1) * c + root, 2 * ((a - 1) - (a + 1) * c), (a + 1) - (a - 1) * c - root),
                _ => throw new InvalidDataException("Unsupported filter.") };
            gain += 20 * Math.Log10(Math.Max(1e-9, ((b0 + b1 * z + b2 * z * z) / (a0 + a1 * z + a2 * z * z)).Magnitude));
        }
        return gain;
    }
}
