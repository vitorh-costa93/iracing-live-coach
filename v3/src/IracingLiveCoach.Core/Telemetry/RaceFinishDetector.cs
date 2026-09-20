namespace IracingLiveCoach.Core.Telemetry;

/// <summary>The player's classification right after taking the chequered flag.</summary>
public sealed record RaceFinish(int OverallPosition, int ClassPosition);

/// <summary>
/// Detects "the player just took the chequered flag in a race" from per-tick telemetry, without
/// touching the SDK (pure and unit-tested). The global chequered bit only says the LEADER finished;
/// the player's own finish is the first lap completed after that flag. The lap count seen just
/// BEFORE the flag is the baseline, so the winner (whose lap ticks over in the same frame the flag
/// appears) is still recognised. The result is reported once, after a short settle delay because
/// iRacing's position channels lag a moment behind the line.
/// </summary>
public sealed class RaceFinishDetector
{
    public static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(2);

    private int _lastLaps = -1;
    private int? _baselineLaps;
    private DateTime? _finishedAt;
    private bool _reported;

    /// <summary>Feeds one tick. Returns true exactly once per race, when the finish has settled and
    /// the caller should read the player's final positions.</summary>
    public bool Update(bool isRace, bool chequeredOut, int playerLapsCompleted, DateTime now)
    {
        if (!isRace || !chequeredOut)
        {
            _baselineLaps = null;
            _finishedAt = null;
            _reported = false;
            _lastLaps = playerLapsCompleted;
            return false;
        }

        _baselineLaps ??= _lastLaps >= 0 ? _lastLaps : playerLapsCompleted;
        _lastLaps = playerLapsCompleted;

        if (_finishedAt is null && playerLapsCompleted > _baselineLaps) _finishedAt = now;
        if (_finishedAt is { } at && !_reported && now - at >= SettleDelay)
        {
            _reported = true;
            return true;
        }
        return false;
    }

    public void Reset()
    {
        _lastLaps = -1;
        _baselineLaps = null;
        _finishedAt = null;
        _reported = false;
    }
}

/// <summary>Which finishing position counts as "winning" for the victory theme.</summary>
public enum VictoryRule { ClassWin, OverallWin }

/// <summary>Victory-theme settings (persisted in the profile). <see cref="FilePath"/> points at a
/// user-supplied audio file kept on their machine; nothing is bundled.</summary>
public sealed record VictoryConfig(bool Enabled, string FilePath, int VolumePct = 70, VictoryRule Rule = VictoryRule.ClassWin)
{
    public static VictoryConfig Default { get; } = new(false, "");

    public bool IsWin(RaceFinish finish) => Rule == VictoryRule.OverallWin
        ? finish.OverallPosition == 1
        : finish.ClassPosition == 1 || (finish.ClassPosition <= 0 && finish.OverallPosition == 1);
}
