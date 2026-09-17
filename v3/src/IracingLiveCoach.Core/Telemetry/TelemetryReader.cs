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
public record RelativeRow(int PositionOffset, string DriverCode, double? GapSeconds, int? TireCompound, bool? P2PActive, int? P2PUsesRemaining, double? P2PSecondsRemaining, bool P2PInCooldown, string FlagEmoji, string LicString, string? LicColorHex, int IRating, int CarClassId, string ManufacturerBadge, bool IsPlayer = false, int ClassPosition = 0, string ClassShortName = "", string? ClassColorHex = null, string CarNumber = "");

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
public record StandingsRow(int Position, string DriverCode, int LapsCompleted, double? LastLapTime, int? TireCompound, bool IsPlayer, string FlagEmoji, string LicString, string? LicColorHex, int IRating, int CarClassId, string ManufacturerBadge, double? GapToLeaderSeconds, double? EstimatedDeltaIRating, double? LapDeltaVsPlayerSeconds, string ClassShortName, string? ClassColorHex, int ClassPosition, double? IntervalSeconds, bool? P2PActive, int? P2PUsesRemaining, double? P2PSecondsRemaining, bool P2PInCooldown, string PitStatus, string CarNumber = "");

/// <summary>One full-field-tick session summary for the Standings/Relative widgets' header block --
/// class/session/lap/flag are all real SDK fields; StrengthOfField uses iRacing's own published SoF
/// formula (BR1 = 1600/ln(2), SoF = BR1 * ln(N / Σ e^(-iRating_i / BR1))) over the current field's
/// real iRatings -- see iracing.com/strength-in-numbers for the source formula.</summary>
public record SessionStatus(string CarClassShortName, string SessionTypeText, int? CurrentLap, int? TotalLaps, string SessionFlagText, string SessionFlagColorHex, double? StrengthOfField, int DriverCount);

/// <summary>The player's own current car status for the Relative/Standings widgets' footer.
/// BrakeBiasPct/TrackRubberState are null if the current car/session doesn't publish that channel
/// (see UpdatePlayerCarStatus's own try/catch per field). BestLapTimeSeconds is the minimum
/// LapLastLapTime observed so far this session -- null until the player has completed one lap.
/// TrackTempC reuses the same real "TrackTemp" channel the Weather widget already reads.</summary>
public record PlayerCarStatus(double? BrakeBiasPct, string? TrackRubberState, double? BestLapTimeSeconds, double? LastLapTimeSeconds, double? TrackTempC);

/// <summary>One tick's fuel state. AverageFuelPerLapLiters/LapsRemaining/TimeRemainingSeconds are
/// null until at least one full lap has completed since the app started watching (see UpdateFuel's
/// own doc comment) -- never show a number computed from zero samples.</summary>
public record FuelStatus(double FuelLevelLiters, double FuelUsePerHourLiters, double? AverageFuelPerLapLiters, double? LapsRemaining, double? TimeRemainingSeconds, double? RefuelToFullLiters = null, double? FuelNeededForFinishLiters = null, double? PlannedPitFuelLiters = null, double? FuelAfterPitLiters = null, double? FuelAtFinishLiters = null);

/// <summary>One car's current position around the lap (0.0 at start/finish, approaching 1.0 as it
/// completes the lap) -- feeds the Weather widget's linear "track usage" bar. Deliberately NOT a
/// real track shape (see the spec's own Phase 2 section for why).</summary>
public record TrackPositionDot(string DriverCode, double LapDistPct, bool IsPlayer);

/// <summary>One (throttled, ~10Hz) weather/track-usage snapshot.</summary>
public record WeatherStatus(double AirTempC, double TrackTempC, double PrecipitationPct, int TrackWetness, bool WeatherDeclaredWet, string? TrackRubberState, List<TrackPositionDot> CarPositions, double? WindSpeedMs = null, double? WindDirectionDeg = null);

/// <summary>One far-field car's signed distance from the player along the lap (negative = behind,
/// positive = ahead), converted from CarIdxLapDistPct using the track's own length. Deliberately
/// carries NO lateral position -- the SDK does not expose one for cars outside the immediate
/// blind-spot window (see CarLeftRight's own doc comment on RadarStatus).</summary>
public record RadarBlip(double DistanceMeters, string DriverCode);

/// <summary>One (throttled, ~10Hz) radar snapshot. BlindSpotLeft/Right come from CarLeftRight, a
/// coarse, NON-per-car signal -- true means "something is right next to you on that side", not
/// "car X is on that side". Blips are the separate far-field distance list (see RadarBlip); the
/// two are rendered differently on purpose (see this plan's own Global Constraints) so a driver
/// never mistakes a far blip's centered position for "directly in my lane".</summary>
public record RadarStatus(bool BlindSpotLeft, bool BlindSpotRight, List<RadarBlip> Blips, bool HasTrackLength);

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

    // UpdateInterval=1 makes IRSDKSharper fire OnTelemetryData on every sim tick (60Hz for most
    // cars) instead of silently skipping frames -- the app's own tick counters below are what
    // decide how often each widget actually reacts, not this.
    private readonly IRacingSdk _sdk = new() { UpdateInterval = 1 };
    private LiveCoachEngine? _engine;
    private bool _sessionDetected;
    private bool _isRaceSession;
    private int _playerCarIdx = -1;
    private Dictionary<int, string> _driverCodesByCarIdx = new();

    // 13/09/2026: rolling-average fuel calculator state -- see UpdateFuel's own doc comment for
    // why a per-lap rolling average is used instead of the SDK's own instantaneous FuelUsePerHour.
    private const int FuelWindowSize = 5;
    private double? _lastFuelLevel;
    private int _lastLapCompleted = -1;
    private readonly Queue<double> _fuelPerLapWindow = new();
    private readonly Queue<double> _lapTimeWindow = new();

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
    private const double RadarMaxRangeMeters = 100.0;

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
    private readonly Dictionary<int, DateTime> _pitRoadEnteredUtcByCarIdx = new();
    private readonly Dictionary<int, string> _lastPitStatusByCarIdx = new();
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
    private readonly Dictionary<int, int> _lastP2PCountByCarIdx = new();
    private readonly Dictionary<int, DateTime> _p2pChargingUntilByCarIdx = new();

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

    private (string FlagEmoji, string LicString, string? LicColorHex, int IRating, int CarClassId, string ManufacturerBadge, string ClassShortName, string? ClassColorHex, string CarNumber) GetIdentity(int carIdx)
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
        return (flagEmoji, licString, licColorHex, iRating, carClassId, manufacturerBadge, classShortName, classColorHex, driver?.CarNumber ?? "");
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
        _driverCodesByCarIdx.Clear();
        _pitRoadEnteredUtcByCarIdx.Clear();
        _lastPitStatusByCarIdx.Clear();
        _lastP2PCountByCarIdx.Clear();
        _p2pChargingUntilByCarIdx.Clear();
        _lastP2PAnomalyLogByCarIdx.Clear();
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

    private void OnSessionInfo()
    {
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
            UpdateOnTrackState();
            // A full-field refresh coincides with the proximity refresh every third tick.  Keep
            // that immutable snapshot for the rest of the tick instead of scanning every CarIdx
            // a second time just to build Standings.
            LivePositions? positionsThisTick = null;

            _proximityTickCounter++;
            if (_proximityTickCounter >= ProximityTickInterval)
            {
                _proximityTickCounter = 0;
                var positions = ComputeLivePositions();
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
                UpdateStandings(positionsThisTick ?? ComputeLivePositions());
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

    // The two per-car P2P arrays are independently optional in iRacing.  Reading them in one
    // try block made a missing Count on an opponent hide an otherwise valid Status (and vice
    // versa).  Keep every usable part of the telemetry for every CarIdx, as Kapps does.
    private (bool? Active, int? Seconds, bool IsCharging) ReadP2P(int carIdx)
    {
        bool? active = null;
        int? seconds = null;
        var isPlayer = carIdx == _playerCarIdx;
        try { active = _sdk.Data.GetBool("CarIdxP2P_Status", carIdx); }
        catch { /* OTS status is not published for this car/session. */ }
        try
        {
            if (isPlayer)
            {
                // The driver's own row reads correctly as a genuine Int32, already on the real
                // 0..200 scale (a smooth 200 -> 196 -> 193 -> ... countdown) -- no scaling needed.
                var rawInt = _sdk.Data.GetInt("CarIdxP2P_Count", carIdx);
                seconds = rawInt is >= 0 && rawInt <= P2PMaxSeconds ? rawInt : null;
            }
            else
            {
                var raw = _sdk.Data.GetFloat("CarIdxP2P_Count", carIdx);
                LogP2PTrace(carIdx, active, raw);
                seconds = ReadP2PCount(raw);
                if (seconds is null) LogP2PAnomaly(carIdx, raw);
            }
        }
        catch { /* The count can be omitted independently of status. */ }
        var now = DateTime.UtcNow;
        if (seconds is int current)
        {
            if (active != true && _lastP2PCountByCarIdx.TryGetValue(carIdx, out var previous) && current > previous)
                _p2pChargingUntilByCarIdx[carIdx] = now.AddSeconds(1.5);
            _lastP2PCountByCarIdx[carIdx] = current;
        }
        // The remaining bank itself is the useful state for this system.  A non-active car below
        // the full 200 s bank is replenishing and must be yellow; a full inactive bank is available
        // and remains gray.  The short rising-edge window also covers a telemetry frame where the
        // bank crosses the full value.
        var charging = active == false && (seconds is int remaining && remaining < P2PMaxSeconds ||
            _p2pChargingUntilByCarIdx.TryGetValue(carIdx, out var until) && until > now);
        return (active, seconds, charging);
    }

    private void RefreshRaceSessionFlag()
    {
        try
        {
            var sessionInfo = _sdk.Data.SessionInfo;
            var currentSessionNum = sessionInfo?.SessionInfo?.CurrentSessionNum ?? -1;
            var session = sessionInfo?.SessionInfo?.Sessions?.FirstOrDefault(s => s.SessionNum == currentSessionNum);
            _isRaceSession = string.Equals(session?.SessionType, "Race", StringComparison.OrdinalIgnoreCase);
        }
        catch { _isRaceSession = false; }
    }

    private readonly record struct LivePositions(Dictionary<int, int> Overall, Dictionary<int, int> ByClass);

    // 16/09/2026: iRacing's own CarIdxPosition/CarIdxClassPosition are documented to lag behind
    // the real order on track, especially right after an overtake -- this is exactly why
    // Standings/Relative appeared to freeze mid-lap despite the SDK firing at 60Hz (confirmed via
    // the iRacing community's own investigation, e.g. niklam/iracedeck#233). Real-time overlays
    // instead derive position from the physical CarIdxLapCompleted+CarIdxLapDistPct -- more laps
    // completed ranks higher, ties broken by how far around the current lap. Computed once per
    // tick group and shared by every consumer below so they can never disagree with each other.
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
            if (!positions.Overall.TryGetValue(_playerCarIdx, out var myPosition)) return;
            var myEstTime = _sdk.Data.GetFloat("CarIdxEstTime", _playerCarIdx);

            var rows = new List<RelativeRow>();
            foreach (var (idx, position) in positions.Overall)
            {
                var offset = position - myPosition;
                if (Math.Abs(offset) > RelativeCarsBehind) continue;

                var theirEstTime = _sdk.Data.GetFloat("CarIdxEstTime", idx);
                // Simple same-lap gap estimate -- does not correct for a lap-count difference
                // between the two cars, a known, disclosed simplification for this first version
                // (see this task's own plan text / the spec's Phase 1 scope).
                double gap = theirEstTime - myEstTime;
                var tireCompound = _sdk.Data.GetInt("CarIdxTireCompound", idx);
                var (p2p, p2pSeconds, p2pCharging) = ReadP2P(idx);

                var identity = GetIdentity(idx);
                var classPosition = positions.ByClass.TryGetValue(idx, out var cp) ? cp : 0;

                var code = _driverCodesByCarIdx.TryGetValue(idx, out var driverCode) ? driverCode : "?";
                rows.Add(new RelativeRow(offset, code, idx == _playerCarIdx ? 0 : gap, tireCompound >= 0 ? tireCompound : null, p2p, p2pSeconds, p2pSeconds, p2pCharging, identity.FlagEmoji, identity.LicString, identity.LicColorHex, identity.IRating, identity.CarClassId, identity.ManufacturerBadge, idx == _playerCarIdx, classPosition, identity.ClassShortName, identity.ClassColorHex, identity.CarNumber));
            }

            FullRelativeUpdated?.Invoke(rows.OrderBy(row => row.PositionOffset).ToList());
        }
        catch
        {
            // Skip this tick -- same defensive posture as every other telemetry read in this class.
        }
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
                var tireCompound = _sdk.Data.GetInt("CarIdxTireCompound", idx);
                var (p2p, p2pSeconds, p2pCharging) = ReadP2P(idx);

                var code = _driverCodesByCarIdx.TryGetValue(idx, out var driverCode) ? driverCode : "?";
                var identity = GetIdentity(idx);
                rows.Add(new RelativeRow(position, code, null, tireCompound >= 0 ? tireCompound : null, p2p, p2pSeconds, p2pSeconds, p2pCharging, identity.FlagEmoji, identity.LicString, identity.LicColorHex, identity.IRating, identity.CarClassId, identity.ManufacturerBadge, CarNumber: identity.CarNumber));
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

    private void UpdateStandings(LivePositions positions)
    {
        try
        {
            var raw = new List<(int Position, string Code, int Laps, double? LastLap, int? Tire, bool IsPlayer,
                string Flag, string Lic, string? LicHex, int IRating, int ClassId, string Manufacturer, string CarNumber, double? Gap,
                string ClassShortName, string? ClassColorHex, int ClassPosition, bool? P2PActive, int? P2PUsesRemaining, double? P2PSecondsRemaining, bool P2PInCooldown, string PitStatus)>();
            var estimatedTimeByPosition = new Dictionary<int, double>();

            var playerLastLapRaw = _sdk.Data.GetFloat("LapLastLapTime");
            double? playerLastLap = playerLastLapRaw > 0 ? playerLastLapRaw : null;

            foreach (var (idx, position) in positions.Overall)
            {
                var lapsCompleted = _sdk.Data.GetInt("CarIdxLap", idx);
                var lastLap = _sdk.Data.GetFloat("CarIdxLastLapTime", idx);
                var tireCompound = _sdk.Data.GetInt("CarIdxTireCompound", idx);
                var code = _driverCodesByCarIdx.TryGetValue(idx, out var driverCode) ? driverCode : "?";
                var identity = GetIdentity(idx);

                double? gap = null;
                try
                {
                    // CarIdxF2Time: "race time behind leader or fastest lap time otherwise, s"
                    // (confirmed real -- sajax.github.io/irsdkdocs). The leader's own value is ~0
                    // and shown as "LEADER" by the view model instead of "+0.0".
                    var f2 = _sdk.Data.GetFloat("CarIdxF2Time", idx);
                    if (f2 >= 0) gap = f2;
                }
                catch { /* not published this session type -- leave gap null */ }
                try
                {
                    var estimatedTime = _sdk.Data.GetFloat("CarIdxEstTime", idx);
                    if (estimatedTime >= 0) estimatedTimeByPosition[position] = estimatedTime;
                }
                catch { /* optional live timing channel */ }

                var classPosition = positions.ByClass.TryGetValue(idx, out var cp) ? cp : 0;
                var onPitRoad = false;
                try { onPitRoad = _sdk.Data.GetBool("CarIdxOnPitRoad", idx); }
                catch { /* channel absent outside an active driving session */ }
                var pitStatus = UpdatePitStatus(idx, onPitRoad, lapsCompleted);
                var (p2pActive, p2pSeconds, p2pCharging) = ReadP2P(idx);

                raw.Add((position, code, lapsCompleted, lastLap > 0 ? lastLap : null, tireCompound >= 0 ? tireCompound : null,
                    idx == _playerCarIdx, identity.FlagEmoji, identity.LicString, identity.LicColorHex, identity.IRating,
                    identity.CarClassId, identity.ManufacturerBadge, identity.CarNumber, gap, identity.ClassShortName, identity.ClassColorHex, classPosition,
                    p2pActive, p2pSeconds, p2pSeconds, p2pCharging, pitStatus));
            }

            var ordered = raw.OrderBy(r => r.Position).ToList();

            // ΔiR*: NOT a real SDK field -- iRacing exposes no such projection. This is a rank-vs-
            // iRating approximation (real SoF formula above, applied to real per-driver iRatings),
            // shown only behind its own asterisk disclosure -- the driver explicitly chose an
            // approximate value over omitting the column entirely (14/09/2026).
            var classified = ordered.Where(r => r.IRating > 0).ToList();
            double? sof = null;
            if (classified.Count > 0)
            {
                var sumExp = classified.Sum(r => Math.Exp(-r.IRating / SofBr1));
                if (sumExp > 0) sof = SofBr1 * Math.Log(classified.Count / sumExp);
            }
            var expectedRankByPosition = classified
                .OrderByDescending(r => r.IRating)
                .Select((r, i) => (r.Position, Rank: i + 1))
                .ToDictionary(x => x.Position, x => x.Rank);

            var rows = new List<StandingsRow>();
            var leader = ordered.FirstOrDefault();
            for (var i = 0; i < ordered.Count; i++)
            {
                var r = ordered[i];
                double? deltaIR = null;
                if (sof is double sofValue && expectedRankByPosition.TryGetValue(r.Position, out var expectedRank))
                    deltaIR = Math.Round((expectedRank - r.Position) * sofValue / 500.0);

                double? lapDelta = r.LastLap is double own && playerLastLap is double mine ? own - mine : null;

                // CarIdxF2Time is authoritative across different laps, but it often advances only
                // at a timing line.  On the same lap, CarIdxEstTime gives the continuously moving
                // separation used by Relative, so the Standings Gap/Interval no longer freeze
                // until a lap is completed.
                var gapToLeader = r.Gap;
                if (r.Position != leader.Position && r.Laps == leader.Laps &&
                    estimatedTimeByPosition.TryGetValue(r.Position, out var currentEstimate) &&
                    estimatedTimeByPosition.TryGetValue(leader.Position, out var leaderEstimate))
                    gapToLeader = Math.Max(0, currentEstimate - leaderEstimate);

                // INTERVAL (gap to the car directly ahead, not the leader) -- derived from the same
                // real CarIdxF2Time values already used for GAP: the difference between two
                // consecutive cars' "time behind leader" is exactly their gap to each other. Null
                // for the leader (no car ahead) or whenever either car's own F2Time is unavailable.
                double? interval = i > 0 && r.Laps == ordered[i - 1].Laps &&
                    estimatedTimeByPosition.TryGetValue(r.Position, out var currentIntervalEstimate) &&
                    estimatedTimeByPosition.TryGetValue(ordered[i - 1].Position, out var aheadIntervalEstimate)
                    ? Math.Max(0, currentIntervalEstimate - aheadIntervalEstimate)
                    : i > 0 && r.Gap is double gapHere && ordered[i - 1].Gap is double gapAhead
                        ? gapHere - gapAhead
                        : null;

                rows.Add(new StandingsRow(r.Position, r.Code, r.Laps, r.LastLap, r.Tire, r.IsPlayer, r.Flag, r.Lic,
                    r.LicHex, r.IRating, r.ClassId, r.Manufacturer, gapToLeader, deltaIR, lapDelta, r.ClassShortName, r.ClassColorHex, r.ClassPosition, interval,
                    r.P2PActive, r.P2PUsesRemaining, r.P2PSecondsRemaining, r.P2PInCooldown, r.PitStatus, r.CarNumber));
            }

            StandingsUpdated?.Invoke(rows);
            SessionStatusUpdated?.Invoke(BuildSessionStatus(ordered.Count, sof));
        }
        catch
        {
            // Skip this tick.
        }
    }

    private string UpdatePitStatus(int carIdx, bool onPitRoad, int lap)
    {
        var now = DateTime.UtcNow;
        if (onPitRoad)
        {
            if (!_pitRoadEnteredUtcByCarIdx.TryGetValue(carIdx, out var entered))
            {
                entered = now;
                _pitRoadEnteredUtcByCarIdx[carIdx] = entered;
            }
            return $"PIT {(now - entered).TotalSeconds:0}s";
        }

        if (_pitRoadEnteredUtcByCarIdx.Remove(carIdx, out var pitEntry))
        {
            var duration = Math.Max(0, (now - pitEntry).TotalSeconds);
            _lastPitStatusByCarIdx[carIdx] = $"L{Math.Max(0, lap)} {duration:0}s";
        }
        return _lastPitStatusByCarIdx.TryGetValue(carIdx, out var last) ? last : "--";
    }

    // Header block shared by Standings and Relative -- class/session type/lap count/flag are all
    // real SDK fields; DriverCount/StrengthOfField come from the same full-field scan UpdateStandings
    // just did (cheap reuse rather than a second pass).
    private SessionStatus BuildSessionStatus(int driverCount, double? sof)
    {
        var carClassShortName = "";
        var sessionTypeText = "";
        int? currentLap = null;
        int? totalLaps = null;
        try
        {
            var sessionInfo = _sdk.Data.SessionInfo;
            var driver = sessionInfo?.DriverInfo?.Drivers?.FirstOrDefault(d => d.CarIdx == _playerCarIdx);
            carClassShortName = driver?.CarClassShortName?.ToUpperInvariant() ?? "";

            var currentSessionNum = sessionInfo?.SessionInfo?.CurrentSessionNum ?? -1;
            var session = sessionInfo?.SessionInfo?.Sessions?.FirstOrDefault(s => s.SessionNum == currentSessionNum);
            sessionTypeText = session?.SessionType?.ToUpperInvariant() ?? "";

            var lap = _sdk.Data.GetInt("Lap");
            if (lap > 0) currentLap = lap;
            if (session?.SessionLaps is string lapsText && int.TryParse(lapsText, out var parsedLaps)) totalLaps = parsedLaps;
        }
        catch { /* session info momentarily incomplete -- leave whatever was resolved */ }

        var (flagText, flagColorHex) = DecodeSessionFlag();
        return new SessionStatus(carClassShortName, sessionTypeText, currentLap, totalLaps, flagText, flagColorHex, sof, driverCount);
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
                rubberState = sessionInfo?.SessionInfo?.Sessions?.FirstOrDefault(s => s.SessionNum == currentSessionNum)?.SessionTrackRubberState;
            }
            catch { /* session info momentarily incomplete -- skip this tick's rubber read */ }

            double? trackTemp = null;
            try { var t = _sdk.Data.GetFloat("TrackTemp"); trackTemp = t; } catch { /* not published */ }

            var lastLap = _sdk.Data.GetFloat("LapLastLapTime");
            double? lastLapSeconds = lastLap > 0 ? lastLap : null;
            if (lastLap > 0 && (_bestLapTimeSeconds is not double best || lastLap < best))
                _bestLapTimeSeconds = lastLap;

            PlayerCarStatusUpdated?.Invoke(new PlayerCarStatus(brakeBias, rubberState, _bestLapTimeSeconds, lastLapSeconds, trackTemp));
        }
        catch
        {
            // Skip this tick.
        }
    }

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

    // 13/09/2026: a rolling average over the last FuelWindowSize completed laps, not the SDK's own
    // instantaneous FuelUsePerHour -- an instantaneous rate swings with throttle/braking on any
    // single sample, while the rolling average is what every established fuel calculator actually
    // uses for a stable "laps remaining" estimate. LapCompleted (not Lap) is the correct edge to
    // watch: it increments exactly once per finished lap, where Lap reports the currently-STARTED
    // lap and would double-count the boundary tick (see LapCompleted's own confirmed SDK doc).
    //
    // 14/09/2026 fix: "1.37/volta" vs the car's real ~2.7 L/lap (McLaren, Road Atlanta) -- the bug
    // was using the fuel level captured at APP ATTACH time as if it were a lap-start sample. If the
    // app attaches mid-lap (the normal case -- the driver is already out when they open the app),
    // that first "previousFuel" is really "fuel at some random mid-lap point", so the first delta
    // measures only a fraction of a lap's burn, not a full lap's. _lastFuelLevel is now only ever
    // set at a CONFIRMED LapCompleted edge (a real start/finish crossing), and the very first such
    // edge only establishes that baseline -- no "used" sample is recorded until the SECOND crossing,
    // once both ends of the delta are real S/F-line boundaries.
    private void UpdateFuel()
    {
        try
        {
            var fuelLevel = _sdk.Data.GetFloat("FuelLevel");
            var fuelUsePerHour = _sdk.Data.GetFloat("FuelUsePerHour");
            var lapCompleted = _sdk.Data.GetInt("LapCompleted");

            if (_lastLapCompleted < 0)
            {
                // First tick ever seen -- we don't know whether "now" lands on a lap boundary, so
                // no fuel baseline is established here (see fix note above). Only LapCompleted is
                // tracked, so the next real crossing can be detected.
                _lastLapCompleted = lapCompleted;
            }
            else if (lapCompleted < _lastLapCompleted)
            {
                // Session segment changed (practice -> qualy -> race), or the session was reset --
                // LapCompleted restarts at 0, so the prior segment's samples no longer describe
                // this stint. Clear the rolling windows so the estimate starts fresh rather than
                // silently freezing on stale samples from a different session segment. The fuel
                // baseline is also invalidated -- the next crossing in the new segment establishes
                // a fresh one, same as at app attach.
                _fuelPerLapWindow.Clear();
                _lapTimeWindow.Clear();
                _lastLapCompleted = lapCompleted;
                _lastFuelLevel = null;
            }
            else if (lapCompleted > _lastLapCompleted)
            {
                if (_lastFuelLevel is double previousFuel)
                {
                    var used = previousFuel - fuelLevel;
                    // Only a positive, plausible consumption sample is trusted -- a pit stop refuel
                    // between ticks would otherwise register as a large negative "used" value and
                    // corrupt the rolling average with a nonsense sample.
                    if (used > 0)
                    {
                        _fuelPerLapWindow.Enqueue(used);
                        if (_fuelPerLapWindow.Count > FuelWindowSize) _fuelPerLapWindow.Dequeue();

                        var lapTime = _sdk.Data.GetFloat("LapLastLapTime");
                        if (lapTime > 0)
                        {
                            _lapTimeWindow.Enqueue(lapTime);
                            if (_lapTimeWindow.Count > FuelWindowSize) _lapTimeWindow.Dequeue();
                        }
                    }
                }
                // This crossing is a real start/finish-line boundary regardless of whether a
                // baseline existed before it -- always safe to use as the baseline for the NEXT lap.
                _lastLapCompleted = lapCompleted;
                _lastFuelLevel = fuelLevel;
            }

            double? avgFuelPerLap = _fuelPerLapWindow.Count > 0 ? _fuelPerLapWindow.Average() : null;
            double? avgLapTime = _lapTimeWindow.Count > 0 ? _lapTimeWindow.Average() : null;
            double? lapsRemaining = avgFuelPerLap is double perLap && perLap > 0 ? fuelLevel / perLap : null;
            double? timeRemaining = lapsRemaining is double laps && avgLapTime is double lapTime2 ? laps * lapTime2 : null;

            double? refuelToFull = null;
            double? plannedPitFuel = null;
            double? fuelNeededForFinish = null;
            double? fuelBurnToFinish = null;
            try
            {
                var fuelPct = _sdk.Data.GetFloat("FuelLevelPct");
                if (fuelPct is > 0.001f and <= 1.0f)
                    refuelToFull = Math.Max(0, fuelLevel / fuelPct - fuelLevel);
            }
            catch { /* optional channel; calculators remain useful without tank capacity */ }
            try
            {
                // PitSvFuel is the live amount currently selected in iRacing's Black Box, in L.
                // Kapps subscribes to this exact channel for its fuel calculator.
                var blackBoxFuel = _sdk.Data.GetFloat("PitSvFuel");
                if (blackBoxFuel >= 0) plannedPitFuel = blackBoxFuel;
            }
            catch { /* no pit service field in this session */ }
            try
            {
                var sessionInfo = _sdk.Data.SessionInfo;
                var currentSessionNum = sessionInfo?.SessionInfo?.CurrentSessionNum ?? -1;
                var session = sessionInfo?.SessionInfo?.Sessions?.FirstOrDefault(s => s.SessionNum == currentSessionNum);
                var lap = _sdk.Data.GetInt("LapCompleted");
                var isRace = string.Equals(session?.SessionType, "Race", StringComparison.OrdinalIgnoreCase);
                if (avgFuelPerLap is double lapFuel && lapFuel > 0 && isRace && int.TryParse(session?.SessionLaps, out var totalLaps) && totalLaps > 0)
                {
                    // Lap-limited race: SessionLaps is authoritative.  LapCompleted is the number
                    // already crossed, therefore the current in-progress lap remains in the burn.
                    fuelBurnToFinish = Math.Max(0, (totalLaps - Math.Max(0, lap)) * lapFuel);
                    fuelNeededForFinish = Math.Max(0, fuelBurnToFinish.Value - fuelLevel);
                }
                else if (avgFuelPerLap is double timedLapFuel && timedLapFuel > 0 && avgLapTime is double timedLapSeconds && timedLapSeconds > 0 && isRace)
                {
                    // Timed races expose no useful SessionLaps.  Estimate the remaining fuel from
                    // the live SDK clock instead of leaving a stale/full-tank recommendation.
                    // The small partial-lap fraction is intentionally retained: fuel is consumed
                    // during the lap that will be completed after the timer reaches zero.
                    var sessionSecondsRemaining = _sdk.Data.GetFloat("SessionTimeRemain");
                    if (sessionSecondsRemaining >= 0)
                    {
                        var lapsToFinish = sessionSecondsRemaining / timedLapSeconds;
                        fuelBurnToFinish = Math.Max(0, lapsToFinish * timedLapFuel);
                        fuelNeededForFinish = Math.Max(0, fuelBurnToFinish.Value - fuelLevel);
                    }
                }
            }
            catch { /* time-limited sessions do not have a fixed lap target */ }
            double? fuelAfterPit = plannedPitFuel is double planned ? fuelLevel + planned : null;
            // "Fuel at end" is based on the actual amount currently selected in the Black Box,
            // then subtracts the projected race burn.  It is not merely tank level after pitting.
            double? fuelAtFinish = fuelAfterPit is double afterPit && fuelBurnToFinish is double burn ? Math.Max(0, afterPit - burn) : null;
            FuelUpdated?.Invoke(new FuelStatus(fuelLevel, fuelUsePerHour, avgFuelPerLap, lapsRemaining, timeRemaining, refuelToFull, fuelNeededForFinish, plannedPitFuel, fuelAfterPit, fuelAtFinish));
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
                rubberState = sessionInfo?.SessionInfo?.Sessions?.FirstOrDefault(s => s.SessionNum == currentSessionNum)?.SessionTrackRubberState;
            }
            catch { /* optional session metadata */ }
            WeatherUpdated?.Invoke(new WeatherStatus(airTemp, trackTemp, precipitation, trackWetness, declaredWet, rubberState, positions, windSpeed, windDirectionDeg));
        }
        catch
        {
            // Skip this tick.
        }
    }

    private void UpdateRadar()
    {
        try
        {
            var leftRight = _sdk.Data.GetInt("CarLeftRight");
            // irsdk_CarLeftRight: 0=Off, 1=Clear, 2=CarLeft, 3=CarRight, 4=CarLeftRight, 5=2CarsLeft, 6=2CarsRight.
            var blindLeft = leftRight is 2 or 4 or 5;
            var blindRight = leftRight is 3 or 4 or 6;

            var blips = new List<RadarBlip>();
            var hasTrackLength = _trackLengthMeters is double trackLength0 && trackLength0 > 0;
            // Do not turn a general "nearby cars" list into a side-by-side warning.  CarLeftRight
            // is the exact iRacing SDK condition for that UI.  Avoiding the field scan when clear
            // also keeps the 60 Hz path extremely cheap for the normal case.
            if (hasTrackLength && (blindLeft || blindRight))
            {
                var trackLength = _trackLengthMeters!.Value;
                var myDistPct = _sdk.Data.GetFloat("CarIdxLapDistPct", _playerCarIdx);
                var maxCars = IRacingSdkConst.MaxNumCars;
                for (var idx = 0; idx < maxCars; idx++)
                {
                    if (idx == _playerCarIdx) continue;
                    var theirDistPct = _sdk.Data.GetFloat("CarIdxLapDistPct", idx);
                    if (theirDistPct < 0) continue; // car not currently on track / not in this session
                    if (_sdk.Data.GetBool("CarIdxOnPitRoad", idx)) continue; // pit-lane cars aren't a real proximity signal

                    // Shortest signed distance around the lap, wrapping at the start/finish line so
                    // a car just ahead across the line doesn't register as almost a full lap behind.
                    double delta = theirDistPct - myDistPct;
                    if (delta > 0.5) delta -= 1.0;
                    if (delta < -0.5) delta += 1.0;

                    var distanceMeters = delta * trackLength;
                    if (Math.Abs(distanceMeters) > RadarMaxRangeMeters) continue;

                    var code = _driverCodesByCarIdx.TryGetValue(idx, out var driverCode) ? driverCode : "?";
                    blips.Add(new RadarBlip(distanceMeters, code));
                }
            }

            RadarUpdated?.Invoke(new RadarStatus(blindLeft, blindRight, blips, hasTrackLength));
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
