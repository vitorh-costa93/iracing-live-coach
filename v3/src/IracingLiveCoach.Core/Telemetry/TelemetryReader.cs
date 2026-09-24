using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using IRSDKSharper;
using IracingLiveCoach.Core;

namespace IracingLiveCoach.Core.Telemetry;

/// <summary>One nearby car's push-to-pass state, relative to the player's own running position.
/// PositionOffset=1 is the car directly behind, 2 the next one back, etc. -- never the player's own
/// car (that's always excluded). P2PActive is null (not false) when CarIdxP2P_Status simply isn't
/// published for this session (a class without push-to-pass, e.g. GT3) -- see RelativeUpdated's own
/// doc comment for why the whole event just doesn't fire in that case instead of firing with nulls.</summary>
public record RelativeCarStatus(int PositionOffset, bool P2PActive);

/// <summary>One row of the full running-order relative widget (Task 5) -- unlike
/// RelativeCarStatus above (P2P-strip only, behind-only), this carries driver code/gap/tire/P2P
/// together and can be ahead (negative PositionOffset) or behind (positive).
/// P2PUsesRemaining/P2PSecondsRemaining/P2PInCooldown are only meaningful when P2PActive
/// is non-null (the session publishes P2P at all). P2PSecondsRemaining/P2PInCooldown are computed
/// from the SF23's OWN PUBLICLY DOCUMENTED Overtake System rules (20s active window, 100s
/// cooldown -- see UpdateFullRelative's own doc comment for the source and the disclosed
/// car-specific caveat) combined with the REAL live CarIdxP2P_Status transition, giving an actual
/// countdown rather than a vague elapsed-time approximation -- corrected 14/09/2026 after the
/// driver confirmed this is a real feature they use today.</summary>
public record RelativeRow(int PositionOffset, string DriverCode, double? GapSeconds, int? TireCompound, bool? P2PActive, int? P2PUsesRemaining, double? P2PSecondsRemaining, bool P2PInCooldown, string FlagEmoji, string LicString, string? LicColorHex, int IRating, int CarClassId, string ManufacturerBadge, bool IsPlayer = false, int ClassPosition = 0, string ClassShortName = "", string? ClassColorHex = null, string CarNumber = "", int OverallPosition = 0, int ClassRank = 0);

/// <summary>One row of the full classification/standings widget (Task 6).
/// GapToLeaderSeconds is real (CarIdxF2Time, "race time behind leader or fastest lap otherwise" --
/// confirmed via sajax.github.io/irsdkdocs), null for the leader (shown as "LEADER" by the view
/// model) and outside a race session. EstimatedDeltaIRating is NOT a real SDK field -- iRacing
/// exposes no such projection -- it is a rank-vs-iRating approximation using the SDK's own
/// published Strength-of-Field formula (see UpdateStandings), always shown with an asterisk
/// disclosure per the driver's own explicit choice (14/09/2026) to include an approximate value
/// rather than omit the column. LapDeltaVsPlayerSeconds is real (this driver's own CarIdxLastLapTime
/// minus the player's own LapLastLapTime), matching the driver's own reference mockup's footnote
/// ("Δ VOLTA = última volta do piloto - sua última volta").</summary>
public record StandingsRow(int Position, string DriverCode, int LapsCompleted, double? LastLapTime, int? TireCompound, bool IsPlayer, string FlagEmoji, string LicString, string? LicColorHex, int IRating, int CarClassId, string ManufacturerBadge, double? GapToLeaderSeconds, double? EstimatedDeltaIRating, double? LapDeltaVsPlayerSeconds, string ClassShortName, string? ClassColorHex, int ClassPosition, double? IntervalSeconds, bool? P2PActive, int? P2PUsesRemaining, double? P2PSecondsRemaining, bool P2PInCooldown, string PitStatus, string CarNumber = "", int ClassRank = 0,
    int? StartPosition = null, int? PositionChange = null, int? IntervalLaps = null, double? BestLapTime = null, bool InPitLane = false, bool TimedOrder = false);
// StartPosition/PositionChange: class grid slot and places gained (+) / lost (-) since the start -- race
// only, see StartingGrid. IntervalLaps: the car ahead in class is this many whole laps up the road
// (IntervalSeconds is then null) -- Kapps' "1L". BestLapTime: the car's best lap (practice/qualifying
// order). InPitLane: live CarIdxOnPitRoad (Kapps tags such a row "PIT"), independent of PitStatus,
// which only reports real stops during a green race (see PitStopTracker). TimedOrder: the rows are ordered by
// best lap (practice/qualifying, or a race's pre-green grid) -- intervals are best-lap differences and the lap
// column shows the best lap (see StandingsCellText).

/// <summary>One full-field-tick session summary for the Standings/Relative widgets' header block --
/// class/session/lap/flag are all real SDK fields; StrengthOfField uses iRacing's own published SoF
/// formula (BR1 = 1600/ln(2), SoF = BR1 * ln(N / Σ e^(-iRating_i / BR1))) over the current field's
/// real iRatings -- see iracing.com/strength-in-numbers for the source formula.</summary>
/// <summary>A class's lap counter for its Standings panel: the class leader's lap and Kapps' projected total
/// (null before the leader has race laps -- Kapps then shows just "Lap 1").</summary>
public readonly record struct ClassLapInfo(int Lap, double? Projected);

public record SessionStatus(string CarClassShortName, string SessionTypeText, int? CurrentLap, int? TotalLaps, string SessionFlagText, string SessionFlagColorHex, double? StrengthOfField, int DriverCount, string PlayerCarName = "", bool TotalLapsEstimated = false, double? TotalLapsProjected = null,
    IReadOnlyDictionary<int, ClassDriverCount>? ClassCounts = null, int PlayerClassId = -1, IReadOnlyDictionary<int, ClassLapInfo>? ClassLaps = null)
{
    /// <summary>Kapps' per-class count ("14", "2/14") for a class; the whole-field count only when the
    /// session's driver list was not available.</summary>
    public string DriverCountText(int classId) =>
        ClassCounts is not null && ClassCounts.TryGetValue(classId, out var c) ? c.Text : DriverCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>The player's own current car status for the Relative/Standings widgets' footer.
/// BrakeBiasPct/TrackRubberState are null if the current car/session doesn't publish that channel
/// (see UpdatePlayerCarStatus's own try/catch per field). BestLapTimeSeconds is the minimum
/// LapLastLapTime observed so far this session -- null until the player has completed one lap.
/// TrackTempC reuses the same real "TrackTemp" channel the Weather widget already reads.</summary>
public record PlayerCarStatus(double? BrakeBiasPct, string? TrackRubberState, double? BestLapTimeSeconds, double? LastLapTimeSeconds, double? TrackTempC, int? Incidents = null, int? IncidentLimit = null);

/// <summary>One tick's fuel state. AverageFuelPerLapLiters/LapsRemaining/TimeRemainingSeconds are
/// null until at least one full lap has completed since the app started watching (see UpdateFuel's
/// own doc comment) -- never show a number computed from zero samples.</summary>
public record FuelStatus(double FuelLevelLiters, double FuelUsePerHourLiters, double? AverageFuelPerLapLiters, double? LapsRemaining, double? TimeRemainingSeconds, double? RefuelToFullLiters = null, double? FuelNeededForFinishLiters = null, double? PlannedPitFuelLiters = null, double? FuelAfterPitLiters = null, double? FuelAtFinishLiters = null, double? LastLapFuelUsedLiters = null, bool LastLapAffectedByPit = false, double? MaxFuelPerLapLiters = null, double? AverageLapTimeSeconds = null, double? RaceLapsRemaining = null, int? RaceTotalLaps = null, bool RaceLapsEstimated = false, bool AverageFromHistory = false, double? PlayerLapDistPct = null);

/// <summary>One car's current position around the lap (0.0 at start/finish, approaching 1.0 as it
/// completes the lap) -- feeds the Weather widget's linear "track usage" bar. Deliberately NOT a
/// real track shape (see the spec's own Phase 2 section for why).</summary>
public record TrackPositionDot(string DriverCode, double LapDistPct, bool IsPlayer);

/// <summary>One (throttled, ~10Hz) weather/track-usage snapshot.</summary>
public record WeatherStatus(double AirTempC, double TrackTempC, double PrecipitationPct, int TrackWetness, bool WeatherDeclaredWet, string? TrackRubberState, List<TrackPositionDot> CarPositions, double? WindSpeedMs = null, double? WindDirectionDeg = null, double? RelativeHumidityPct = null);

/// <summary>One far-field car's signed distance from the player along the lap (negative = behind,
/// positive = ahead), converted from CarIdxLapDistPct using the track's own length. Deliberately
/// carries NO lateral position -- the SDK does not expose one for cars outside the immediate
/// blind-spot window (see CarLeftRight's own doc comment on RadarStatus).</summary>
public record RadarBlip(double DistanceMeters, string DriverCode, RadarSide Side = RadarSide.Center);

/// <summary>One (throttled, ~10Hz) radar snapshot. BlindSpotLeft/Right come from CarLeftRight, a
/// coarse, NON-per-car signal -- true means "something is right next to you on that side", not
/// "car X is on that side". Blips are the separate far-field distance list (see RadarBlip); the
/// two are rendered differently on purpose (see this plan's own Global Constraints) so a driver
/// never mistakes a far blip's centered position for "directly in my lane".
/// LeftCarOffsetMeters/RightCarOffsetMeters: signed longitudinal offset (+ = ahead) of the nearest car on
/// that side while CarLeftRight reports it -- see RadarSideOffsets; null = draw the full-length bar.</summary>
public record RadarStatus(bool BlindSpotLeft, bool BlindSpotRight, List<RadarBlip> Blips, bool HasTrackLength, double? LeftCarOffsetMeters = null, double? RightCarOffsetMeters = null);

/// <summary>Race-start clutch/throttle bars -- confirmed real via Clutch/Throttle/Speed telemetry.
/// ShouldShow is true only while the current session is a Race AND the car is essentially
/// stationary (Speed below a small threshold) -- the same "car is staying still" trigger Kapps'
/// own Race Start Helper widget uses per the driver's own description (14/09/2026, "uso bastante
/// ele pra largar no SF23" -- SF23 uses a clutch-based standing start, the exact case this helps
/// with). No extra latch/state beyond the live Speed check -- if the driver stops again later in
/// the race (a spin, a full-course caution), the bars simply reappear, matching the plain
/// "when car is staying still" behavior Kapps itself describes.</summary>
public record RaceStartStatus(double ClutchPct, double ThrottlePct, bool ShouldShow, double RpmValue = 0);

/// <summary>Wraps IRSDKSharper's IRacingSdk, translating its raw telemetry variables into this
/// app's own TelemetrySample shape and forwarding each tick to a LiveCoachEngine. IRSDKSharper's
/// own LapDistPct is 0-1; the baseline endpoint's corner boundaries (and this app's
/// TelemetrySample.LapDistPct) are 0-100, so this is where that *100 conversion happens -- the
/// ONLY place in this app that needs to know about that unit mismatch.
///
/// Car and track are never picked by the user -- per the design spec, they're auto-detected once
/// the SDK reports a live session (SessionDetected fires once, from IRSDKSharper's own OnSessionInfo
/// event, which updates far less often than telemetry). Until that fires, no LiveCoachEngine is
/// attached and telemetry ticks are simply ignored -- there is nothing to compare against yet.</summary>
public class TelemetryReader : IDisposable
{
    // 13/09/2026: "não tenho a possibilidade de ver se o carro de trás está usando o botão de
    // ultrapassagem" -- how many cars behind to report. 3 covers "who might attack me soon", not
    // just the immediate follower, without turning into a full running order.
    private const int RelativeCarsBehind = 3;
    private const int RelativeCarsMax = RelativeRules.Max;

    // UpdateInterval=1 makes IRSDKSharper fire OnTelemetryData on every sim tick (60Hz for most
    // cars) instead of silently skipping frames -- the app's own tick counters below are what
    // decide how often each widget actually reacts, not this.
    private readonly IRacingSdk _sdk = new() { UpdateInterval = 1 };
    private LiveCoachEngine? _engine;
    private bool _sessionDetected;
    private bool _isRaceSession;
    private int _playerCarIdx = -1;
    private Dictionary<int, string> _driverCodesByCarIdx = new();

    // Fuel: per-lap measurement lives in the pure, unit-tested FuelTracker (clean green-flag laps
    // only); history per car+track seeds the average the way Kapps shows one from lap 1.
    private FuelTracker _fuelTracker = new();
    private static readonly Lazy<FuelHistoryStore> FuelHistory = new(() => new FuelHistoryStore());
    private int _carId;
    private int _trackId;
    private double? _estimatedLapTime;
    private RaceLapEstimate? _raceEstimate;

    // Push-to-pass: iRacing publishes CarIdxP2P_* in every session (also for classes without it,
    // as zeros), so the session only "has" P2P once a car actually shows a bank or an activation.
    private DateTime _lastP2PEvidenceUtc = DateTime.MinValue;
    private readonly P2PCooldownTracker _p2pCooldown = new();
    private int? _incidentLimit;
    private int _leaderLap;
    private bool SessionHasP2P => (DateTime.UtcNow - _lastP2PEvidenceUtc).TotalSeconds < 30;

    // 13/09/2026: WeatherUpdated is throttled to ~10Hz (every 6th telemetry tick, 60Hz/6=10) --
    // see this plan's own Global Constraints for why a full-MaxNumCars scan doesn't need 60Hz here.
    private const int WeatherTickInterval = 6;
    private int _weatherTickCounter;

    // 13/09/2026: set once per session (MainWindow calls this right after a baseline is fetched,
    // reusing BaselineSync's own already-fetched TrackLengthMeters rather than re-parsing
    // WeekendInfo.TrackLength here) -- null until then, so UpdateRadar's meter conversion simply
    // skips far-field blips (not fabricate a wrong distance) until a real length is known.
    private double? _trackLengthMeters;

    // CarLeftRight is the SDK's purpose-built side-by-side signal.  Sample it on every telemetry
    // frame so the side warning reacts at the simulator's 60 Hz cadence.  The more expensive
    // all-car distance scan is still deferred until that signal says a car is actually alongside.
    private const int RadarTickInterval = 1;
    private int _radarTickCounter;
    private const double RadarMaxRangeMeters = 30.0;

    // 16/09/2026: previously 10 Hz / 2 Hz -- with the WPF side now updating rows in place instead
    // of rebuilding the whole collection every tick (see MainWindow's ApplyRows), that render cost
    // dropped enough to read proximity/full-field data far closer to the sim's own 60 Hz, which is
    // what "instant" overtake reporting on a chaotic opening lap actually requires. The
    // driving-coach engine below remains on every SDK tick regardless.
    private const int ProximityTickInterval = 1;
    // Standings and Relative must derive from the same live position snapshot.  Keeping a slower
    // full-field cadence made Standings appear to freeze until a timing line even while Relative
    // moved.  UI coalescing keeps only the newest snapshot for each composed frame.
    private const int FullFieldTickInterval = 1;
    private int _proximityTickCounter;
    private int _fullFieldTickCounter;

    // 16/09/2026 correction: the previous SF23-specific 20s-active/100s-cooldown model was WRONG
    // -- the driver confirmed CarIdxP2P_Count IS the real remaining-seconds bank the SF23 Overtake
    // System exposes directly ("começa com 200s e vai diminuindo conforme usa"), not a discrete
    // activation counter needing a fabricated countdown. There is no separate cooldown field, so
    // that invented phase-timer machinery (UpdateP2PPhase and its two dictionaries) was removed
    // rather than guessed at again -- CarIdxP2P_Status/CarIdxP2P_Count are read and passed through
    // as-is everywhere P2P is reported.

    // CarIdxOnPitRoad is published per car.  The SDK does not expose a formatted pit timer, so
    // retain the real transition locally and present its elapsed duration.  Once a car exits, the
    // last completed pit (lap + duration) remains available as useful race context.
    private readonly PitStopTracker _pitStops = new();

    // Per-session state. A session = telemetry (SessionUniqueID, SessionNum); practice -> qualifying ->
    // race of one event are different sessions of one connection, so everything tied to "this session"
    // is reset here, not only on disconnect.
    private SessionKey? _sessionKey;
    private SessionKind? _sessionKind;
    /// <summary>Track rubber, captured on the first read of each session and then fixed (driver's own
    /// rule, not Kapps'). Static: every widget's reader shows the same captured value.</summary>
    private static readonly SessionLatch<string> RubberLatch = new();
    /// <summary>CarIdx -> 1-based overall grid slot of the current race (see StartingGrid).</summary>
    private Dictionary<int, int> _grid = new();
    private string _gridSource = "";
    /// <summary>CarIdx -> qualifying best lap of the grid source (Kapps shows these before the green).</summary>
    private Dictionary<int, double> _gridBestLap = new();
    /// <summary>Live SessionState, refreshed every tick by CheckSessionChange.</summary>
    private int _sessionState;
    private readonly Dictionary<int, int> _preGreenGrid = new();
    /// <summary>Class id -> CarClassEstLapTime, the reference lap CarIdxEstTime is measured on.</summary>
    private Dictionary<int, double> _classLapTimeById = new();
    // CarIdxP2P_Count is only meaningful for an OTS car.  Some non-OTS entries expose an
    // uninitialised integer instead of the SDK's usual Int32.MaxValue sentinel, so retain the
    // last valid bank per car and mark a short recharge window only when the real bank rises.
    // 16/09/2026: the driver confirmed the real bank is 200 s (matching their own car's Int32
    // reading, a clean 200 -> 196 -> 193 -> ... countdown). Opponents decode through GetFloat as
    // 0..20 with the SAME real decay rate per second (19.985 -> 19.683 -> 19.371 -> 19.07 across
    // ~9s is ~0.1/s in the raw float, i.e. ~1/s once scaled by 10 -- identical to the player's own
    // ~1/s Int32 decay). The float is therefore the real value in TENS of seconds, not seconds --
    // RawP2PMaxSeconds is its own ceiling (the sanity check happens before scaling); P2PMaxSeconds
    // is the real, final 0..200 scale shared by both the player's Int32 path and the opponents'
    // scaled-float path.
    private const float RawP2PMaxSeconds = 20f;
    public const int P2PMaxSeconds = 200;
    // 16/09/2026 correction: this was previously "fixed" by multiplying CarIdx by 4 under the
    // theory that GetInt's index parameter is a byte offset. Verified against IRSDKSharper's own
    // source (IRacingSdkData.GetInt): the method already does `Offset + datum.Offset + index * 4`
    // internally, so `index` IS the element index -- passing CarIdx directly was always correct.
    // Multiplying by 4 again made every car past CarIdx 15 read 16 bytes per slot instead of 4,
    // walking past the real 64-int array into unrelated telemetry memory -- reintroducing the
    // exact garbage-number bug it was meant to fix, just for a different, wider set of cars.

    private double? _bestLapTimeSeconds;

    // 14/09/2026: "deixar oculto até eu ir pra pista" -- PlayerTrackSurface (confirmed real via
    // reflection against the actual IRSDKSharper.dll this app uses: IRacingSdkEnum.TrkLoc, with
    // OnTrack=3) drives whether the widget suite should be visible at all. Starts false (hidden)
    // so a driver who just launched the app, or who's sitting in a menu/the pits, doesn't get a
    // screen full of overlays before they've actually gone out -- matching the reference behavior
    // the driver described in Kapps. Only fires OnTrackStateChanged when the bool actually flips,
    // not every tick, so MainWindow isn't re-applying visibility to nine windows 60 times a second.
    private bool _isOnTrack;

    // Populated once alongside SessionDetected/_playerCarIdx -- driver identities don't change
    // mid-session, so this is read once from OnSessionInfo, not re-parsed every telemetry tick.
    private static Dictionary<int, string> BuildDriverCodes(IRacingSdkSessionInfo? sessionInfo)
    {
        var map = new Dictionary<int, string>();
        foreach (var driver in sessionInfo?.DriverInfo?.Drivers ?? new List<IRacingSdkSessionInfo.DriverInfoModel.DriverModel>())
        {
            // AbbrevName is not stable across all iRacing session types: some AI/session data
            // fills it with the car model ("08 - ACURA") rather than a person.  UserName is the
            // driver identity.  Format it as the broadcast convention "V. COSTA".
            var code = FormatFullDriverName(driver.UserName, driver.CarNumber);
            map[driver.CarIdx] = code;
        }
        return map;
    }

    private static string FormatDriverName(string? userName, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(userName)) return fallback ?? "?";
        var parts = userName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1) return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(parts[0].ToLowerInvariant());
        return $"{char.ToUpperInvariant(parts[0][0])}. {CultureInfo.InvariantCulture.TextInfo.ToTitleCase(parts[^1].ToLowerInvariant())}";
    }

    private static string FormatFullDriverName(string? userName, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(userName)) return fallback ?? "?";
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(userName.Trim().ToLowerInvariant());
    }

    /// <summary>The pace/safety car is a real entry in the session's driver list (own "class", #0)
    /// but is not a competitor: it must never appear as a row or a class panel in Standings/Relative.</summary>
    private bool IsPaceCar(int carIdx)
    {
        try
        {
            var driver = _sdk.Data.SessionInfo?.DriverInfo?.Drivers?.FirstOrDefault(d => d.CarIdx == carIdx);
            return driver is not null && driver.CarIsPaceCar != 0;
        }
        catch { return false; }
    }

    private (string FlagEmoji, string LicString, string? LicColorHex, int IRating, int CarClassId, string ManufacturerBadge, string ClassShortName, string? ClassColorHex, string CarNumber, int ClassRank) GetIdentity(int carIdx)
    {
        var sessionInfo = _sdk.Data.SessionInfo;
        var driver = sessionInfo?.DriverInfo?.Drivers?.FirstOrDefault(d => d.CarIdx == carIdx);
        var flagEmoji = CountryFlags.ToEmoji(driver?.FlairName);
        var licString = driver?.LicString ?? "--";
        var licColorHex = driver?.LicColor; // "String" per the real IRSDKSharper 1.3.0 type -- parsed defensively by the view model, not here.
        var iRating = driver?.IRating ?? 0;
        var carClassId = driver?.CarClassID ?? 0;
        var manufacturerBadge = ExtractManufacturer(driver?.CarScreenName);
        // CarClassShortName/CarClassColor are both real DriverModel fields (confirmed via reflection
        // on the actual IRSDKSharper 1.3.0 type) -- CarClassColor is iRacing's OWN per-class color
        // assignment (the same one the sim itself uses to color-code classes), so a multiclass field
        // gets a real, session-consistent class indicator rather than an invented palette.
        var classShortName = driver?.CarClassShortName ?? "";
        var classColorHex = driver?.CarClassColor;
        return (flagEmoji, licString, licColorHex, iRating, carClassId, manufacturerBadge, classShortName, classColorHex, driver?.CarNumber ?? "",
            _classRankById.TryGetValue(carClassId, out var classRank) ? classRank : 0);
    }

    // 14/09/2026: "team logo" -- iRacing has no real "team" concept for pickup/public racing and
    // no logo asset for anything via the SDK, but CarScreenName (confirmed real, e.g. "Ferrari 296
    // GT3 EVO") DOES let us derive the car's own manufacturer, which is what every competing
    // overlay's "team badge" actually shows in practice. iRacing's own car names consistently
    // follow a "<Manufacturer> <Model>" convention, so the first whitespace-delimited token is the
    // manufacturer in the overwhelming majority of cases -- a plain text badge (e.g. "FERRARI"),
    // not an image logo (bundling real trademarked logo graphics is a materially larger, separate
    // effort with its own legal/asset-sourcing considerations -- see this plan's own spec section).
    // 16/09/2026: the first-token heuristic misses cars whose CarScreenName carries the
    // manufacturer as a suffix instead of a prefix -- the SF23 is named e.g. "Super Formula SF23 -
    // Honda"/"... - Toyota" (the engine supplier), so "SUPER" was being extracted instead of the
    // real badge. Scanning the whole name for a known manufacturer first (falling back to the
    // first-token guess only when none matches) is what makes SF23's Honda/Toyota badge resolve.
    private static readonly string[] KnownManufacturers =
    {
        "ASTON MARTIN", "MERCEDES", "MCLAREN", "LAMBORGHINI", "CHEVROLET", "CORVETTE", "CADILLAC",
        "PORSCHE", "FERRARI", "FORD", "BMW", "AUDI", "ACURA", "DALLARA", "HONDA", "TOYOTA"
    };

    private static string ExtractManufacturer(string? carScreenName)
    {
        if (string.IsNullOrWhiteSpace(carScreenName)) return "";
        var upper = carScreenName.ToUpperInvariant();
        foreach (var known in KnownManufacturers)
            if (upper.Contains(known)) return known;
        return carScreenName.Split(' ', 2)[0].ToUpperInvariant();
    }

    /// <summary>Fires once per app run, the first time the SDK reports a session with both a
    /// track and the local driver's own car resolved. (carId, trackId) match iRacing's own
    /// catalog ids, the same ones Garage61 sync already uses for the `cars`/`tracks` tables.</summary>
    public event Action<int, int>? SessionDetected;

    /// <summary>Fires every telemetry tick once the player's own car index and running position are
    /// known, with one entry per car behind (closest first), ordered same as iRacing's own Relative
    /// box reads top-to-bottom. Simply doesn't fire at all (rather than firing with every P2PActive
    /// null) when CarIdxP2P_Status isn't published for this session -- a class without push-to-pass
    /// (GT3, etc.) -- so the strip can just show "sem push-to-pass" once and stop updating, instead
    /// of flickering a meaningless "sem dado" every tick.</summary>
    public event Action<List<RelativeCarStatus>>? RelativeUpdated;

    /// <summary>Fires every telemetry tick once the player's own position is known, with one row
    /// per nearby car (3 ahead, 3 behind, same window as the existing P2P-only RelativeUpdated)
    /// but carrying driver code/gap/tire/P2P together -- feeds the new full RelativeWidget
    /// (Task 5), distinct from the existing narrow P2P strip which keeps consuming
    /// RelativeUpdated unchanged.</summary>
    public event Action<List<RelativeRow>>? FullRelativeUpdated;

    /// <summary>Fires every telemetry tick once the session is detected, with one row per
    /// currently-classified car (CarIdxPosition > 0), ordered by position.</summary>
    public event Action<List<StandingsRow>>? StandingsUpdated;

    /// <summary>Fires alongside StandingsUpdated (same full-field tick budget) with the header
    /// summary shared by the Standings and Relative panels -- see SessionStatus's own doc comment.</summary>
    public event Action<SessionStatus>? SessionStatusUpdated;

    /// <summary>Fires every telemetry tick once the session is detected, with the player's own
    /// current fuel state and a rolling-average-based remaining-laps/time estimate.</summary>
    public event Action<FuelStatus>? FuelUpdated;

    /// <summary>Fires roughly every 10th of a second (throttled -- see WeatherTickInterval) once
    /// the session is detected, with the current weather/track-wetness readout and every car's
    /// current lap position.</summary>
    public event Action<WeatherStatus>? WeatherUpdated;

    /// <summary>Fires roughly every 10th of a second (throttled, same reasoning as
    /// WeatherUpdated) once the session is detected, with the current blind-spot state and every
    /// nearby car's far-field distance. See RadarStatus's own doc comment for why these two halves
    /// are kept visually distinct.</summary>
    public event Action<RadarStatus>? RadarUpdated;

    /// <summary>Fires every telemetry tick once the session is detected, with the player's own
    /// current brake bias / track rubber state / best lap for the widgets' own footer row.</summary>
    public event Action<PlayerCarStatus>? PlayerCarStatusUpdated;

    /// <summary>Mirrors FullRelativeUpdated's own shape and cadence, but scoped to the first car
    /// class found in the session that differs from the player's own (CarIdxClass), ranked by
    /// CarIdxClassPosition rather than overall CarIdxPosition -- feeds a second, independently
    /// positioned Relative widget instance for multiclass sessions. Simply never fires (not fires
    /// with an empty list) when the session is single-class -- same "don't flicker a meaningless
    /// empty state" posture RelativeUpdated already has for non-P2P sessions.</summary>
    public event Action<List<RelativeRow>>? SecondaryRelativeUpdated;

    /// <summary>Fires only when the player's own on-track state actually changes (not every tick),
    /// once the session is detected -- true while the player has assumed the car: on track,
    /// stopped on the grid, in the pit stall or off-track. It becomes false only at NotInWorld,
    /// i.e. after leaving the car/session. Drives the locked-overlay visibility.</summary>
    public event Action<bool>? OnTrackStateChanged;

    /// <summary>Fires every telemetry tick once the session is detected, with the player's own
    /// clutch/throttle pedal position and whether the Race Start Helper should currently be shown
    /// (Race session + car essentially stationary). See RaceStartStatus's own doc comment.</summary>
    public event Action<RaceStartStatus>? RaceStartUpdated;

    /// <summary>Fires once when the player takes the chequered flag in a Race session, with their
    /// final overall/class positions (see <see cref="RaceFinishDetector"/>).</summary>
    public event Action<RaceFinish>? PlayerFinishedRace;

    private readonly RaceFinishDetector _finishDetector = new();

    /// <summary>Called every telemetry tick: feeds the finish detector and, once it reports, reads
    /// the player's settled positions straight from the SDK.</summary>
    private void PollRaceFinish()
    {
        try
        {
            bool chequered = (_sdk.Data.GetInt("SessionFlags") & FlagCheckered) != 0;
            int laps = _sdk.Data.GetInt("CarIdxLapCompleted", _playerCarIdx);
            if (!_finishDetector.Update(_isRaceSession, chequered, laps, DateTime.UtcNow)) return;
            int overall = _sdk.Data.GetInt("CarIdxPosition", _playerCarIdx);
            int inClass = _sdk.Data.GetInt("CarIdxClassPosition", _playerCarIdx);
            PlayerFinishedRace?.Invoke(new RaceFinish(overall, inClass));
        }
        catch { /* channel momentarily unavailable: try again next tick */ }
    }

    public TelemetryReader()
    {
        _sdk.OnSessionInfo += OnSessionInfo;
        _sdk.OnTelemetryData += OnTelemetryData;
        // Telemetry stops immediately when the simulator closes, so frames alone cannot observe
        // that transition. IRSDKSharper exposes it explicitly through OnDisconnected.
        _sdk.OnDisconnected += OnDisconnected;
    }

    public void Start() => _sdk.Start();

    private long _lastTelemetryUtcTicks;
    /// <summary>True while the simulator is still supplying fresh shared-memory frames.  This is
    /// deliberately independent of OnDisconnected, which some shutdown paths fail to raise.</summary>
    public bool HasRecentTelemetry => DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastTelemetryUtcTicks) < TimeSpan.FromSeconds(2).Ticks;

    /// <summary>Read-only exposure of already-tracked session state, added for V3's <see cref="TelemetrySnapshot"/>
    /// (spec section 1's "documente capacidades reais por carro e sessão") -- pass-through only, no new logic.</summary>
    public bool IsRaceSession => _isRaceSession;

    /// <summary>Read-only exposure of the already-tracked player CarIdx, or -1 if not yet detected.</summary>
    public int PlayerCarIdx => _playerCarIdx;

    /// <summary>Builds a <see cref="TelemetrySnapshot"/> from the reader's current state -- a point-in-time
    /// capability/validity summary, not a replacement for the existing per-widget events above.</summary>
    public TelemetrySnapshot CaptureSnapshot() => new(
        DateTime.UtcNow,
        HasRecentTelemetry,
        _sessionDetected,
        IsRaceSession,
        PlayerCarIdx >= 0 ? PlayerCarIdx : null);

    private void OnDisconnected()
    {
        _isOnTrack = false;
        _playerCarIdx = -1;
        _sessionDetected = false;
        _isRaceSession = false;
        _finishDetector.Reset();
        _driverCodesByCarIdx.Clear();
        ResetPerSessionState();
        _sessionKey = null;
        RubberLatch.Reset();
        _lastP2PEvidenceUtc = DateTime.MinValue;
        _p2pCooldown.Reset();
        _lastP2PAnomalyLogByCarIdx.Clear();
        _raceEstimate = null;
        // Always notify: closing can happen between telemetry frames, while the last known
        // in-car state is still true. The V2 window then hides every locked widget immediately.
        OnTrackStateChanged?.Invoke(false);
    }

    /// <summary>Called once BaselineSync has resolved the detected car+track's history --
    /// telemetry ticks before this is called are simply ignored (OnTelemetryData no-ops on a
    /// null engine).</summary>
    public void AttachEngine(LiveCoachEngine engine) => _engine = engine;

    /// <summary>Called once by MainWindow after BaselineSync resolves the detected car+track's
    /// history (see OnSessionDetectedAsync) -- reuses that already-fetched track length instead of
    /// this class re-parsing WeekendInfo.TrackLength itself.</summary>
    public void SetTrackLength(double? meters) => _trackLengthMeters = meters;

    /// <summary>Class id -> speed rank (1 = fastest), rebuilt on every session-info update.</summary>
    private Dictionary<int, int> _classRankById = new();

    /// <summary>Lone qualifying: no other car is really around the player (SessionKinds.IsSolo).</summary>
    private bool _soloSession;

    /// <summary>Race lap times per car (ClassRaceProjection's last-5 average), reset per session.</summary>
    private readonly LapHistory _lapHistory = new();
    /// <summary>Class id -> (class leader's lap, Kapps' projected total) from the last Standings pass.</summary>
    private Dictionary<int, ClassLapInfo> _classLaps = new();

    private void RefreshClassRanks()
    {
        try
        {
            var drivers = _sdk.Data.SessionInfo?.DriverInfo?.Drivers;
            if (drivers is null) return;
            _classRankById = ClassRanks.Compute(drivers.Select(d => new ClassSpeedInfo(d.CarClassID, d.CarClassRelSpeed, d.CarClassEstLapTime)));
            _classLapTimeById = drivers.Where(d => d.CarClassEstLapTime > 1).GroupBy(d => d.CarClassID)
                .ToDictionary(g => g.Key, g => (double)g.First().CarClassEstLapTime);
        }
        catch { /* session info momentarily incomplete -- keep the previous ranks */ }
    }

    private void OnSessionInfo()
    {
        RefreshClassRanks();
        // SessionInfo arrives on session transitions as well as initial attachment.  Refresh the
        // cached race flag here, never in the 60-Hz pedal path.
        if (_sessionDetected)
        {
            RefreshRaceSessionFlag();
            return;
        }

        // Session info can be incomplete on early updates (e.g. before the driver has picked a
        // car) or the YAML parse can momentarily throw -- wait for a later, more complete update
        // rather than surfacing an error.
        try
        {
            var sessionInfo = _sdk.Data.SessionInfo;
            var trackId = sessionInfo?.WeekendInfo?.TrackID ?? 0;
            var driverCarIdx = sessionInfo?.DriverInfo?.DriverCarIdx ?? -1;
            var carId = sessionInfo?.DriverInfo?.Drivers?.FirstOrDefault(d => d.CarIdx == driverCarIdx)?.CarID ?? 0;
            if (trackId <= 0 || carId <= 0) return;

            _playerCarIdx = driverCarIdx;
            LogPlayerCarIdx(driverCarIdx);
            _driverCodesByCarIdx = BuildDriverCodes(sessionInfo);
            RefreshRaceSessionFlag();
            // V2 has no baseline-sync step (that's V1's corner-coaching flow, which never runs in
            // this build) to call SetTrackLength -- reading WeekendInfo.TrackLength directly here
            // means the radar's real distance blips work standalone, without that dependency.
            _trackLengthMeters ??= ParseTrackLengthMeters(sessionInfo?.WeekendInfo?.TrackLength);
            _carId = carId;
            _trackId = trackId;
            try
            {
                var estLap = sessionInfo?.DriverInfo?.DriverCarEstLapTime ?? 0;
                _estimatedLapTime = estLap > 1 ? estLap : null;
            }
            catch { _estimatedLapTime = null; }
            _fuelTracker = new FuelTracker();
            if (FuelHistory.Value.Get(carId, trackId) is { } history) _fuelTracker.Seed(history.FuelPerLap, history.LapTime);
            _sessionDetected = true;
            SessionDetected?.Invoke(carId, trackId);
        }
        catch
        {
            // Wait for the next OnSessionInfo update.
        }
    }

    // 16/09/2026: the driver's own CarIdxP2P_Count decoded cleanly as a genuine Int32 countdown
    // (200 -> 196 -> 193 -> ...) while every opponent decoded as a float -- but that was inferred
    // from CarIdx=0 without confirming CarIdx=0 IS the driver's own car this session. Logging the
    // real DriverCarIdx once per session removes that remaining guess before any "read the
    // player's index differently" rule gets built on it.
    private static void LogPlayerCarIdx(int carIdx)
    {
        try
        {
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iracing-live-coach", "p2p-trace.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.AppendAllText(path, $"{DateTime.UtcNow:O} SESSION DriverCarIdx={carIdx}\n");
        }
        catch { /* diagnostics must never break the real read path */ }
    }

    // WeekendInfo.TrackLength is a real field but formatted as free text (e.g. "3.06 km") rather
    // than a raw number -- parsed defensively since a future track/unit format could otherwise
    // silently throw and mask every other field this same try/catch protects.
    private static double? ParseTrackLengthMeters(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        var unit = trimmed.EndsWith("km", StringComparison.OrdinalIgnoreCase) ? 1000.0
            : trimmed.EndsWith("mi", StringComparison.OrdinalIgnoreCase) ? 1609.344
            : 1.0;
        var numberPart = trimmed.TrimEnd('k', 'm', 'i', 'K', 'M', 'I', ' ');
        return double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 ? value * unit : null;
    }

    private void OnTelemetryData()
    {
        Interlocked.Exchange(ref _lastTelemetryUtcTicks, DateTime.UtcNow.Ticks);
        // Relative/P2P doesn't depend on a baseline (there's no "history" for it), so it's read
        // regardless of whether a LiveCoachEngine has been attached yet -- only the corner-coaching
        // half below needs that.
        if (_playerCarIdx >= 0)
        {
            CheckSessionChange();
            UpdateOnTrackState();
            PollRaceFinish();
            // A full-field refresh coincides with the proximity refresh every third tick.  Keep
            // that immutable snapshot for the rest of the tick instead of scanning every CarIdx
            // a second time just to build Standings.
            LivePositions? positionsThisTick = null;

            _proximityTickCounter++;
            if (_proximityTickCounter >= ProximityTickInterval)
            {
                _proximityTickCounter = 0;
                var positions = ComputePositions();
                positionsThisTick = positions;
                UpdateRelative(positions);
                UpdateFullRelative(positions);
                UpdateSecondaryRelative(positions);
                UpdatePlayerCarStatus();
                UpdateRaceStart();
            }

            _fullFieldTickCounter++;
            if (_fullFieldTickCounter >= FullFieldTickInterval)
            {
                _fullFieldTickCounter = 0;
                UpdateStandings(positionsThisTick ?? ComputePositions());
                UpdateFuel();
            }

            _weatherTickCounter++;
            if (_weatherTickCounter >= WeatherTickInterval)
            {
                _weatherTickCounter = 0;
                UpdateWeather();
            }

            _radarTickCounter++;
            if (_radarTickCounter >= RadarTickInterval)
            {
                _radarTickCounter = 0;
                UpdateRadar();
            }
        }

        if (_engine is null) return; // no baseline attached yet -- nothing to compare corners against.

        // IRSDKSharper can throw when a requested channel is momentarily unavailable/unpublished
        // for the current car or session state; a live coaching overlay must never crash the whole
        // process over one bad telemetry tick, so a bad tick is silently skipped, not surfaced.
        try
        {
            var lapDistPct = _sdk.Data.GetFloat("LapDistPct") * 100.0;
            var brake = (double?)_sdk.Data.GetFloat("Brake");
            var throttle = (double?)_sdk.Data.GetFloat("Throttle");
            var steeringRad = (double?)_sdk.Data.GetFloat("SteeringWheelAngle");
            var rpm = (double?)_sdk.Data.GetFloat("RPM");
            var gear = (int?)_sdk.Data.GetInt("Gear");
            var speedMs = (double?)_sdk.Data.GetFloat("Speed");

            _engine.Update(new TelemetrySample(lapDistPct, brake, throttle, steeringRad, rpm, gear, speedMs));
        }
        catch
        {
            // Skip this tick.
        }
    }

    // 16/09/2026: root cause found from real session data logged by LogP2PAnomaly below. Every
    // opponent logged the EXACT SAME raw int, 1101004800, at every tick -- not noise (garbage from
    // an out-of-bounds/misaligned read varies per car and per tick), a fixed, well-formed value.
    // Its bits (0x41A00000) decode as the IEEE-754 float 20.0 -- and sajax's own irsdkdocs entry
    // for the per-player shortcut "P2P_Count" documents its type as float, not int (an array and
    // its single-value "shortcut" always share the same underlying type in this SDK). So
    // CarIdxP2P_Count is a float[] being misread through GetInt, which reinterprets the raw bytes
    // instead of converting -- reading it through GetFloat is what actually fixes this, not any
    // index/stride change (both the original code and the reverted "CarIdx * 4" attempt used
    // GetInt and were equally wrong for this reason).
    private static int? ReadP2PCount(float raw) => raw is >= 0 and <= RawP2PMaxSeconds ? (int)Math.Round(raw * 10) : null;

    // Kept for live evidence if a future value still falls outside the confirmed 0..200 s Super
    // Formula bank -- now logs the float itself, throttled per car to avoid flooding.
    private readonly Dictionary<int, DateTime> _lastP2PAnomalyLogByCarIdx = new();
    private void LogP2PAnomaly(int carIdx, float raw)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (_lastP2PAnomalyLogByCarIdx.TryGetValue(carIdx, out var last) && (now - last).TotalSeconds < 5) return;
            _lastP2PAnomalyLogByCarIdx[carIdx] = now;
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iracing-live-coach", "p2p-debug.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.AppendAllText(path, $"{now:O} CarIdx={carIdx} RawCarIdxP2P_Count(float)={raw} (outside 0..{P2PMaxSeconds})\n");
        }
        catch { /* diagnostics must never break the real read path */ }
    }

    // 16/09/2026: the driver correctly flagged that a CONSTANT 20.0 for every idle opponent doesn't
    // match "starts at 200s and drains" -- that constant is far more consistent with the SF23's own
    // publicly documented Overtake rule of a 20-SECOND ACTIVATION WINDOW per use, i.e. this field
    // may report the fixed per-activation duration for an idle car, not a personal remaining bank.
    // Logging every read (not just anomalies), throttled, to see whether/how it actually changes
    // when a car is active vs idle -- real behavior over time, not another single-snapshot guess.
    private readonly Dictionary<int, DateTime> _lastP2PTraceLogByCarIdx = new();
    private void LogP2PTrace(int carIdx, bool? active, float raw)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (_lastP2PTraceLogByCarIdx.TryGetValue(carIdx, out var last) && (now - last).TotalSeconds < 3) return;
            _lastP2PTraceLogByCarIdx[carIdx] = now;
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iracing-live-coach", "p2p-trace.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.AppendAllText(path, $"{now:O} CarIdx={carIdx} Status={active} RawCount(float)={raw}\n");
        }
        catch { /* diagnostics must never break the real read path */ }
    }

    // CarIdxP2P_Count is declared as an int, but iRacing fills the player's slot with the bank in
    // seconds (200 -> 0) and every other car's slot with a FLOAT in tens of seconds (20.0 -> 0.0),
    // verified live (SF23, Interlagos). Decode the raw 32 bits both ways and keep whichever is in
    // range. There is no "recharging" state in this system: the bank only goes down as it is used,
    // so a car is ACTIVE (status on), AVAILABLE (bank > 0) or EMPTY.
    private (bool? Active, int? Seconds, bool IsCharging) ReadP2P(int carIdx)
    {
        bool? active = null;
        int? seconds = null;
        try { active = _sdk.Data.GetBool("CarIdxP2P_Status", carIdx); }
        catch { /* not published for this car/session */ }
        try
        {
            int bits = _sdk.Data.GetInt("CarIdxP2P_Count", carIdx);
            if (bits is >= 0 and <= P2PMaxSeconds) seconds = bits;
            else
            {
                float asFloat = BitConverter.Int32BitsToSingle(bits);
                seconds = ReadP2PCount(asFloat);
                if (seconds is null) LogP2PAnomaly(carIdx, asFloat);
            }
        }
        catch { /* the count can be omitted independently of the status */ }

        var now = DateTime.UtcNow;
        if (active == true || seconds > 0) _lastP2PEvidenceUtc = now;
        if (!SessionHasP2P) return (null, null, false); // class without push-to-pass: no column at all
        bool lockedOut = _p2pCooldown.Update(carIdx, active, now);
        return (active, seconds, lockedOut);
    }

    private void RefreshRaceSessionFlag()
    {
        try
        {
            var sessionInfo = _sdk.Data.SessionInfo;
            var currentSessionNum = sessionInfo?.SessionInfo?.CurrentSessionNum ?? -1;
            var session = sessionInfo?.SessionInfo?.Sessions?.FirstOrDefault(s => s.SessionNum == currentSessionNum);
            _isRaceSession = string.Equals(session?.SessionType, "Race", StringComparison.OrdinalIgnoreCase);
            _sessionKind = SessionKinds.Classify(session?.SessionType);
            _soloSession = SessionKinds.IsSolo(session?.SessionType);
            RefreshStartingGrid(sessionInfo, session);
        }
        catch { _isRaceSession = false; }
    }

    /// <summary>Grid of the current race from SessionInfo (see StartingGrid for the source order). The
    /// pre-green CarIdxPosition sample is the last resort, used by UpdateStandings.</summary>
    private void RefreshStartingGrid(IRacingSdkSessionInfo? sessionInfo, IRacingSdkSessionInfo.SessionInfoModel.SessionModel? session)
    {
        if (!_isRaceSession) { _grid = new(); _gridSource = ""; _gridBestLap = new(); return; }
        try
        {
            var fromRace = session?.QualifyPositions;
            if (fromRace is { Count: > 0 })
            {
                _grid = StartingGrid.Normalize(fromRace.Select(q => (q.CarIdx, q.Position)));
                _gridBestLap = fromRace.Where(q => q.FastestTime > 0).GroupBy(q => q.CarIdx).ToDictionary(g => g.Key, g => (double)g.First().FastestTime);
                _gridSource = "race.QualifyPositions";
                return;
            }
            var fromQualify = sessionInfo?.QualifyResultsInfo?.Results;
            if (fromQualify is { Count: > 0 })
            {
                _grid = StartingGrid.Normalize(fromQualify.Select(q => (q.CarIdx, q.Position)));
                _gridBestLap = fromQualify.Where(q => q.FastestTime > 0).GroupBy(q => q.CarIdx).ToDictionary(g => g.Key, g => (double)g.First().FastestTime);
                _gridSource = "QualifyResultsInfo";
                return;
            }
        }
        catch { /* YAML momentarily incomplete -- keep what we had */ }
    }

    /// <summary>Detects a session change from the live SessionUniqueID/SessionNum channels (cheap ints,
    /// read every tick) and resets every per-session value.</summary>
    private void CheckSessionChange()
    {
        try
        {
            try { _sessionState = _sdk.Data.GetInt("SessionState"); } catch { }
            var key = new SessionKey(_sdk.Data.GetInt("SessionUniqueID"), _sdk.Data.GetInt("SessionNum"));
            if (_sessionKey == key) return;
            bool first = _sessionKey is null;
            _sessionKey = key;
            if (!first) ResetPerSessionState();
            RefreshRaceSessionFlag();
        }
        catch { /* channels momentarily unavailable */ }
    }

    private void ResetPerSessionState()
    {
        _pitStops.Reset();
        _preGreenGrid.Clear();
        _grid = new();
        _gridSource = "";
        _gridBestLap = new();
        _bestLapTimeSeconds = null;
        _finishDetector.Reset();
        _lapHistory.Clear();
        _classLaps = new();
    }

    private readonly record struct LivePositions(Dictionary<int, int> Overall, Dictionary<int, int> ByClass);

    // 16/09/2026: iRacing's own CarIdxPosition/CarIdxClassPosition are documented to lag behind
    // the real order on track, especially right after an overtake -- this is exactly why
    // Standings/Relative appeared to freeze mid-lap despite the SDK firing at 60Hz (confirmed via
    // the iRacing community's own investigation, e.g. niklam/iracedeck#233). Real-time overlays
    // instead derive position from the physical CarIdxLapCompleted+CarIdxLapDistPct -- more laps
    // completed ranks higher, ties broken by how far around the current lap. Computed once per
    // tick group and shared by every consumer below so they can never disagree with each other.
    /// <summary>Practice/qualifying/test: iRacing's best-lap classification (TimedSessionOrder). Race or
    /// unknown session: the live order on track (ComputeLivePositions).</summary>
    private bool IsTimedSession => _sessionKind is SessionKind.Practice or SessionKind.Qualify;

    /// <summary>A race before the green (get in car / warm-up / parade laps) with a known grid. Kapps then
    /// lists the GRID order with the qualifying laps (verified live, Watkins Glen 24/09/2026: order, best
    /// laps and intervals were the qualifying classification while the pace car led the field), and
    /// CarIdxPosition is 0 for everyone during the parade lap, so the live order would be meaningless.</summary>
    private bool IsPreGreenGrid => _isRaceSession && _sessionState is > 0 and < SessionStateRacingState && _grid.Count > 0;

    /// <summary>Race after the chequered flag: the official classification (see FinalResults), refreshed
    /// from SessionInfo every tick; empty when it is not published yet.</summary>
    private Dictionary<int, FinalResultEntry> _finalResults = new();

    private bool RefreshFinalResults()
    {
        _finalResults = new();
        if (!FinalResults.Applies(_isRaceSession, _sessionState)) return false;
        try
        {
            var info = _sdk.Data.SessionInfo?.SessionInfo;
            var num = info?.CurrentSessionNum ?? -1;
            var results = info?.Sessions?.FirstOrDefault(s => s.SessionNum == num)?.ResultsPositions;
            if (results is not { Count: > 0 }) return false;
            foreach (var r in results)
                _finalResults[r.CarIdx] = new FinalResultEntry(r.CarIdx, r.Position, r.ClassPosition, r.Time, r.LastTime, r.LapsComplete);
        }
        catch { _finalResults = new(); }
        return _finalResults.Count > 0;
    }

    private LivePositions ComputePositions()
    {
        if (RefreshFinalResults())
        {
            var (finalOverall, finalByClass) = FinalResults.Positions(_finalResults.Values);
            return new LivePositions(finalOverall, finalByClass);
        }
        bool preGreen = IsPreGreenGrid;
        if (!IsTimedSession && !preGreen) return ComputeLivePositions();
        var cars = new List<TimedCar>();
        for (var idx = 0; idx < IRacingSdkConst.MaxNumCars; idx++)
        {
            var position = preGreen ? (_grid.TryGetValue(idx, out var slot) ? slot : 0) : _sdk.Data.GetInt("CarIdxPosition", idx);
            var inWorld = _sdk.Data.GetFloat("CarIdxLapDistPct", idx) >= 0;
            if (position <= 0 && !inWorld) continue;
            if (IsPaceCar(idx)) continue;
            // Pre-green: class slots are ranked from the overall grid inside TimedSessionOrder (ClassPosition 0).
            cars.Add(new TimedCar(idx, _sdk.Data.GetInt("CarIdxClass", idx), position, preGreen ? 0 : _sdk.Data.GetInt("CarIdxClassPosition", idx), inWorld));
        }
        var (overall, byClass) = TimedSessionOrder.Compute(cars);
        return new LivePositions(overall, byClass);
    }

    private LivePositions ComputeLivePositions()
    {
        var maxCars = IRacingSdkConst.MaxNumCars;
        var entries = new List<(int Idx, int Laps, float DistPct, int ClassId)>();
        for (var idx = 0; idx < maxCars; idx++)
        {
            var distPct = _sdk.Data.GetFloat("CarIdxLapDistPct", idx);
            if (distPct < 0) continue; // car not currently on track / not in this session
            var laps = _sdk.Data.GetInt("CarIdxLapCompleted", idx);
            var classId = _sdk.Data.GetInt("CarIdxClass", idx);
            entries.Add((idx, laps, distPct, classId));
        }

        if (entries.Count == 0)
        {
            // Nobody has a valid on-track distance yet (e.g. still sitting in the pit stall before
            // the session goes live) -- fall back to iRacing's own CarIdxPosition/CarIdxClassPosition
            // (the grid order) so Standings/Relative show the real field the instant the car is
            // assumed, instead of staying empty until the first car actually rolls.
            var overallFallback = new Dictionary<int, int>();
            var classFallback = new Dictionary<int, int>();
            for (var idx = 0; idx < maxCars; idx++)
            {
                var position = _sdk.Data.GetInt("CarIdxPosition", idx);
                if (position > 0) overallFallback[idx] = position;
                var classPosition = _sdk.Data.GetInt("CarIdxClassPosition", idx);
                if (classPosition > 0) classFallback[idx] = classPosition;
            }
            return new LivePositions(overallFallback, classFallback);
        }

        var overall = entries
            .OrderByDescending(e => e.Laps).ThenByDescending(e => e.DistPct)
            .Select((e, i) => (e.Idx, Position: i + 1))
            .ToDictionary(x => x.Idx, x => x.Position);

        var byClass = entries
            .GroupBy(e => e.ClassId)
            .SelectMany(group => group
                .OrderByDescending(e => e.Laps).ThenByDescending(e => e.DistPct)
                .Select((e, i) => (e.Idx, Position: i + 1)))
            .ToDictionary(x => x.Idx, x => x.Position);

        return new LivePositions(overall, byClass);
    }

    private void UpdateRelative(LivePositions positions)
    {
        try
        {
            if (!positions.Overall.TryGetValue(_playerCarIdx, out var myPosition)) return;

            var byOffset = new Dictionary<int, bool>();
            foreach (var (idx, position) in positions.Overall)
            {
                if (IsPaceCar(idx)) continue;
                if (idx == _playerCarIdx || position <= myPosition) continue;
                var offset = position - myPosition;
                if (offset > RelativeCarsBehind) continue;
                // CarIdxP2P_Status throws for the whole session (not just this one index) when the
                // current car class doesn't have push-to-pass at all -- let it propagate to the
                // outer catch below rather than swallowing it per-index, so RelativeUpdated simply
                // never fires this session instead of firing once per index with a fabricated false.
                byOffset[offset] = _sdk.Data.GetBool("CarIdxP2P_Status", idx);
            }

            var rows = byOffset.OrderBy(pair => pair.Key).Select(pair => new RelativeCarStatus(pair.Key, pair.Value)).ToList();
            RelativeUpdated?.Invoke(rows);
        }
        catch
        {
            // CarIdxP2P_Status not published this session (no push-to-pass in this car class) --
            // simply don't fire RelativeUpdated; the strip keeps showing its own "sem push-to-pass"
            // idle state instead of a misleading always-false reading.
        }
    }

    // 13/09/2026: full running-order relative (F1-style widget), extending 3-ahead/3-behind --
    // reads the SAME live position ranking as UpdateRelative but is a SEPARATE pass (not merged
    // into it) so a change here can never affect the already-shipped P2P strip's own behavior.
    private void UpdateFullRelative(LivePositions positions)
    {
        try
        {
            // The Relative shows who is physically around the player on track -- any class, any
            // lap -- ordered by distance along the lap, NOT the running order the Standings use.
            // Each row still carries the car's position inside its own class.
            float myPct = _sdk.Data.GetFloat("CarIdxLapDistPct", _playerCarIdx);
            if (myPct < 0) return;
            // Gap = physical distance x the PLAYER's class reference lap (RelativeGap) -- never the fuel
            // tracker's average lap (seeded from history: a 204.9 s sample gave John West "-204.76" live,
            // 24/09/2026, where Kapps showed 0.1).
            double lapTime = _classLapTimeById.TryGetValue(_sdk.Data.GetInt("CarIdxClass", _playerCarIdx), out var classLap) ? classLap
                : _estimatedLapTime ?? 0;

            var around = new List<(int Idx, double Delta)>();
            for (var idx = 0; idx < IRacingSdkConst.MaxNumCars; idx++)
            {
                if (idx == _playerCarIdx) continue;
                float pct = _sdk.Data.GetFloat("CarIdxLapDistPct", idx);
                if (pct < 0 || IsPaceCar(idx) || _soloSession) continue;
                around.Add((idx, RelativeGap.WrappedDelta(pct, myPct))); // > 0 = ahead of the player on track
            }
            var ahead = around.Where(c => c.Delta > 0).OrderBy(c => c.Delta).Take(RelativeCarsMax).ToList();
            var behind = around.Where(c => c.Delta <= 0).OrderByDescending(c => c.Delta).Take(RelativeCarsMax).ToList();

            var rows = new List<RelativeRow> { BuildRelativeRow(_playerCarIdx, 0, 0, positions) };
            for (int i = 0; i < ahead.Count; i++)
                rows.Add(BuildRelativeRow(ahead[i].Idx, -(i + 1), -Math.Abs(ahead[i].Delta) * lapTime, positions));
            for (int i = 0; i < behind.Count; i++)
                rows.Add(BuildRelativeRow(behind[i].Idx, i + 1, Math.Abs(behind[i].Delta) * lapTime, positions));

            FullRelativeUpdated?.Invoke(rows.OrderBy(row => row.PositionOffset).ToList());
        }
        catch
        {
            // Skip this tick -- same defensive posture as every other telemetry read in this class.
        }
    }

    private RelativeRow BuildRelativeRow(int idx, int offset, double gap, LivePositions positions)
    {
        var tireCompound = _sdk.Data.GetInt("CarIdxTireCompound", idx);
        var (p2p, p2pSeconds, p2pCharging) = ReadP2P(idx);
        var identity = GetIdentity(idx);
        var classPosition = positions.ByClass.TryGetValue(idx, out var cp) ? cp : 0;
        var overall = positions.Overall.TryGetValue(idx, out var op) ? op : 0;
        var code = _driverCodesByCarIdx.TryGetValue(idx, out var driverCode) ? driverCode : "?";
        return new RelativeRow(offset, code, gap, tireCompound >= 0 ? tireCompound : null, p2p, p2pSeconds, p2pSeconds, p2pCharging,
            identity.FlagEmoji, identity.LicString, identity.LicColorHex, identity.IRating, identity.CarClassId, identity.ManufacturerBadge,
            idx == _playerCarIdx, classPosition, identity.ClassShortName, identity.ClassColorHex, identity.CarNumber, overall, identity.ClassRank);
    }

    // 14/09/2026: mirrors UpdateFullRelative's shape but ranks by the live class position within
    // the first car class found that differs from the player's own CarIdxClass, for the second,
    // auto-configuring Relative widget instance (multiclass sessions only -- see this plan's own
    // Global Constraints for why there's no manual class picker).
    private void UpdateSecondaryRelative(LivePositions positions)
    {
        try
        {
            var myClass = _sdk.Data.GetInt("CarIdxClass", _playerCarIdx);
            var maxCars = IRacingSdkConst.MaxNumCars;

            int? secondaryClass = null;
            for (var idx = 0; idx < maxCars; idx++)
            {
                if (idx == _playerCarIdx || !positions.ByClass.ContainsKey(idx)) continue;
                var classId = _sdk.Data.GetInt("CarIdxClass", idx);
                if (classId == myClass) continue;
                secondaryClass = classId;
                break; // first differing class found -- deterministic since CarIdx order is stable within a session
            }
            if (secondaryClass is not int targetClass) return; // single-class session -- don't fire

            var byOffset = new List<(int Position, int Idx)>();
            for (var idx = 0; idx < maxCars; idx++)
            {
                if (_sdk.Data.GetInt("CarIdxClass", idx) != targetClass) continue;
                if (!positions.ByClass.TryGetValue(idx, out var classPosition)) continue;
                byOffset.Add((classPosition, idx));
            }
            if (byOffset.Count == 0) return;

            // No player row in this class -- center the window on the class's own leader rather
            // than an offset from the (absent) player position; show the top RelativeCarsBehind*2+1
            // class-classified cars, matching the mockup's own "AO REDOR DE VOCÊ" framing loosely
            // adapted to "top of this class" since the player isn't racing in it.
            var rows = new List<RelativeRow>();
            var ordered = byOffset.OrderBy(pair => pair.Position).Take(RelativeCarsBehind * 2 + 1).ToList();
            foreach (var (position, idx) in ordered)
            {
                if (IsPaceCar(idx)) continue;
                var tireCompound = _sdk.Data.GetInt("CarIdxTireCompound", idx);
                var (p2p, p2pSeconds, p2pCharging) = ReadP2P(idx);

                var code = _driverCodesByCarIdx.TryGetValue(idx, out var driverCode) ? driverCode : "?";
                var identity = GetIdentity(idx);
                rows.Add(new RelativeRow(position, code, null, tireCompound >= 0 ? tireCompound : null, p2p, p2pSeconds, p2pSeconds, p2pCharging, identity.FlagEmoji, identity.LicString, identity.LicColorHex, identity.IRating, identity.CarClassId, identity.ManufacturerBadge, CarNumber: identity.CarNumber, ClassRank: identity.ClassRank));
            }

            SecondaryRelativeUpdated?.Invoke(rows);
        }
        catch
        {
            // Skip this tick.
        }
    }

    // iRacing's own published Strength-of-Field constant (iracing.com/strength-in-numbers):
    // BR1 = 1600 / ln(2). SoF = BR1 * ln(N / Sum(e^(-iRating_i / BR1))).
    private const double SofBr1 = 1600.0 / 0.69314718055994530942;

    private const int SessionStateRacingState = 4;

    private void UpdateStandings(LivePositions positions)
    {
        try
        {
            var raw = new List<(int Idx, int Position, string Code, int Laps, double? LastLap, int? Tire, bool IsPlayer,
                string Flag, string Lic, string? LicHex, int IRating, int ClassId, string Manufacturer, string CarNumber, double? Gap,
                string ClassShortName, string? ClassColorHex, int ClassPosition, bool? P2PActive, int? P2PUsesRemaining, double? P2PSecondsRemaining, bool P2PInCooldown,
                string PitStatus, int ClassRank, double? Progress, double? EstTime, double? BestLap, bool OnPitRoad, bool HasValidLap)>();

            var playerLastLapRaw = _sdk.Data.GetFloat("LapLastLapTime");
            double? playerLastLap = playerLastLapRaw > 0 ? playerLastLapRaw : null;
            bool preGreenGrid = IsPreGreenGrid;
            bool timed = IsTimedSession || preGreenGrid;
            int sessionState = 0;
            try { sessionState = _sdk.Data.GetInt("SessionState"); } catch { }
            bool stopsCount = _isRaceSession && sessionState == SessionStateRacingState;
            var now = DateTime.UtcNow;

            foreach (var (idx, position) in positions.Overall)
            {
                if (IsPaceCar(idx)) continue;
                var lapsCompleted = _sdk.Data.GetInt("CarIdxLap", idx);
                var lastLap = _sdk.Data.GetFloat("CarIdxLastLapTime", idx);
                var tireCompound = _sdk.Data.GetInt("CarIdxTireCompound", idx);
                var code = _driverCodesByCarIdx.TryGetValue(idx, out var driverCode) ? driverCode : "?";
                var identity = GetIdentity(idx);

                // CarIdxF2Time: "race time behind leader or fastest lap time otherwise" -- verified live
                // in practice: there it IS each car's own best lap (== ResultsPositions FastestTime).
                double? f2 = null;
                try { var v = _sdk.Data.GetFloat("CarIdxF2Time", idx); if (v >= 0) f2 = v; }
                catch { /* not published this session type */ }
                double? best = null;
                try { var b = _sdk.Data.GetFloat("CarIdxBestLapTime", idx); if (b > 0) best = b; } catch { }
                if (timed && f2 is > 0) best = f2;
                if (preGreenGrid) best = _gridBestLap.TryGetValue(idx, out var qualifyLap) ? qualifyLap : null;

                double? estTime = null;
                try { var e = _sdk.Data.GetFloat("CarIdxEstTime", idx); if (e >= 0) estTime = e; } catch { }
                var pct = _sdk.Data.GetFloat("CarIdxLapDistPct", idx);
                double? progress = RaceLapEstimator.Progress(_sdk.Data.GetInt("CarIdxLapCompleted", idx), pct);

                var classPosition = positions.ByClass.TryGetValue(idx, out var cp) ? cp : 0;
                var onPitRoad = false;
                try { onPitRoad = _sdk.Data.GetBool("CarIdxOnPitRoad", idx); }
                catch { /* channel absent outside an active driving session */ }
                var pitStatus = _pitStops.Update(idx, onPitRoad, lapsCompleted, stopsCount, now);
                var (p2pActive, p2pSeconds, p2pCharging) = ReadP2P(idx);
                // "Valid lap" (qualifying gate): iRacing only classifies (CarIdxPosition > 0) a car that has
                // a timed lap.
                bool hasValidLap = _sdk.Data.GetInt("CarIdxPosition", idx) > 0 && (best is not null || f2 is > 0);

                // After the flag: the official classification (finished cars have left the world).
                if (_finalResults.TryGetValue(idx, out var final))
                {
                    if (lastLap <= 0 && final.LastTime > 0) lastLap = (float)final.LastTime;
                    progress = final.LapsComplete;
                    estTime = null;
                    f2 = final.Time >= 0 ? final.Time : null;
                    lapsCompleted = final.LapsComplete;
                }

                raw.Add((idx, position, code, lapsCompleted, lastLap > 0 ? lastLap : null, tireCompound >= 0 ? tireCompound : null,
                    idx == _playerCarIdx, identity.FlagEmoji, identity.LicString, identity.LicColorHex, identity.IRating,
                    identity.CarClassId, identity.ManufacturerBadge, identity.CarNumber, timed ? null : f2, identity.ClassShortName, identity.ClassColorHex, classPosition,
                    p2pActive, p2pSeconds, p2pSeconds, p2pCharging, pitStatus, identity.ClassRank, progress, estTime, best, onPitRoad, hasValidLap));
            }

            var ordered = raw.OrderBy(r => r.Position).ToList();

            // Starting grid (race): SessionInfo first (RefreshStartingGrid); otherwise the order iRacing
            // shows before the green, sampled here while SessionState < Racing.
            if (_isRaceSession && sessionState is > 0 and < SessionStateRacingState)
                for (var idx = 0; idx < IRacingSdkConst.MaxNumCars; idx++)
                {
                    var gridPos = _sdk.Data.GetInt("CarIdxPosition", idx);
                    if (gridPos > 0) _preGreenGrid[idx] = gridPos;
                }
            var overallGrid = _grid.Count > 0 ? _grid : _preGreenGrid;
            var classOfIdx = ordered.ToDictionary(r => r.Idx, r => r.ClassId);
            var classGrid = _isRaceSession && overallGrid.Count > 0
                ? StartingGrid.ByClass(overallGrid.Where(kv => classOfIdx.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value), i => classOfIdx[i])
                : new Dictionary<int, int>();

            var classified = ordered.Where(r => r.IRating > 1).ToList();
            double? sof = classified.Count > 0 ? Sof.Compute(classified.Select(r => r.IRating)) : null;

            // ΔiR: projected iRating change in the current order, per class (IRatingProjection -- the
            // community-standard iRacing formula), gated by session: never in practice, in qualifying only
            // once the player has a valid lap (and only for cars with one), in a race always.
            bool playerHasValidLap = ordered.Any(r => r.IsPlayer && r.HasValidLap);
            var deltaByIdx = new Dictionary<int, double>();
            if (_sessionKind is SessionKind.Race or SessionKind.Qualify)
                foreach (var cls in ordered.GroupBy(r => r.ClassId))
                    foreach (var kv in IRatingProjection.Compute(cls.Select(r => new IRatingEntry(r.Idx, r.IRating, r.ClassPosition > 0 ? r.ClassPosition : r.Position))))
                        deltaByIdx[kv.Key] = kv.Value;

            var intervals = ClassIntervals.Compute(
                ordered.Select(o => new IntervalInput(o.ClassId, o.Position, o.ClassPosition, o.Progress, o.EstTime, o.Gap, o.BestLap)).ToList(),
                raceMode: !timed, _classLapTimeById);

            // Practice/qualifying gap: best lap minus the class's best lap.
            var classBest = ordered.Where(r => r.BestLap is not null).GroupBy(r => r.ClassId).ToDictionary(g => g.Key, g => g.Min(r => r.BestLap!.Value));

            var rows = new List<StandingsRow>();
            var leader = ordered.FirstOrDefault();
            for (var i = 0; i < ordered.Count; i++)
            {
                var r = ordered[i];
                double? deltaIR = deltaByIdx.TryGetValue(r.Idx, out var d) && IRatingProjection.ShowFor(_sessionKind, playerHasValidLap, r.HasValidLap) ? Math.Round(d) : null;
                double? lapDelta = r.LastLap is double own && playerLastLap is double mine ? own - mine : null;

                double? gapToLeader;
                if (timed)
                    gapToLeader = r.BestLap is double b && classBest.TryGetValue(r.ClassId, out var cb) ? b - cb : null;
                else
                {
                    // CarIdxF2Time is authoritative across different laps but only advances at timing
                    // lines; on the same lap CarIdxEstTime moves continuously.
                    gapToLeader = r.Gap;
                    if (r.Position != leader.Position && r.Laps == leader.Laps && r.EstTime is double ce && leader.EstTime is double le)
                        gapToLeader = Math.Max(0, ce - le);
                }

                var interval = intervals[i];
                int? start = classGrid.TryGetValue(r.Idx, out var g) ? g : null;
                int? change = _isRaceSession ? StartingGrid.Change(start, r.ClassPosition) : null;

                rows.Add(new StandingsRow(r.Position, r.Code, r.Laps, r.LastLap, r.Tire, r.IsPlayer, r.Flag, r.Lic,
                    r.LicHex, r.IRating, r.ClassId, r.Manufacturer, gapToLeader, deltaIR, lapDelta, r.ClassShortName, r.ClassColorHex, r.ClassPosition, interval.Seconds,
                    r.P2PActive, r.P2PUsesRemaining, r.P2PSecondsRemaining, r.P2PInCooldown, r.PitStatus, r.CarNumber, r.ClassRank,
                    start, change, interval.Laps, r.BestLap, r.OnPitRoad, timed));
            }

            // Race: lap history + Kapps' per-class lap projection (ClassRaceProjection).
            var classLaps = new Dictionary<int, ClassLapInfo>();
            if (_isRaceSession && !preGreenGrid && _finalResults.Count == 0)
            {
                foreach (var r in ordered)
                {
                    try { _lapHistory.Update(r.Idx, _sdk.Data.GetInt("CarIdxLapCompleted", r.Idx), _sdk.Data.GetFloat("CarIdxLastLapTime", r.Idx)); } catch { }
                }
                var classLeaders = ordered.GroupBy(r => r.ClassId)
                    .Select(g => g.OrderBy(r => r.ClassPosition > 0 ? r.ClassPosition : int.MaxValue).ThenBy(r => r.Position).First())
                    .ToList();
                double remainNow = -1;
                try { remainNow = _sdk.Data.GetDouble("SessionTimeRemain"); } catch { }
                var projections = ordered.Count > 0 && sessionState == SessionStateRacingState
                    ? ClassRaceProjection.Compute(classLeaders.Select(l => new ClassLeader(l.ClassId, l.Progress ?? 0, _lapHistory.RecentAverage(l.Idx))).ToList(), ordered[0].ClassId, remainNow)
                    : new Dictionary<int, double>();
                foreach (var l in classLeaders)
                {
                    int lap = 0;
                    try { lap = _sdk.Data.GetInt("CarIdxLap", l.Idx); } catch { }
                    classLaps[l.ClassId] = new ClassLapInfo(lap, projections.TryGetValue(l.ClassId, out var pr) ? pr : null);
                }
            }
            _classLaps = classLaps;

            // Per-class driver counts (Kapps: the class total, or "2/14" in practice/qualifying while only some
            // have a time). Entrants = the session's driver list minus pace car/spectators.
            var entrants = new List<(int CarIdx, int ClassId)>();
            try
            {
                foreach (var d in _sdk.Data.SessionInfo?.DriverInfo?.Drivers ?? [])
                    if (d.CarIsPaceCar == 0 && d.IsSpectator == 0) entrants.Add((d.CarIdx, d.CarClassID));
            }
            catch { entrants.Clear(); }
            // Race: Kapps shows the plain class total the whole race (live 24/09/2026: "14"/"13"/"13" on the grid
            // and mid-race with a towed player who had no lap time), so only practice/qualifying count times.
            var withTime = timed && !preGreenGrid ? ordered.Where(r => r.BestLap is > 0).Select(r => r.Idx).ToList() : new List<int>();
            var classCounts = entrants.Count > 0 ? ClassDriverCounts.Compute(entrants, withTime) : null;
            int playerClassId = ordered.FirstOrDefault(r => r.IsPlayer).ClassId;
            if (!ordered.Any(r => r.IsPlayer)) playerClassId = -1;

            StandingsUpdated?.Invoke(rows);
            SessionStatusUpdated?.Invoke(BuildSessionStatus(ordered.Count, sof) with { ClassCounts = classCounts, PlayerClassId = playerClassId, ClassLaps = classLaps.Count > 0 ? classLaps : null });
        }
        catch
        {
            // Skip this tick.
        }
    }

    // Header block shared by Standings and Relative -- class/session type/lap count/flag are all
    // real SDK fields; DriverCount/StrengthOfField come from the same full-field scan UpdateStandings
    // just did (cheap reuse rather than a second pass).
    private SessionStatus BuildSessionStatus(int driverCount, double? sof)
    {
        var carClassShortName = "";
        var sessionTypeText = "";
        var playerCarName = "";
        int? currentLap = null;
        int? totalLaps = null;
        bool totalEstimated = false;
        double? totalProjected = null;
        try
        {
            var sessionInfo = _sdk.Data.SessionInfo;
            var driver = sessionInfo?.DriverInfo?.Drivers?.FirstOrDefault(d => d.CarIdx == _playerCarIdx);
            carClassShortName = driver?.CarClassShortName?.ToUpperInvariant() ?? "";
            playerCarName = driver?.CarScreenName ?? "";

            var currentSessionNum = sessionInfo?.SessionInfo?.CurrentSessionNum ?? -1;
            var session = sessionInfo?.SessionInfo?.Sessions?.FirstOrDefault(s => s.SessionNum == currentSessionNum);
            sessionTypeText = session?.SessionType?.ToUpperInvariant() ?? "";

            // In a race the header shows the RACE lap (the leader's), like Kapps' "R 14/30".
            var lap = _isRaceSession && _leaderLap > 0 ? _leaderLap : _sdk.Data.GetInt("Lap");
            if (lap > 0) currentLap = lap;
            if (_raceEstimate is { } estimate) { totalLaps = estimate.TotalLaps; totalEstimated = estimate.IsEstimate; totalProjected = estimate.ProjectedTotalLaps; }
            else if (session?.SessionLaps is string lapsText && int.TryParse(lapsText, out var parsedLaps)) totalLaps = parsedLaps;
            // Race over: the real length (Kapps "R 13/12" in the cool-down), no projection.
            if (_finalResults.Count > 0 && FinalResults.TotalLaps(_finalResults.Values) is int finalLaps)
            { totalLaps = finalLaps; totalEstimated = false; totalProjected = null; }
        }
        catch { /* session info momentarily incomplete -- leave whatever was resolved */ }

        var (flagText, flagColorHex) = DecodeSessionFlag();
        return new SessionStatus(carClassShortName, sessionTypeText, currentLap, totalLaps, flagText, flagColorHex, sof, driverCount, playerCarName, totalEstimated, TotalLapsProjected: totalProjected);
    }

    // SessionFlags bitmask -- confirmed real (sajax.github.io/irsdkdocs/telemetry/sessionflags.html),
    // bit values per iRacing's own public irsdk_defines.h. Checked most-severe-first since several
    // bits can be set at once (e.g. yellowWaving + caution).
    private const int FlagCheckered = 0x00000001;
    private const int FlagRed = 0x00000010;
    private const int FlagYellow = 0x00000008;
    private const int FlagYellowWaving = 0x00000100;
    private const int FlagCaution = 0x00004000;
    private const int FlagCautionWaving = 0x00008000;
    private const int FlagWhite = 0x00000002;
    private const int FlagGreen = 0x00000004;

    private (string Text, string ColorHex) DecodeSessionFlag()
    {
        try
        {
            var flags = _sdk.Data.GetInt("SessionFlags");
            if ((flags & FlagRed) != 0) return ("VERMELHA", "#FFE2483D");
            if ((flags & (FlagYellow | FlagYellowWaving | FlagCaution | FlagCautionWaving)) != 0) return ("AMARELA", "#FFE0A52C");
            if ((flags & FlagCheckered) != 0) return ("QUADRICULADA", "#FFF2F4F7");
            if ((flags & FlagWhite) != 0) return ("BRANCA", "#FFF2F4F7");
            if ((flags & FlagGreen) != 0) return ("GREEN", "#FF20E884");
            return ("--", "#FF9AA3AF");
        }
        catch
        {
            return ("--", "#FF9AA3AF");
        }
    }

    private void UpdatePlayerCarStatus()
    {
        try
        {
            double? brakeBias = null;
            try { brakeBias = _sdk.Data.GetFloat("dcBrakeBias"); }
            catch { try { brakeBias = _sdk.Data.GetFloat("dcPeakBrakeBias"); } catch { /* not published this car */ } }

            string? rubberState = null;
            try
            {
                var sessionInfo = _sdk.Data.SessionInfo;
                var currentSessionNum = sessionInfo?.SessionInfo?.CurrentSessionNum ?? -1;
                rubberState = LatchedRubber(ResolvedRubber(sessionInfo, currentSessionNum));
                // "17" or "unlimited"; only a real number is a limit.
                var limitText = sessionInfo?.WeekendInfo?.WeekendOptions?.IncidentLimit?.ToString();
                _incidentLimit = int.TryParse(limitText, out var limit) && limit > 0 ? limit : null;
            }
            catch { /* session info momentarily incomplete -- skip this tick's rubber read */ }

            double? trackTemp = null;
            try { var t = _sdk.Data.GetFloat("TrackTemp"); trackTemp = t; } catch { /* not published */ }

            var lastLap = _sdk.Data.GetFloat("LapLastLapTime");
            double? lastLapSeconds = lastLap > 0 ? lastLap : null;
            if (lastLap > 0 && (_bestLapTimeSeconds is not double best || lastLap < best))
                _bestLapTimeSeconds = lastLap;

            int? incidents = null;
            try { var n = _sdk.Data.GetInt("PlayerCarMyIncidentCount"); if (n >= 0) incidents = n; } catch { /* not published */ }

            PlayerCarStatusUpdated?.Invoke(new PlayerCarStatus(brakeBias, rubberState, _bestLapTimeSeconds, lastLapSeconds, trackTemp, incidents, _incidentLimit));
        }
        catch
        {
            // Skip this tick.
        }
    }

    /// <summary>Track rubber as captured on the first read of this session (driver's rule: fixed for the
    /// whole session, no carry-over; a new session captures again). Before the session key is known the
    /// live value passes through.</summary>
    /// <summary>The current session's rubber with "carry over" resolved to the earlier session's state (RubberState).</summary>
    private static string? ResolvedRubber(IRacingSdkSessionInfo? sessionInfo, int currentSessionNum)
    {
        var sessions = sessionInfo?.SessionInfo?.Sessions;
        if (sessions is null) return null;
        var states = new Dictionary<int, string?>();
        foreach (var session in sessions) states[session.SessionNum] = session.SessionTrackRubberState;
        return RubberState.Resolve(states, currentSessionNum);
    }

    private string? LatchedRubber(string? live) => _sessionKey is SessionKey key ? RubberLatch.Get(key, live) : live;

    private void UpdateOnTrackState()
    {
        try
        {
            var surface = _sdk.Data.GetInt("PlayerTrackSurface");
            // TrkLoc is -1 only when the player is not in a car.  Pits, grid and off-track are
            // all valid in-car states and must keep the overlay visible for setup/start work.
            var isOnTrack = surface != -1;
            if (isOnTrack == _isOnTrack) return; // only fire on a real transition, not every tick

            _isOnTrack = isOnTrack;
            OnTrackStateChanged?.Invoke(_isOnTrack);
        }
        catch
        {
            // Skip this tick -- keep the last known on-track state rather than guessing.
        }
    }

    // 14/09/2026: "aquele que auxilia o race start... uso bastante ele pra largar no SF23" --
    // Clutch/Throttle/Speed all confirmed real telemetry. StationarySpeedThreshold (0.5 m/s, ~1.8
    // km/h) tolerates GPS/physics jitter while the car is genuinely held stopped on the grid,
    // without waiting for an exact 0.0.
    private const double StationarySpeedThreshold = 0.5;

    private void UpdateRaceStart()
    {
        try
        {
            var speed = _sdk.Data.GetFloat("Speed");
            var clutch = _sdk.Data.GetFloat("Clutch");
            var throttle = _sdk.Data.GetFloat("Throttle");
            var rpm = _sdk.Data.GetFloat("RPM"); // same real channel UpdateEngineSample already reads

            // SessionInfo is a large parsed object.  Looking it up and LINQ-scanning it on every
            // pedal frame was enough to make the Start Helper feel like 20 FPS.  Its race flag is
            // cached once at session detection; only the three real-time scalar channels remain.
            var shouldShow = _isRaceSession && Math.Abs(speed) < StationarySpeedThreshold;
            RaceStartUpdated?.Invoke(new RaceStartStatus(clutch * 100.0, throttle * 100.0, shouldShow, rpm));
        }
        catch
        {
            // Skip this tick.
        }
    }

    private const int SessionStateRacing = 4;

    /// <summary>Fuel calculator, Kapps-style: consumption from clean green-flag laps only (see
    /// <see cref="FuelTracker"/>), laps left in the race from <see cref="RaceLapEstimator"/> (lap- and
    /// time-limited), fuel to finish = laps left x average, margin = level - fuel to finish. Every input
    /// is a telemetry channel read each tick -- never the SessionInfo YAML, whose occasional failed
    /// parse used to blank the last row every few frames (the "flicker").</summary>
    private void UpdateFuel()
    {
        try
        {
            var fuelLevel = _sdk.Data.GetFloat("FuelLevel");
            var fuelUsePerHour = _sdk.Data.GetFloat("FuelUsePerHour");
            var lapCompleted = _sdk.Data.GetInt("LapCompleted");

            bool onPitRoad = false, notGreen = false, towed = false;
            try { onPitRoad = _sdk.Data.GetBool("OnPitRoad"); } catch { }
            try
            {
                var state = _sdk.Data.GetInt("SessionState");
                var flags = _sdk.Data.GetInt("SessionFlags");
                notGreen = state != SessionStateRacing || (flags & (FlagCaution | FlagCautionWaving)) != 0;
            }
            catch { }
            try { towed = _sdk.Data.GetInt("PlayerTrackSurface") == -1; } catch { }
            double lastLapTime = 0;
            try { lastLapTime = _sdk.Data.GetFloat("LapLastLapTime"); } catch { }

            bool addedLap = _fuelTracker.Update(fuelLevel, lapCompleted, onPitRoad || notGreen || towed, lastLapTime);
            if (addedLap && FuelUpdated is not null && _carId > 0 && _fuelTracker.AverageFuel is double learned)
                FuelHistory.Value.Put(_carId, _trackId, learned, _fuelTracker.AverageLapTime);

            double? avgFuelPerLap = _fuelTracker.AverageFuel;
            double? playerLapDistPct = null;
            try { var pct = _sdk.Data.GetFloat("LapDistPct"); if (pct >= 0) playerLapDistPct = pct; } catch { }
            double? avgLapTime = _fuelTracker.AverageLapTime ?? (lastLapTime > 0 ? lastLapTime : _estimatedLapTime);
            double? lapsRemaining = avgFuelPerLap is double perLap && perLap > 0 ? fuelLevel / perLap : null;
            double? timeRemaining = lapsRemaining is double laps && avgLapTime is double lapTime2 ? laps * lapTime2 : null;

            // Race length (lap- or time-limited) from live channels.
            _raceEstimate = null;
            if (_isRaceSession)
            {
                int? lapsLimit = null;
                double? timeLeft = null;
                try { lapsLimit = _sdk.Data.GetInt("SessionLapsTotal"); } catch { }
                try
                {
                    double? remain = _sdk.Data.GetDouble("SessionTimeRemain");
                    double? total = null;
                    try { total = _sdk.Data.GetDouble("SessionTimeTotal"); } catch { }
                    timeLeft = RaceLapEstimator.RaceTimeRemaining(_sdk.Data.GetInt("SessionState"), remain, total);
                }
                catch { }
                double leader = 0, player = 0;
                int leaderIdx = -1;
                for (var idx = 0; idx < IRacingSdkConst.MaxNumCars; idx++)
                {
                    if (RaceLapEstimator.Progress(_sdk.Data.GetInt("CarIdxLapCompleted", idx), _sdk.Data.GetFloat("CarIdxLapDistPct", idx)) is not double progress) continue;
                    if (idx == _playerCarIdx) player = progress;
                    if (progress > leader && !IsPaceCar(idx)) { leader = progress; leaderIdx = idx; }
                }
                _leaderLap = leaderIdx >= 0 ? _sdk.Data.GetInt("CarIdxLap", leaderIdx) : 0;
                // Single class: the player's own pace (verified against Kapps). Multiclass with a faster
                // class leading: that leader's last lap, else its class reference lap.
                double? leaderLapTime = null;
                if (leaderIdx >= 0 && leaderIdx != _playerCarIdx)
                {
                    int leaderClass = _sdk.Data.GetInt("CarIdxClass", leaderIdx);
                    if (leaderClass != _sdk.Data.GetInt("CarIdxClass", _playerCarIdx))
                    {
                        var leaderLast = _sdk.Data.GetFloat("CarIdxLastLapTime", leaderIdx);
                        leaderLapTime = leaderLast > 1 ? leaderLast : _classLapTimeById.TryGetValue(leaderClass, out var est) ? est : (double?)null;
                    }
                }
                int raceState = 0;
                try { raceState = _sdk.Data.GetInt("SessionState"); } catch { }
                double? sessionTotal = null;
                try { var tt = _sdk.Data.GetDouble("SessionTimeTotal"); if (tt > 0) sessionTotal = tt; } catch { }
                bool leaderHasRaceLap = leaderIdx >= 0 && _sdk.Data.GetFloat("CarIdxLastLapTime", leaderIdx) > 1;
                double? pole = RaceLapEstimator.PoleLapTime(_grid, _gridBestLap);
                // Own pace for the projection: laps actually driven, never the history seed. The race length
                // itself follows the overall leader's last-5 average (ClassRaceProjection, Kapps).
                double? ownPace = _lapHistory.RecentAverage(_playerCarIdx) ?? _fuelTracker.MeasuredAverageLapTime ?? (lastLapTime > 1 ? lastLapTime : pole ?? _estimatedLapTime);
                if (leaderIdx >= 0 && _lapHistory.RecentAverage(leaderIdx) is double leaderAvg) leaderLapTime = leaderAvg;
                _raceEstimate = pole is double poleLap && sessionTotal is double sessionLength && (raceState < SessionStateRacingState || !leaderHasRaceLap)
                    ? RaceLapEstimator.EstimateFromPole(lapsLimit, sessionLength, poleLap, player)
                    : RaceLapEstimator.Estimate(lapsLimit, timeLeft, leader, player, ownPace, leaderLapTime);
            }

            double? refuelToFull = null;
            double? plannedPitFuel = null;
            try
            {
                var fuelPct = _sdk.Data.GetFloat("FuelLevelPct");
                if (fuelPct is > 0.001f and <= 1.0f)
                    refuelToFull = Math.Max(0, fuelLevel / fuelPct - fuelLevel);
            }
            catch { /* optional channel */ }
            try
            {
                var blackBoxFuel = _sdk.Data.GetFloat("PitSvFuel");
                if (blackBoxFuel >= 0) plannedPitFuel = blackBoxFuel;
            }
            catch { /* no pit service field in this session */ }

            // Signed: positive = fuel still to add, negative = surplus (the widget shows the margin).
            double? fuelBurnToFinish = _raceEstimate is { } race && avgFuelPerLap is double lapFuel && lapFuel > 0 ? race.PlayerLapsRemaining * lapFuel : null;
            double? fuelNeededForFinish = fuelBurnToFinish is double burnAll ? burnAll - fuelLevel : null;
            double? fuelAfterPit = plannedPitFuel is double planned ? fuelLevel + planned : null;
            double? fuelAtFinish = fuelAfterPit is double afterPit && fuelBurnToFinish is double burn ? afterPit - burn : null;

            FuelUpdated?.Invoke(new FuelStatus(fuelLevel, fuelUsePerHour, avgFuelPerLap, lapsRemaining, timeRemaining, refuelToFull,
                fuelNeededForFinish, plannedPitFuel, fuelAfterPit, fuelAtFinish, _fuelTracker.LastLapUsed, _fuelTracker.LastLapDirty,
                _fuelTracker.MaxFuel, avgLapTime, _raceEstimate?.PlayerLapsRemaining, _raceEstimate?.TotalLaps, _raceEstimate?.IsEstimate ?? false,
                _fuelTracker.AverageIsFromHistory, playerLapDistPct));
        }
        catch
        {
            // Skip this tick.
        }
    }

    private void UpdateWeather()
    {
        try
        {
            var airTemp = _sdk.Data.GetFloat("AirTemp");
            var trackTemp = _sdk.Data.GetFloat("TrackTemp");
            var precipitation = _sdk.Data.GetFloat("Precipitation") * 100.0;
            var trackWetness = _sdk.Data.GetInt("TrackWetness");
            var declaredWet = _sdk.Data.GetBool("WeatherDeclaredWet");

            // WindVel/WindDir are independently optional (some sessions/replays omit them), same
            // defensive pattern as every other per-field try/catch in this method -- spec §8 requires
            // wind+direction as a distinct field, never fabricated when absent.
            double? windSpeed = null;
            double? windDirectionDeg = null;
            try { windSpeed = _sdk.Data.GetFloat("WindVel"); } catch { /* optional channel */ }
            try { windDirectionDeg = _sdk.Data.GetFloat("WindDir") * (180.0 / Math.PI); } catch { /* optional channel */ }
            double? humidityPct = null;
            try { humidityPct = _sdk.Data.GetFloat("RelativeHumidity") * 100.0; } catch { /* optional channel */ }

            var maxCars = IRacingSdkConst.MaxNumCars;
            var positions = new List<TrackPositionDot>();
            for (var idx = 0; idx < maxCars; idx++)
            {
                var lapDistPct = _sdk.Data.GetFloat("CarIdxLapDistPct", idx);
                if (lapDistPct < 0) continue; // car not currently on track / not in this session
                var code = _driverCodesByCarIdx.TryGetValue(idx, out var driverCode) ? driverCode : "?";
                positions.Add(new TrackPositionDot(code, lapDistPct, idx == _playerCarIdx));
            }

            string? rubberState = null;
            try
            {
                var sessionInfo = _sdk.Data.SessionInfo;
                var currentSessionNum = sessionInfo?.SessionInfo?.CurrentSessionNum ?? -1;
                rubberState = LatchedRubber(ResolvedRubber(sessionInfo, currentSessionNum));
            }
            catch { /* optional session metadata */ }
            WeatherUpdated?.Invoke(new WeatherStatus(airTemp, trackTemp, precipitation, trackWetness, declaredWet, rubberState, positions, windSpeed, windDirectionDeg, humidityPct));
        }
        catch
        {
            // Skip this tick.
        }
    }

    private readonly RadarSideAssigner _radarSides = new();

    /// <summary>Kapps-style radar: every car within <see cref="RadarMaxRangeMeters"/> along the lap,
    /// at its real longitudinal distance, placed in a lane by <see cref="RadarSideAssigner"/> from the
    /// player's CarLeftRight flag (iRacing's only lateral information).</summary>
    private void UpdateRadar()
    {
        try
        {
            var leftRight = _soloSession ? 1 : _sdk.Data.GetInt("CarLeftRight");
            // irsdk_CarLeftRight: 0=Off, 1=Clear, 2=CarLeft, 3=CarRight, 4=CarLeftRight, 5=2CarsLeft, 6=2CarsRight.
            var blindLeft = leftRight is 2 or 4 or 5;
            var blindRight = leftRight is 3 or 4 or 6;

            var blips = new List<RadarBlip>();
            var hasTrackLength = _trackLengthMeters is double trackLength0 && trackLength0 > 0;
            if (hasTrackLength)
            {
                var trackLength = _trackLengthMeters!.Value;
                var myDistPct = _sdk.Data.GetFloat("CarIdxLapDistPct", _playerCarIdx);
                bool iAmOnPitRoad = false;
                try { iAmOnPitRoad = _sdk.Data.GetBool("CarIdxOnPitRoad", _playerCarIdx); } catch { }
                var near = new List<(int Idx, double DistanceMeters)>();
                if (myDistPct >= 0)
                {
                    for (var idx = 0; idx < IRacingSdkConst.MaxNumCars; idx++)
                    {
                        if (idx == _playerCarIdx || _soloSession) continue;
                        var theirDistPct = _sdk.Data.GetFloat("CarIdxLapDistPct", idx);
                        if (theirDistPct < 0) continue; // not on track / not in this session
                        // A car in the pit lane is only a neighbour when the player is in the pit lane too.
                        if (_sdk.Data.GetBool("CarIdxOnPitRoad", idx) != iAmOnPitRoad) continue;

                        double delta = theirDistPct - myDistPct;
                        if (delta > 0.5) delta -= 1.0;
                        if (delta < -0.5) delta += 1.0;
                        var distanceMeters = delta * trackLength;
                        if (Math.Abs(distanceMeters) > RadarMaxRangeMeters || IsPaceCar(idx)) continue;
                        near.Add((idx, distanceMeters));
                    }
                }

                var sides = _radarSides.Assign(near, leftRight);
                foreach (var (idx, distance) in near)
                {
                    var code = _driverCodesByCarIdx.TryGetValue(idx, out var driverCode) ? driverCode : "?";
                    blips.Add(new RadarBlip(distance, code, sides.TryGetValue(idx, out var side) ? side : RadarSide.Center));
                }
            }

            var (leftOffset, rightOffset) = RadarSideOffsets.Nearest(blips, blindLeft, blindRight);
            RadarUpdated?.Invoke(new RadarStatus(blindLeft, blindRight, blips, hasTrackLength, leftOffset, rightOffset));
        }
        catch
        {
            // Skip this tick.
        }
    }

    public void Dispose()
    {
        _sdk.OnSessionInfo -= OnSessionInfo;
        _sdk.OnTelemetryData -= OnTelemetryData;
        _sdk.OnDisconnected -= OnDisconnected;
        _sdk.Stop();
    }
}
