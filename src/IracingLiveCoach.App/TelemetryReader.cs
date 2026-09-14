using System;
using System.Collections.Generic;
using System.Linq;
using IRSDKSharper;
using IracingLiveCoach.Core;

namespace IracingLiveCoach.App;

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
public record RelativeRow(int PositionOffset, string DriverCode, double? GapSeconds, int? TireCompound, bool? P2PActive, int? P2PUsesRemaining, double? P2PSecondsRemaining, bool P2PInCooldown, string FlagEmoji, string LicString, string? LicColorHex, int IRating, int CarClassId, string ManufacturerBadge, bool IsPlayer = false);

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
public record StandingsRow(int Position, string DriverCode, int LapsCompleted, double? LastLapTime, int? TireCompound, bool IsPlayer, string FlagEmoji, string LicString, string? LicColorHex, int IRating, int CarClassId, string ManufacturerBadge, double? GapToLeaderSeconds, double? EstimatedDeltaIRating, double? LapDeltaVsPlayerSeconds);

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
public record FuelStatus(double FuelLevelLiters, double FuelUsePerHourLiters, double? AverageFuelPerLapLiters, double? LapsRemaining, double? TimeRemainingSeconds);

/// <summary>One car's current position around the lap (0.0 at start/finish, approaching 1.0 as it
/// completes the lap) -- feeds the Weather widget's linear "track usage" bar. Deliberately NOT a
/// real track shape (see the spec's own Phase 2 section for why).</summary>
public record TrackPositionDot(string DriverCode, double LapDistPct, bool IsPlayer);

/// <summary>One (throttled, ~10Hz) weather/track-usage snapshot.</summary>
public record WeatherStatus(double AirTempC, double TrackTempC, double PrecipitationPct, int TrackWetness, bool WeatherDeclaredWet, List<TrackPositionDot> CarPositions);

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
public record RaceStartStatus(double ClutchPct, double ThrottlePct, bool ShouldShow);

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

    private readonly IRacingSdk _sdk = new();
    private LiveCoachEngine? _engine;
    private bool _sessionDetected;
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

    private const int RadarTickInterval = 6;
    private int _radarTickCounter;
    private const double RadarMaxRangeMeters = 100.0;

    // Reading every SDK channel is inexpensive; rebuilding WPF item collections is not.  The
    // simulator can publish at 60 Hz while a wet/new circuit is already CPU-bound, so the live
    // widgets deliberately run on two small budgets: 10 Hz for proximity information and 2 Hz
    // for the full-field cards.  The driving-coach engine below remains on every SDK tick.
    private const int ProximityTickInterval = 6;
    private const int FullFieldTickInterval = 30;
    private int _proximityTickCounter;
    private int _fullFieldTickCounter;

    // 14/09/2026: SF23's Overtake System rules, publicly documented on iRacing's own car page --
    // 20s of activation per use, at least 100s cooldown ("ReTime") afterward. This is car-specific
    // domain knowledge (the same kind the sibling iracing-analytics project already hardcodes for
    // ARB/differential/spring targets per car architecture), not a telemetry read -- combined with
    // the REAL CarIdxP2P_Status transition below to compute an actual countdown. Disclosed caveat:
    // these constants are SF23-specific and could be wrong for a different P2P-enabled car this
    // app hasn't researched, or if iRacing rebalances SF23's own system in a future season -- the
    // underlying CarIdxP2P_Status/CarIdxP2P_Count are always real regardless of whether the
    // countdown numbers happen to be exactly right for the car actually being driven.
    private const double OtsActiveSeconds = 20.0;
    private const double OtsCooldownSeconds = 100.0;

    // Keyed by CarIdx so each car's own activation/cooldown phase is timed independently.
    // _p2pPhaseEndUtcByCarIdx holds the UTC instant the CURRENT phase (active or cooldown) ends.
    private readonly Dictionary<int, bool> _lastP2PActiveByCarIdx = new();
    private readonly Dictionary<int, DateTime> _p2pPhaseEndUtcByCarIdx = new();

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
            var code = FormatDriverName(driver.UserName, driver.CarNumber);
            map[driver.CarIdx] = code;
        }
        return map;
    }

    private static string FormatDriverName(string? userName, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(userName)) return fallback ?? "?";
        var parts = userName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1) return parts[0].ToUpperInvariant();
        return $"{char.ToUpperInvariant(parts[0][0])}. {parts[^1].ToUpperInvariant()}";
    }

    private (string FlagEmoji, string LicString, string? LicColorHex, int IRating, int CarClassId, string ManufacturerBadge) GetIdentity(int carIdx)
    {
        var sessionInfo = _sdk.Data.SessionInfo;
        var driver = sessionInfo?.DriverInfo?.Drivers?.FirstOrDefault(d => d.CarIdx == carIdx);
        var flagEmoji = CountryFlags.ToEmoji(driver?.FlairName);
        var licString = driver?.LicString ?? "--";
        var licColorHex = driver?.LicColor; // "String" per the real IRSDKSharper 1.3.0 type -- parsed defensively by the view model, not here.
        var iRating = driver?.IRating ?? 0;
        var carClassId = driver?.CarClassID ?? 0;
        var manufacturerBadge = ExtractManufacturer(driver?.CarScreenName);
        return (flagEmoji, licString, licColorHex, iRating, carClassId, manufacturerBadge);
    }

    // 14/09/2026: "team logo" -- iRacing has no real "team" concept for pickup/public racing and
    // no logo asset for anything via the SDK, but CarScreenName (confirmed real, e.g. "Ferrari 296
    // GT3 EVO") DOES let us derive the car's own manufacturer, which is what every competing
    // overlay's "team badge" actually shows in practice. iRacing's own car names consistently
    // follow a "<Manufacturer> <Model>" convention, so the first whitespace-delimited token is the
    // manufacturer in the overwhelming majority of cases -- a plain text badge (e.g. "FERRARI"),
    // not an image logo (bundling real trademarked logo graphics is a materially larger, separate
    // effort with its own legal/asset-sourcing considerations -- see this plan's own spec section).
    private static string ExtractManufacturer(string? carScreenName)
    {
        if (string.IsNullOrWhiteSpace(carScreenName)) return "";
        var firstToken = carScreenName.Split(' ', 2)[0];
        return firstToken.ToUpperInvariant();
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
    /// once the session is detected -- true only while PlayerTrackSurface reports OnTrack (not in
    /// the pits, not approaching the pits, not off-track/in-world-but-parked). Drives whether the
    /// whole widget suite is shown at all (see MainWindow's own subscription).</summary>
    public event Action<bool>? OnTrackStateChanged;

    /// <summary>Fires every telemetry tick once the session is detected, with the player's own
    /// clutch/throttle pedal position and whether the Race Start Helper should currently be shown
    /// (Race session + car essentially stationary). See RaceStartStatus's own doc comment.</summary>
    public event Action<RaceStartStatus>? RaceStartUpdated;

    public TelemetryReader()
    {
        _sdk.OnSessionInfo += OnSessionInfo;
        _sdk.OnTelemetryData += OnTelemetryData;
    }

    public void Start() => _sdk.Start();

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
        if (_sessionDetected) return;

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
            _driverCodesByCarIdx = BuildDriverCodes(sessionInfo);
            _sessionDetected = true;
            SessionDetected?.Invoke(carId, trackId);
        }
        catch
        {
            // Wait for the next OnSessionInfo update.
        }
    }

    private void OnTelemetryData()
    {
        // Relative/P2P doesn't depend on a baseline (there's no "history" for it), so it's read
        // regardless of whether a LiveCoachEngine has been attached yet -- only the corner-coaching
        // half below needs that.
        if (_playerCarIdx >= 0)
        {
            UpdateOnTrackState();

            _proximityTickCounter++;
            if (_proximityTickCounter >= ProximityTickInterval)
            {
                _proximityTickCounter = 0;
                UpdateRelative();
                UpdateFullRelative();
                UpdateSecondaryRelative();
                UpdatePlayerCarStatus();
                UpdateRaceStart();
            }

            _fullFieldTickCounter++;
            if (_fullFieldTickCounter >= FullFieldTickInterval)
            {
                _fullFieldTickCounter = 0;
                UpdateStandings();
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

    private void UpdateRelative()
    {
        try
        {
            var myPosition = _sdk.Data.GetInt("CarIdxPosition", _playerCarIdx);
            if (myPosition <= 0) return; // not yet classified (formation, out-lap before scoring starts, etc.)

            var maxCars = IRacingSdkConst.MaxNumCars;
            var byOffset = new Dictionary<int, bool>();
            for (var idx = 0; idx < maxCars; idx++)
            {
                var position = _sdk.Data.GetInt("CarIdxPosition", idx);
                if (position <= myPosition) continue;
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

    // Returns (SecondsRemaining, InCooldown) for the given car's P2P phase, given its current
    // active/inactive telemetry state -- see OtsActiveSeconds/OtsCooldownSeconds's own doc comment
    // for the SF23-sourced constants this is built from.
    private (double? SecondsRemaining, bool InCooldown) UpdateP2PPhase(int idx, bool active)
    {
        var wasActive = _lastP2PActiveByCarIdx.TryGetValue(idx, out var previouslyActive) && previouslyActive;
        if (active && !wasActive)
        {
            // Activation just started -- the active window ends OtsActiveSeconds from now.
            _p2pPhaseEndUtcByCarIdx[idx] = DateTime.UtcNow.AddSeconds(OtsActiveSeconds);
        }
        else if (!active && wasActive)
        {
            // Deactivation just happened (driver released it early, or it auto-expired) -- the
            // cooldown window starts now and ends OtsCooldownSeconds later.
            _p2pPhaseEndUtcByCarIdx[idx] = DateTime.UtcNow.AddSeconds(OtsCooldownSeconds);
        }
        _lastP2PActiveByCarIdx[idx] = active;

        if (!_p2pPhaseEndUtcByCarIdx.TryGetValue(idx, out var phaseEnd)) return (null, false);
        var remaining = (phaseEnd - DateTime.UtcNow).TotalSeconds;
        if (remaining <= 0) return (null, false); // phase already elapsed -- PRONTO, no countdown to show
        return (remaining, !active); // still counting down: if not currently active, this is the cooldown countdown
    }

    // 13/09/2026: full running-order relative (F1-style widget), extending 3-ahead/3-behind --
    // reads the SAME CarIdxPosition scan as UpdateRelative but is a SEPARATE pass (not merged into
    // it) so a change here can never affect the already-shipped P2P strip's own behavior.
    private void UpdateFullRelative()
    {
        try
        {
            var myPosition = _sdk.Data.GetInt("CarIdxPosition", _playerCarIdx);
            if (myPosition <= 0) return;
            var myEstTime = _sdk.Data.GetFloat("CarIdxEstTime", _playerCarIdx);

            var maxCars = IRacingSdkConst.MaxNumCars;
            var rows = new List<RelativeRow>();
            for (var idx = 0; idx < maxCars; idx++)
            {
                if (idx == _playerCarIdx) continue;
                var position = _sdk.Data.GetInt("CarIdxPosition", idx);
                if (position <= 0) continue;
                var offset = position - myPosition;
                if (Math.Abs(offset) > RelativeCarsBehind) continue;

                var theirEstTime = _sdk.Data.GetFloat("CarIdxEstTime", idx);
                // Simple same-lap gap estimate -- does not correct for a lap-count difference
                // between the two cars, a known, disclosed simplification for this first version
                // (see this task's own plan text / the spec's Phase 1 scope).
                double gap = theirEstTime - myEstTime;
                var tireCompound = _sdk.Data.GetInt("CarIdxTireCompound", idx);
                bool? p2p = null;
                try { p2p = _sdk.Data.GetBool("CarIdxP2P_Status", idx); } catch { /* no P2P this session */ }

                int? p2pUses = null;
                double? p2pSecondsRemaining = null;
                var p2pInCooldown = false;
                if (p2p is bool active)
                {
                    p2pUses = _sdk.Data.GetInt("CarIdxP2P_Count", idx);
                    (p2pSecondsRemaining, p2pInCooldown) = UpdateP2PPhase(idx, active);
                }

                var identity = GetIdentity(idx);

                var code = _driverCodesByCarIdx.TryGetValue(idx, out var driverCode) ? driverCode : "?";
                rows.Add(new RelativeRow(position, code, idx == _playerCarIdx ? 0 : gap, tireCompound >= 0 ? tireCompound : null, p2p, p2pUses, p2pSecondsRemaining, p2pInCooldown, identity.FlagEmoji, identity.LicString, identity.LicColorHex, identity.IRating, identity.CarClassId, identity.ManufacturerBadge, idx == _playerCarIdx));
            }

            FullRelativeUpdated?.Invoke(rows.OrderBy(row => row.PositionOffset).ToList());
        }
        catch
        {
            // Skip this tick -- same defensive posture as every other telemetry read in this class.
        }
    }

    // 14/09/2026: mirrors UpdateFullRelative's shape but ranks by CarIdxClassPosition within the
    // first car class found that differs from the player's own CarIdxClass, for the second,
    // auto-configuring Relative widget instance (multiclass sessions only -- see this plan's own
    // Global Constraints for why there's no manual class picker).
    private void UpdateSecondaryRelative()
    {
        try
        {
            var myClass = _sdk.Data.GetInt("CarIdxClass", _playerCarIdx);
            var maxCars = IRacingSdkConst.MaxNumCars;

            int? secondaryClass = null;
            for (var idx = 0; idx < maxCars; idx++)
            {
                if (idx == _playerCarIdx) continue;
                var classId = _sdk.Data.GetInt("CarIdxClass", idx);
                var classPosition = _sdk.Data.GetInt("CarIdxClassPosition", idx);
                if (classPosition <= 0 || classId == myClass) continue;
                secondaryClass = classId;
                break; // first differing class found -- deterministic since CarIdx order is stable within a session
            }
            if (secondaryClass is not int targetClass) return; // single-class session -- don't fire

            var byOffset = new List<(int Position, int Idx)>();
            for (var idx = 0; idx < maxCars; idx++)
            {
                if (_sdk.Data.GetInt("CarIdxClass", idx) != targetClass) continue;
                var classPosition = _sdk.Data.GetInt("CarIdxClassPosition", idx);
                if (classPosition <= 0) continue;
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
                bool? p2p = null;
                int? p2pUses = null;
                double? p2pSecondsRemaining = null;
                var p2pInCooldown = false;
                try
                {
                    var active = _sdk.Data.GetBool("CarIdxP2P_Status", idx);
                    p2p = active;
                    p2pUses = _sdk.Data.GetInt("CarIdxP2P_Count", idx);
                    (p2pSecondsRemaining, p2pInCooldown) = UpdateP2PPhase(idx, active);
                }
                catch { /* no P2P for this class */ }

                var code = _driverCodesByCarIdx.TryGetValue(idx, out var driverCode) ? driverCode : "?";
                var identity = GetIdentity(idx);
                rows.Add(new RelativeRow(position, code, null, tireCompound >= 0 ? tireCompound : null, p2p, p2pUses, p2pSecondsRemaining, p2pInCooldown, identity.FlagEmoji, identity.LicString, identity.LicColorHex, identity.IRating, identity.CarClassId, identity.ManufacturerBadge));
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

    private void UpdateStandings()
    {
        try
        {
            var maxCars = IRacingSdkConst.MaxNumCars;
            var raw = new List<(int Position, string Code, int Laps, double? LastLap, int? Tire, bool IsPlayer,
                string Flag, string Lic, string? LicHex, int IRating, int ClassId, string Manufacturer, double? Gap)>();

            var playerLastLapRaw = _sdk.Data.GetFloat("LapLastLapTime");
            double? playerLastLap = playerLastLapRaw > 0 ? playerLastLapRaw : null;

            for (var idx = 0; idx < maxCars; idx++)
            {
                var position = _sdk.Data.GetInt("CarIdxPosition", idx);
                if (position <= 0) continue; // not currently classified

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

                raw.Add((position, code, lapsCompleted, lastLap > 0 ? lastLap : null, tireCompound >= 0 ? tireCompound : null,
                    idx == _playerCarIdx, identity.FlagEmoji, identity.LicString, identity.LicColorHex, identity.IRating,
                    identity.CarClassId, identity.ManufacturerBadge, gap));
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
            foreach (var r in ordered)
            {
                double? deltaIR = null;
                if (sof is double sofValue && expectedRankByPosition.TryGetValue(r.Position, out var expectedRank))
                    deltaIR = Math.Round((expectedRank - r.Position) * sofValue / 500.0);

                double? lapDelta = r.LastLap is double own && playerLastLap is double mine ? own - mine : null;

                rows.Add(new StandingsRow(r.Position, r.Code, r.Laps, r.LastLap, r.Tire, r.IsPlayer, r.Flag, r.Lic,
                    r.LicHex, r.IRating, r.ClassId, r.Manufacturer, r.Gap, deltaIR, lapDelta));
            }

            StandingsUpdated?.Invoke(rows);
            SessionStatusUpdated?.Invoke(BuildSessionStatus(ordered.Count, sof));
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
            var isOnTrack = surface == (int)IRacingSdkEnum.TrkLoc.OnTrack;
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
            var sessionInfo = _sdk.Data.SessionInfo;
            var currentSessionNum = sessionInfo?.SessionInfo?.CurrentSessionNum ?? -1;
            var sessionType = sessionInfo?.SessionInfo?.Sessions?.FirstOrDefault(s => s.SessionNum == currentSessionNum)?.SessionType;
            var isRace = string.Equals(sessionType, "Race", StringComparison.OrdinalIgnoreCase);

            var speed = _sdk.Data.GetFloat("Speed");
            var clutch = _sdk.Data.GetFloat("Clutch");
            var throttle = _sdk.Data.GetFloat("Throttle");

            var shouldShow = isRace && Math.Abs(speed) < StationarySpeedThreshold;
            RaceStartUpdated?.Invoke(new RaceStartStatus(clutch * 100.0, throttle * 100.0, shouldShow));
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

            FuelUpdated?.Invoke(new FuelStatus(fuelLevel, fuelUsePerHour, avgFuelPerLap, lapsRemaining, timeRemaining));
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

            var maxCars = IRacingSdkConst.MaxNumCars;
            var positions = new List<TrackPositionDot>();
            for (var idx = 0; idx < maxCars; idx++)
            {
                var lapDistPct = _sdk.Data.GetFloat("CarIdxLapDistPct", idx);
                if (lapDistPct < 0) continue; // car not currently on track / not in this session
                var code = _driverCodesByCarIdx.TryGetValue(idx, out var driverCode) ? driverCode : "?";
                positions.Add(new TrackPositionDot(code, lapDistPct, idx == _playerCarIdx));
            }

            WeatherUpdated?.Invoke(new WeatherStatus(airTemp, trackTemp, precipitation, trackWetness, declaredWet, positions));
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
            if (hasTrackLength)
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
        _sdk.Stop();
    }
}
