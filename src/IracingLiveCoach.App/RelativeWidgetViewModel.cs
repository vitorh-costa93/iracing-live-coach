// src/IracingLiveCoach.App/RelativeWidgetViewModel.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace IracingLiveCoach.App;

public class FullRelativeRowViewModel
{
    public string PositionText { get; }
    public string FlagAndCode { get; }
    public string ManufacturerText { get; }
    public string LicText { get; }
    public Brush LicBrush { get; }
    public string IRatingText { get; }
    public string GapText { get; }
    public string P2PText { get; }
    public Brush P2PBrush { get; }
    public bool IsPlayerRow { get; }
    public Brush RowForegroundBrush { get; }

    private static readonly Brush P2PActiveBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0x48, 0x3D));
    private static readonly Brush P2PCooldownBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xA5, 0x2C));
    private static readonly Brush P2PIdleBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAF));
    private static readonly Brush LicFallbackBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAF));
    private static readonly System.Collections.Generic.Dictionary<string, Brush> LicBrushCache = new();

    public FullRelativeRowViewModel(RelativeRow row, bool showClassPositionAsAbsolute)
    {
        // For the secondary (class-scoped) instance, PositionOffset is an absolute class position
        // (never the player's own row, since the player doesn't race in that class) -- only the
        // primary, player-relative instance can ever have a real "this is me" row.
        IsPlayerRow = !showClassPositionAsAbsolute && row.PositionOffset == 0;
        PositionText = showClassPositionAsAbsolute
            ? row.PositionOffset.ToString(CultureInfo.InvariantCulture)
            : (row.PositionOffset > 0 ? "+" : "") + row.PositionOffset.ToString(CultureInfo.InvariantCulture);
        // Manufacturer badge folded inline (no separate column) to keep this widget's column count
        // matching the mockup's own Relative layout -- Standings (Task 4) gives it its own column
        // instead, since that mockup panel shows the badge more prominently.
        FlagAndCode = string.IsNullOrEmpty(row.ManufacturerBadge)
            ? $"{row.FlagEmoji} {row.DriverCode}"
            : $"{row.FlagEmoji} {row.DriverCode} · {row.ManufacturerBadge}";
        ManufacturerText = row.ManufacturerBadge;
        LicText = row.LicString;
        LicBrush = ParseLicColor(row.LicColorHex);
        IRatingText = row.IRating > 0 ? row.IRating.ToString("N0", CultureInfo.InvariantCulture) : "--";
        GapText = row.GapSeconds is double gap ? $"{(gap >= 0 ? "+" : "")}{gap.ToString("0.0", CultureInfo.InvariantCulture)}" : "--";

        // P2PSecondsRemaining/P2PInCooldown are derived from the SF23's own documented Overtake
        // System rules (20s active/100s cooldown) applied to the real CarIdxP2P_Status transition
        // -- see TelemetryReader.UpdateP2PPhase's own doc comment for the source and caveat.
        if (row.P2PActive is bool active)
        {
            var uses = row.P2PUsesRemaining is int u ? u.ToString(CultureInfo.InvariantCulture) : "?";
            if (active && row.P2PSecondsRemaining is double activeRemaining)
            {
                P2PText = $"ATIVO {activeRemaining.ToString("0", CultureInfo.InvariantCulture)}s ({uses})";
                P2PBrush = P2PActiveBrush;
            }
            else if (row.P2PInCooldown && row.P2PSecondsRemaining is double cooldownRemaining)
            {
                P2PText = $"RECARGA {cooldownRemaining.ToString("0", CultureInfo.InvariantCulture)}s ({uses})";
                P2PBrush = P2PCooldownBrush;
            }
            else
            {
                // Qualitative state always wins over a missing countdown number -- showing PRONTO
                // while the car's P2P is genuinely active would be worse than a number-less ATIVO.
                P2PText = active ? $"ATIVO ({uses})" : $"PRONTO ({uses})";
                P2PBrush = active ? P2PActiveBrush : P2PIdleBrush;
            }
        }
        else
        {
            P2PText = "";
            P2PBrush = P2PIdleBrush;
        }

        // The player's own row gets a dark foreground since its Border background is the bright
        // F1HighlightBrush cyan -- F1TextBrush's near-white would be nearly illegible against it.
        RowForegroundBrush = IsPlayerRow
            ? new SolidColorBrush(Color.FromRgb(0x0B, 0x14, 0x20))
            : (Brush)System.Windows.Application.Current.Resources["F1TextBrush"];
    }

    // LicColor is a String on the real IRSDKSharper 1.3.0 DriverModel type (confirmed via
    // reflection -- corrected from this session's own earlier packed-int assumption). iRacing's
    // own YAML color-string convention is "0xRRGGBB" or "#RRGGBB"; parsed defensively since this
    // field's exact format is not independently documented beyond its type.
    private static Brush ParseLicColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return LicFallbackBrush;
        if (LicBrushCache.TryGetValue(hex, out var cached)) return cached;
        try
        {
            var cleaned = hex.Trim();
            if (cleaned.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase)) cleaned = "#" + cleaned[2..];
            if (!cleaned.StartsWith("#")) cleaned = "#" + cleaned;
            var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(cleaned)!;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            LicBrushCache[hex] = brush;
            return brush;
        }
        catch
        {
            return LicFallbackBrush;
        }
    }
}

public class RelativeWidgetViewModel : INotifyPropertyChanged
{
    private string _brakeBiasText = "--";
    private string _trackRubberText = "--";
    private string _bestLapText = "--";
    private string _lastLapText = "--";

    public string BrakeBiasText { get => _brakeBiasText; private set => Set(ref _brakeBiasText, value); }
    public string TrackRubberText { get => _trackRubberText; private set => Set(ref _trackRubberText, value); }
    public string BestLapText { get => _bestLapText; private set => Set(ref _bestLapText, value); }
    public string LastLapText { get => _lastLapText; private set => Set(ref _lastLapText, value); }

    public ObservableCollection<FullRelativeRowViewModel> Rows { get; } = new();

    // showClassPositionAsAbsolute=true for the secondary (class-scoped) instance, whose
    // PositionOffset carries an absolute class position, not an offset from the player.
    public void SetRows(System.Collections.Generic.List<RelativeRow> rows, bool showClassPositionAsAbsolute = false)
    {
        Rows.Clear();
        foreach (var row in rows) Rows.Add(new FullRelativeRowViewModel(row, showClassPositionAsAbsolute));
    }

    public void ApplyPlayerStatus(PlayerCarStatus status)
    {
        BrakeBiasText = status.BrakeBiasPct is double bias ? bias.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "--";
        TrackRubberText = status.TrackRubberState ?? "--";
        if (status.BestLapTimeSeconds is double best) BestLapText = IracingLiveCoach.Core.LapTimeFormatting.Format(best);
        LastLapText = status.LastLapTimeSeconds is double last ? IracingLiveCoach.Core.LapTimeFormatting.Format(last) : "--";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
