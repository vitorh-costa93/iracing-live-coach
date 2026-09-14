// src/IracingLiveCoach.App/StandingsWidgetViewModel.cs
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace IracingLiveCoach.App;

public class StandingsRowViewModel
{
    public string PositionText { get; }
    public string FlagAndCode { get; }
    public string ManufacturerText { get; }
    public string LicText { get; }
    public Brush LicBrush { get; }
    public string IRatingText { get; }
    public string LastLapText { get; }
    public bool IsPlayer { get; }

    private static readonly Brush LicFallbackBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAF));

    public StandingsRowViewModel(StandingsRow row)
    {
        IsPlayer = row.IsPlayer;
        PositionText = row.Position.ToString(CultureInfo.InvariantCulture);
        FlagAndCode = $"{row.FlagEmoji} {row.DriverCode}";
        ManufacturerText = row.ManufacturerBadge;
        LicText = row.LicString;
        LicBrush = ParseLicColor(row.LicColorHex);
        IRatingText = row.IRating > 0 ? row.IRating.ToString("N0", CultureInfo.InvariantCulture) : "--";
        LastLapText = row.LastLapTime is double t ? t.ToString("0.000", CultureInfo.InvariantCulture) : "--";
    }

    // Same defensive parse as RelativeWidgetViewModel's own ParseLicColor -- LicColor is a
    // String on the real IRSDKSharper 1.3.0 type, format not independently documented.
    private static Brush ParseLicColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return LicFallbackBrush;
        try
        {
            var cleaned = hex.Trim();
            if (cleaned.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase)) cleaned = "#" + cleaned[2..];
            if (!cleaned.StartsWith("#")) cleaned = "#" + cleaned;
            var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(cleaned)!;
            return new SolidColorBrush(color);
        }
        catch
        {
            return LicFallbackBrush;
        }
    }
}

public class StandingsWidgetViewModel
{
    public ObservableCollection<StandingsRowViewModel> Rows { get; } = new();

    public void SetRows(System.Collections.Generic.List<StandingsRow> rows)
    {
        Rows.Clear();
        foreach (var row in rows) Rows.Add(new StandingsRowViewModel(row));
    }
}
