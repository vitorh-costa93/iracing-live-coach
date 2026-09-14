// src/IracingLiveCoach.App/StandingsWidgetViewModel.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace IracingLiveCoach.App;

public class StandingsRowViewModel
{
    public string PositionText { get; }
    public string FlagAndCode { get; }
    public Brush FlagBackground { get; }
    public Brush FlagSymbolBrush { get; }
    public string FlagSymbol { get; }
    public string FlagImagePath { get; }
    public string ManufacturerText { get; }
    public string BrandPathData { get; }
    public Brush BrandColorBrush { get; }
    public bool HasBrandIcon { get; }
    public string BrandImagePath { get; }
    public bool HasBrandImage { get; }
    public bool HasBrandPath { get; }
    public string LicText { get; }
    public Brush LicBrush { get; }
    public string IRatingText { get; }
    public string IRatingDeltaText { get; }
    public Brush IRatingDeltaBrush { get; }
    public string GapText { get; }
    public string LastLapText { get; }
    public string LapDeltaText { get; }
    public Brush LapDeltaBrush { get; }
    public bool IsPlayer { get; }
    public Brush RowForegroundBrush { get; }
    public Brush PositionBrush { get; }
    public string ClassBadgeText { get; }
    public bool HasClassBadge { get; }
    public Brush ClassAccentBrush { get; }

    private static readonly Brush LicFallbackBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAF));
    private static readonly System.Collections.Generic.Dictionary<string, Brush> LicBrushCache = new();
    private static readonly Brush GainBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0xE2, 0xB2));
    private static readonly Brush LossBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0x48, 0x3D));

    public StandingsRowViewModel(StandingsRow row)
    {
        IsPlayer = row.IsPlayer;
        PositionText = row.Position.ToString(CultureInfo.InvariantCulture);
        FlagAndCode = row.DriverCode;
        (FlagBackground, FlagSymbolBrush, FlagSymbol) = FlagStyle.For(row.FlagEmoji);
        FlagImagePath = FlagStyle.ImageFor(row.FlagEmoji);
        ManufacturerText = row.ManufacturerBadge;
        var brandIcon = BrandIcons.TryGet(row.ManufacturerBadge);
        BrandImagePath = BrandIcons.TryGetImage(row.ManufacturerBadge) ?? "";
        HasBrandImage = BrandImagePath.Length > 0;
        HasBrandPath = !HasBrandImage && brandIcon is not null;
        HasBrandIcon = HasBrandImage || HasBrandPath;
        BrandPathData = brandIcon?.PathData ?? "";
        BrandColorBrush = brandIcon is (_, string hex) ? ParseLicColor(hex) : LicFallbackBrush;
        LicText = row.LicString;
        LicBrush = ParseLicColor(row.LicColorHex);
        IRatingText = row.IRating > 0 ? row.IRating.ToString("N0", CultureInfo.InvariantCulture) : "--";
        LastLapText = row.LastLapTime is double t ? IracingLiveCoach.Core.LapTimeFormatting.Format(t) : "--";

        // Position by class -- "igual funciona no Kapps hoje" (14/09/2026): a multiclass field's
        // overall position alone hides where a car stands within its own class. CarClassPosition
        // and CarClassColor are both real DriverModel fields (see GetIdentity's own doc comment) --
        // CarClassColor is iRacing's OWN per-class color assignment, so the accent bar matches
        // whatever color the sim itself already uses for that class, not an invented palette.
        HasClassBadge = !string.IsNullOrEmpty(row.ClassShortName);
        ClassBadgeText = HasClassBadge ? $"P{row.ClassPosition} {row.ClassShortName}" : "";
        ClassAccentBrush = row.ClassColorHex is not null
            ? ParseLicColor(row.ClassColorHex)
            : (Brush)System.Windows.Application.Current.Resources["F1AccentBrush"];

        // The player's own row gets a dark foreground since its Border background is the bright
        // F1HighlightBrush cyan -- F1TextBrush's near-white would be nearly illegible against it.
        RowForegroundBrush = IsPlayer
            ? new SolidColorBrush(Color.FromRgb(0x0B, 0x14, 0x20))
            : (Brush)System.Windows.Application.Current.Resources["F1TextBrush"];
        // The position number normally uses F1AccentBrush's red-orange, which -- like F1TextBrush's
        // near-white -- fails contrast against the highlighted row's cyan background; reuse the same
        // dark ink for the player's own row instead of the accent color.
        PositionBrush = IsPlayer
            ? RowForegroundBrush
            : (Brush)System.Windows.Application.Current.Resources["F1AccentBrush"];

        // "LEADER" for P1 -- CarIdxF2Time reads ~0 for the leader rather than null, so the text is
        // keyed off position, not a missing value (a missing value means the session just doesn't
        // publish gap data, e.g. outside a race -- shown as "--").
        GapText = row.Position == 1
            ? "LEADER"
            : row.GapToLeaderSeconds is double gap ? $"+{gap.ToString("0.000", CultureInfo.InvariantCulture)}" : "--";

        // ΔiR* compiled into the same iR column (not a separate one) per the driver's own request
        // (14/09/2026, "não quero uma coluna adicional... o Kapps faz isso compilado em uma só") --
        // see StandingsRow's own doc comment: an approximation, never a real SDK value.
        IRatingDeltaText = row.EstimatedDeltaIRating is double deltaIR
            ? (deltaIR >= 0 ? "+" : "") + deltaIR.ToString("0", CultureInfo.InvariantCulture)
            : "";
        IRatingDeltaBrush = IsPlayer ? RowForegroundBrush : row.EstimatedDeltaIRating is double d && d < 0 ? LossBrush : GainBrush;

        // Δ VOLTA = this driver's own last lap - the player's own last lap (the mockup's own
        // footnote convention), so negative ("faster than you") is green and positive is red --
        // the OPPOSITE polarity from ΔiR* above, which is intentional (matches the mockup).
        if (row.LapDeltaVsPlayerSeconds is double lapDelta)
        {
            LapDeltaText = lapDelta == 0 ? "0.000" : (lapDelta > 0 ? "+" : "") + lapDelta.ToString("0.000", CultureInfo.InvariantCulture);
            LapDeltaBrush = IsPlayer ? RowForegroundBrush : lapDelta < 0 ? GainBrush : lapDelta > 0 ? LossBrush : RowForegroundBrush;
        }
        else
        {
            LapDeltaText = "--";
            LapDeltaBrush = RowForegroundBrush;
        }
    }

    // Same defensive parse as RelativeWidgetViewModel's own ParseLicColor -- LicColor is a
    // String on the real IRSDKSharper 1.3.0 type, format not independently documented.
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

public class StandingsWidgetViewModel : INotifyPropertyChanged
{
    private string _classSessionText = "--";
    private string _lapText = "--";
    private string _flagText = "--";
    private Brush _flagBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAF));
    private string _sofText = "SOF --";
    private string _driverCountText = "-- PILOTOS";

    public string ClassSessionText { get => _classSessionText; private set => Set(ref _classSessionText, value); }
    public string LapText { get => _lapText; private set => Set(ref _lapText, value); }
    public string FlagText { get => _flagText; private set => Set(ref _flagText, value); }
    public Brush FlagBrush { get => _flagBrush; private set => Set(ref _flagBrush, value); }
    public string SofText { get => _sofText; private set => Set(ref _sofText, value); }
    public string DriverCountText { get => _driverCountText; private set => Set(ref _driverCountText, value); }

    public ObservableCollection<StandingsRowViewModel> Rows { get; } = new();

    private System.Collections.Generic.List<StandingsRow> _lastRows = new();
    private int _myClassLimit;
    private int _otherClassLimit = 3;

    public void SetRows(System.Collections.Generic.List<StandingsRow> rows)
    {
        _lastRows = rows;
        ApplyGroupedRows();
    }

    // "Em Standings as classes não se misturam, igual no Kapps" (14/09/2026): the player's own
    // class is shown first as its own contiguous block (never interleaved with other classes by
    // overall position), followed by each other class as its own block -- ordered by whichever
    // class is currently running highest overall, matching how Kapps itself orders class groups.
    // A limit of 0 means "show every row of that group".
    public void SetClassRowLimits(int myClassLimit, int otherClassLimit)
    {
        _myClassLimit = myClassLimit;
        _otherClassLimit = otherClassLimit;
        ApplyGroupedRows();
    }

    private void ApplyGroupedRows()
    {
        var rows = _lastRows;
        var player = rows.Find(r => r.IsPlayer);
        System.Collections.Generic.List<StandingsRow> grouped;
        if (player is null)
        {
            // No player row classified yet (e.g. in the pits before joining) -- show the field
            // exactly as received rather than guess at a grouping.
            grouped = rows;
        }
        else
        {
            var myClass = rows.FindAll(r => r.CarClassId == player.CarClassId);
            myClass.Sort((a, b) => a.Position.CompareTo(b.Position));
            if (_myClassLimit > 0 && myClass.Count > _myClassLimit) myClass = myClass.GetRange(0, _myClassLimit);

            var otherClassIds = new System.Collections.Generic.List<int>();
            foreach (var r in rows)
                if (r.CarClassId != player.CarClassId && !otherClassIds.Contains(r.CarClassId))
                    otherClassIds.Add(r.CarClassId);
            // Classes ordered by whichever is currently running highest overall (its own best position).
            otherClassIds.Sort((a, b) =>
            {
                var bestA = rows.FindAll(r => r.CarClassId == a).ConvertAll(r => r.Position).Min();
                var bestB = rows.FindAll(r => r.CarClassId == b).ConvertAll(r => r.Position).Min();
                return bestA.CompareTo(bestB);
            });

            grouped = new System.Collections.Generic.List<StandingsRow>(myClass);
            foreach (var classId in otherClassIds)
            {
                var classRows = rows.FindAll(r => r.CarClassId == classId);
                classRows.Sort((a, b) => a.Position.CompareTo(b.Position));
                if (_otherClassLimit > 0 && classRows.Count > _otherClassLimit) classRows = classRows.GetRange(0, _otherClassLimit);
                grouped.AddRange(classRows);
            }
        }

        Rows.Clear();
        foreach (var row in grouped) Rows.Add(new StandingsRowViewModel(row));
    }

    // All real SDK fields except StrengthOfField (see SessionStatus's own doc comment for its formula).
    public void ApplySessionStatus(SessionStatus status)
    {
        ClassSessionText = $"{status.CarClassShortName}  •  {status.SessionTypeText}";
        LapText = status.CurrentLap is int lap
            ? (status.TotalLaps is int total ? $"LAP {lap} / {total}" : $"LAP {lap}")
            : "--";
        FlagText = status.SessionFlagText;
        FlagBrush = ParseColor(status.SessionFlagColorHex);
        SofText = status.StrengthOfField is double sof ? $"SOF {sof.ToString("0", CultureInfo.InvariantCulture)}" : "SOF --";
        DriverCountText = $"{status.DriverCount} PILOTOS";
    }

    private static Brush ParseColor(string hex)
    {
        try { return new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!); }
        catch { return new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAF)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
