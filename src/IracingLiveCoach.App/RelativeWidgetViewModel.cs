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
    public Brush FlagBackground { get; }
    public Brush FlagSymbolBrush { get; }
    public string FlagSymbol { get; }
    public string FlagImagePath { get; }
    public string ManufacturerText { get; }
    public string BrandPathData { get; }
    public Brush BrandColorBrush { get; }
    public bool HasBrandIcon { get; }
    public string BrandImagePath { get; }
    public ImageSource? BrandImage => BrandImageLoader.Load(BrandImagePath);
    public bool HasBrandImage { get; }
    public bool HasBrandPath { get; }
    public string LicText { get; }
    public Brush LicBrush { get; }
    public string IRatingText { get; }
    public string GapText { get; }
    public string P2PText { get; }
    public bool HasP2P { get; }
    public string P2PStateText { get; }
    public string P2PDetailText { get; }
    public Brush P2PBrush { get; }
    public bool IsPlayerRow { get; }
    public Brush RowForegroundBrush { get; }
    public string ClassBadgeText { get; }
    public bool HasClassBadge { get; }
    public Brush ClassAccentBrush { get; }

    private static readonly Brush P2PActiveBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xE8, 0x89));
    private static readonly Brush P2PCooldownBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xA5, 0x2C));
    private static readonly Brush P2PIdleBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAF));
    private static readonly Brush LicFallbackBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAF));
    private static readonly System.Collections.Generic.Dictionary<string, Brush> LicBrushCache = new();

    public FullRelativeRowViewModel(RelativeRow row, bool showClassPositionAsAbsolute)
    {
        // For the secondary (class-scoped) instance, PositionOffset is an absolute class position
        // (never the player's own row, since the player doesn't race in that class) -- only the
        // primary, player-relative instance can ever have a real "this is me" row.
        IsPlayerRow = row.IsPlayer;
        PositionText = row.PositionOffset.ToString(CultureInfo.InvariantCulture);
        // The pilot column is strictly the driver identity.  Some AI sessions publish a car model
        // in AbbrevName; mixing the manufacturer into this same string made that defect look like
        // the UI was intentionally showing cars instead of drivers.
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
        GapText = row.GapSeconds is double gap ? $"{(gap >= 0 ? "+" : "")}{gap.ToString("0.0", CultureInfo.InvariantCulture)}" : "--";

        // Position by class -- "igual funciona no Kapps hoje" (14/09/2026). See StandingsRowViewModel's
        // own comment: CarClassPosition/CarClassColor are both real DriverModel fields.
        HasClassBadge = !string.IsNullOrEmpty(row.ClassShortName);
        ClassBadgeText = HasClassBadge ? $"P{row.ClassPosition} {row.ClassShortName}" : "";
        ClassAccentBrush = row.ClassColorHex is not null
            ? ParseLicColor(row.ClassColorHex)
            : (Brush)System.Windows.Application.Current.Resources["F1AccentBrush"];

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

        HasP2P = row.P2PActive.HasValue;
        P2PStateText = row.P2PActive == true ? "ϟ ATIVO" : row.P2PInCooldown ? "◷ RECARGA" : HasP2P ? "✓ PRONTO" : "";
        P2PDetailText = row.P2PSecondsRemaining is double seconds && (row.P2PActive == true || row.P2PInCooldown)
            ? (row.P2PInCooldown ? "LIBERA ~" : "~") + seconds.ToString("0", CultureInfo.InvariantCulture) + " s"
            : HasP2P ? "DISPONÍVEL" : "";
        if (HasP2P && row.P2PActive == false && !row.P2PInCooldown) P2PBrush = P2PActiveBrush;

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

internal static class FlagStyle
{
    private static readonly Brush BrazilGreen = Frozen("#FF168A45");
    private static readonly Brush JapanWhite = Frozen("#FFF5F5F5");
    private static readonly Brush UsaRed = Frozen("#FFB22234");
    private static readonly Brush Generic = Frozen("#FF435363");
    private static readonly Brush Yellow = Frozen("#FFFFD447");
    private static readonly Brush Red = Frozen("#FFCF233A");
    private static readonly Brush White = Frozen("#FFFFFFFF");
    private static readonly Brush Dark = Frozen("#FF102030");
    public static (Brush Background, Brush Symbol, string Mark) For(string emoji) => emoji switch
    {
        "🇧🇷" => (BrazilGreen, Yellow, "◆"),
        "🇯🇵" => (JapanWhite, Red, "●"),
        "🇺🇸" => (UsaRed, White, "★"),
        _ => (Generic, White, "•")
    };
    public static string ImageFor(string emoji) => emoji switch
    {
        "🇧🇷" => "pack://application:,,,/IracingLiveCoach.App;component/Assets/Flags/br.png",
        "🇯🇵" => "pack://application:,,,/IracingLiveCoach.App;component/Assets/Flags/jp.png",
        "🇺🇸" => "pack://application:,,,/IracingLiveCoach.App;component/Assets/Flags/us.png",
        _ => ""
    };
    private static Brush Frozen(string value)
    {
        var brush = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(value)!);
        brush.Freeze();
        return brush;
    }
}

public class RelativeWidgetViewModel : INotifyPropertyChanged
{
    private string _brakeBiasText = "--";
    private string _trackRubberText = "--";
    private string _trackTempText = "--";
    private string _classSessionText = "--";
    private string _rubberBlocksText = "□□□□□";

    public string BrakeBiasText { get => _brakeBiasText; private set => Set(ref _brakeBiasText, value); }
    public string TrackRubberText { get => _trackRubberText; private set => Set(ref _trackRubberText, value); }
    public string TrackTempText { get => _trackTempText; private set => Set(ref _trackTempText, value); }

    // "queria que tivesse a possibilidade de dimensionar as colunas de forma personalizável, como
    // uma tabela do Excel" (14/09/2026) -- see StandingsWidgetViewModel's own copy of this comment.
    private System.Windows.GridLength _posColumnWidth = new(38);
    private System.Windows.GridLength _licColumnWidth = new(64);
    private System.Windows.GridLength _iRatingColumnWidth = new(60);
    private System.Windows.GridLength _deltaColumnWidth = new(72);
    private System.Windows.GridLength _otColumnWidth = new(106);
    private bool _hasOvertake;
    public bool HasOvertake { get => _hasOvertake; private set => Set(ref _hasOvertake, value); }
    public System.Windows.GridLength PosColumnWidth { get => _posColumnWidth; set => Set(ref _posColumnWidth, value); }
    public System.Windows.GridLength LicColumnWidth { get => _licColumnWidth; set => Set(ref _licColumnWidth, value); }
    public System.Windows.GridLength IRatingColumnWidth { get => _iRatingColumnWidth; set => Set(ref _iRatingColumnWidth, value); }
    public System.Windows.GridLength DeltaColumnWidth { get => _deltaColumnWidth; set => Set(ref _deltaColumnWidth, value); }
    public System.Windows.GridLength OtColumnWidth { get => _otColumnWidth; set => Set(ref _otColumnWidth, value); }
    public string ClassSessionText { get => _classSessionText; private set => Set(ref _classSessionText, value); }
    public string RubberBlocksText { get => _rubberBlocksText; private set => Set(ref _rubberBlocksText, value); }

    public ObservableCollection<FullRelativeRowViewModel> Rows { get; } = new();

    // Class/session text is the only piece of SessionStatus this panel's header shows -- lap/flag
    // belong to Standings, and this widget's own title (set at construction) already names the class.
    public void ApplySessionStatus(SessionStatus status) => ClassSessionText = $"{status.CarClassShortName}  •  AO REDOR DE VOCÊ";

    // showClassPositionAsAbsolute=true for the secondary (class-scoped) instance, whose
    // PositionOffset carries an absolute class position, not an offset from the player.
    public void SetRows(System.Collections.Generic.List<RelativeRow> rows, bool showClassPositionAsAbsolute = false)
    {
        HasOvertake = rows.Exists(row => row.P2PActive.HasValue);
        if (!HasOvertake) OtColumnWidth = new(0);
        else if (OtColumnWidth.Value == 0) OtColumnWidth = new(106);
        Rows.Clear();
        foreach (var row in rows) Rows.Add(new FullRelativeRowViewModel(row, showClassPositionAsAbsolute));
    }

    public void ApplyPlayerStatus(PlayerCarStatus status)
    {
        BrakeBiasText = status.BrakeBiasPct is double bias ? bias.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "--";
        TrackRubberText = status.TrackRubberState ?? "--";
        TrackTempText = status.TrackTempC is double temp ? temp.ToString("0", CultureInfo.InvariantCulture) + "°C" : "--";
        RubberBlocksText = RubberBlocks(status.TrackRubberState);
    }

    // SessionTrackRubberState is real (confirmed via reflection) but its own text values aren't
    // independently documented -- mapped defensively by keyword rather than exact match, so an
    // unrecognized value degrades to "no data" (empty blocks) instead of a wrong reading.
    private static string RubberBlocks(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return "□□□□□";
        var lowered = state.ToLowerInvariant();
        int filled =
            lowered.Contains("high") || lowered.Contains("heavy") || lowered.Contains("alto") ? 5 :
            lowered.Contains("moderate") || lowered.Contains("medium") || lowered.Contains("moder") ? 3 :
            lowered.Contains("light") || lowered.Contains("low") || lowered.Contains("leve") ? 1 :
            lowered.Contains("none") || lowered.Contains("green") ? 0 : 2;
        return new string('■', filled) + new string('□', 5 - filled);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
