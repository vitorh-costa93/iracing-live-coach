using System.Globalization;

namespace IracingLiveCoach.Core.Telemetry;

/// <summary>
/// Kapps' licence / iRating presentation, pure and unit-tested. Evidence (Kapps prints vs SDK strings,
/// 24/09/2026): "A 1.43" -> "A1.4", "B 1.16" -> "B1.1", "A 2.37" -> "A2.3" (letter + SR TRUNCATED to one
/// decimal, no space); iRating 3965 -> "3.9k", 3469 -> "3.4k", AI 0 -> "0.0k" (thousands truncated to one
/// decimal). Pill colour by licence letter, sampled from the prints: R #DE251B, A #006EFF, B #33CC00;
/// C/D/P follow iRacing's own licence colours (no Kapps print yet).
/// </summary>
public static class LicenseStyle
{
    public static (string Letter, double? SafetyRating) Split(string? sdkLicString)
    {
        if (string.IsNullOrWhiteSpace(sdkLicString)) return ("", null);
        var s = sdkLicString.Trim();
        int space = s.IndexOf(' ');
        string letter = space < 0 ? s : s[..space];
        double? sr = space >= 0 && double.TryParse(s[(space + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        return (letter, sr);
    }

    public static string TruncatedOneDecimal(double value) =>
        (Math.Floor(value * 10 + 1e-9) / 10).ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>"A1.4" (Kapps); a string without a number ("Pro") is returned as-is.</summary>
    public static string KappsLicense(string? sdkLicString)
    {
        var (letter, sr) = Split(sdkLicString);
        return sr is double v ? letter + TruncatedOneDecimal(v) : sdkLicString ?? "";
    }

    /// <summary>"3.4k" (Kapps), "0.0k" for an AI/unrated driver.</summary>
    public static string KappsIRating(int iRating) => TruncatedOneDecimal(Math.Max(0, iRating) / 1000.0) + "k";

    /// <summary>Pill colour (#RRGGBB) for the licence letter; null when unknown.</summary>
    public static string? ColorHex(string? sdkLicString) => Split(sdkLicString).Letter.ToUpperInvariant() switch
    {
        "R" => "#DE251B",
        "D" => "#FC8A27",
        "C" => "#FFCC00",
        "B" => "#33CC00",
        "A" => "#006EFF",
        "P" or "PRO" or "WC" => "#000000",
        _ => null,
    };
}
