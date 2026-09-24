namespace IracingLiveCoach.Core.Telemetry;

/// <summary>iRating display format (spec §12: "formato de número para iRating"). Full is the SDK
/// value grouped with a "." thousands separator ("4.390"); Thousands is a compact "4.4k" reading,
/// useful at small font scales; Plain is the same raw value with NO separator ("4390"), added later
/// (item 12) -- Full=0/Thousands=1 keep their existing numeric values so saved profiles never change
/// meaning.</summary>
public enum IRatingFormat { Full, Thousands, Plain }

/// <summary>Safety Rating display format (spec §12: "formato de número para... Safety Rating").
/// iRacing's own SDK reports SR as a combined "letter class + number" string (e.g. "A 4.12");
/// these variants split that apart for widgets/users who only want part of it.</summary>
public enum SafetyRatingFormat { LetterAndNumber, NumberOnly, LetterOnly }

/// <summary>Live, user-configurable number-format preferences, global across widgets (spec §12
/// groups this with the other cross-cutting formatting asks, not per-widget like column decimals).
/// Null means "use Default".</summary>
public sealed record NumberFormatConfig(IRatingFormat IRating, SafetyRatingFormat SafetyRating, NameDisplayFormat NameFormat = NameDisplayFormat.Full, bool ShowIRatingDelta = true)
{
    public static NumberFormatConfig Default { get; } = new(IRatingFormat.Full, SafetyRatingFormat.LetterAndNumber, NameDisplayFormat.Full);

    /// <summary>Formats a raw iRating value per this config.</summary>
    public string FormatIRating(int iRating) => IRating switch
    {
        IRatingFormat.Thousands => (iRating / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "k",
        IRatingFormat.Plain => iRating.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => GroupThousands(iRating),
    };

    /// <summary>Thousands separated by '.' as in the mockups ("4.390"): overlay TEXT is US English
    /// but numbers follow the user's pt-BR style. Fixed, not culture-dependent, so it looks the same
    /// on every machine.</summary>
    public static string GroupThousands(int value) =>
        value.ToString("N0", new System.Globalization.NumberFormatInfo { NumberGroupSeparator = ".", NumberGroupSizes = [3] });

    /// <summary>Reformats an SDK-provided license string ("A 4.12", "R 1.50", or a lone "Pro" with
    /// no numeric part) per this config. Splits on the first space rather than parsing raw SDK
    /// numeric fields -- the SDK's own string is already exactly "letter-or-class-name" + "number",
    /// so re-deriving that split from LicLevel/LicSubLevel would just duplicate logic the SDK already
    /// did, with more room to get the class-boundary mapping wrong.</summary>
    public string FormatLicense(string sdkLicString)
    {
        if (string.IsNullOrWhiteSpace(sdkLicString)) return sdkLicString;
        int spaceIdx = sdkLicString.IndexOf(' ');
        string letter = spaceIdx < 0 ? sdkLicString : sdkLicString[..spaceIdx];
        string number = spaceIdx < 0 ? "" : sdkLicString[(spaceIdx + 1)..];
        return SafetyRating switch
        {
            SafetyRatingFormat.NumberOnly => number.Length > 0 ? number : sdkLicString,
            SafetyRatingFormat.LetterOnly => letter,
            _ => sdkLicString,
        };
    }
}
