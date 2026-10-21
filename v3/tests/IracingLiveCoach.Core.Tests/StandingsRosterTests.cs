using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.Core.Tests;

public sealed class StandingsRosterTests
{
    private static readonly RatingEventKey Event = RatingEventKey.From(100, 5000, 7);

    [Fact]
    public void Practice_includes_all_entrants_without_laps_and_retains_identity_when_they_leave()
    {
        var roster = new StandingsRoster<string>();
        roster.Enter(Event, 0);
        roster.Observe([new(1, 10, "Driver One"), new(2, 10, "Driver Two"), new(3, 20, "Driver Three"),
            new(4, 10, "Pace", PaceCar: true), new(5, 10, "Spectator", Spectator: true)]);
        var (before, _) = roster.TimedPositions();
        Assert.Equal(3, before.Count);
        roster.Observe([new(1, 10, "Driver One")]);
        var (after, byClass) = roster.TimedPositions();
        Assert.Equal(before, after);
        Assert.True(roster.TryGetDriver(2, out var retained));
        Assert.Equal("Driver Two", retained.Identity);
        Assert.Equal(2, byClass[2]);
        var counts = ClassDriverCounts.Compute(roster.Drivers.Select(d => (d.CarIdx, d.ClassId)), []);
        Assert.Equal(new ClassDriverCount(2, 0), counts[10]);
        Assert.Equal(new ClassDriverCount(1, 0), counts[20]);
    }

    [Fact]
    public void Absent_driver_keeps_best_last_and_laps_and_new_faster_time_passes_them()
    {
        var roster = new StandingsRoster<string>();
        roster.Enter(Event, 0);
        roster.Observe([new(1, 10, "One"), new(2, 10, "Two"), new(3, 20, "Three")]);
        roster.ObserveTiming(1, 1, 90, 91, 4);
        roster.ObserveTiming(2, 2, 91, 93, 3);
        roster.Observe([new(2, 10, "Two")]);
        roster.ObserveTiming(1, 0, -1, -1, -1);
        roster.ObserveTiming(2, 1, 89, 89, 4);
        var (overall, byClass) = roster.TimedPositions();
        Assert.Equal(1, overall[2]);
        Assert.Equal(2, overall[1]);
        Assert.Equal(3, overall[3]); // entrant without a lap stays after all timed drivers
        Assert.Equal(2, byClass[1]);
        Assert.Equal(new RosterTiming(1, 90, 91, 4), roster.Timing(1));
        var counts = ClassDriverCounts.Compute(roster.Drivers.Select(d => (d.CarIdx, d.ClassId)),
            roster.Drivers.Where(d => roster.Timing(d.CarIdx).BestLap is > 0).Select(d => d.CarIdx));
        Assert.Equal(new ClassDriverCount(2, 2), counts[10]);
    }

    [Fact]
    public void Qualifying_session_clears_practice_times_but_same_event_reconnection_keeps_them()
    {
        var roster = new StandingsRoster<string>();
        roster.Enter(Event, 0);
        roster.Observe([new(1, 10, "One"), new(2, 10, "Two")]);
        roster.ObserveTiming(1, 1, 90, 90, 5);
        roster.Enter(Event, 1);
        Assert.Equal(2, roster.Count);
        Assert.Null(roster.Timing(1).BestLap);
        roster.ObserveTiming(2, 1, 89, 89, 1);
        roster.Enter(RatingEventKey.From(100, 5000, 99), 1);
        roster.Observe([]);
        Assert.Equal(89, roster.Timing(2).BestLap);
        Assert.Equal(1, roster.TimedPositions().Overall[2]);
    }

    [Fact]
    public void New_event_clears_identities_and_results_even_when_car_indices_are_reused()
    {
        var roster = new StandingsRoster<string>();
        roster.Enter(Event, 0);
        roster.Observe([new(1, 10, "Previous One"), new(2, 10, "Previous Two")]);
        roster.ObserveTiming(1, 1, 90, 90, 5);
        roster.Enter(RatingEventKey.From(101, 5001, 7), 0);
        roster.Observe([new(1, 20, "New One")]);
        Assert.Equal(1, roster.Count);
        Assert.False(roster.TryGetDriver(2, out _));
        Assert.True(roster.TryGetDriver(1, out var current));
        Assert.Equal("New One", current.Identity);
        Assert.Null(roster.Timing(1).BestLap);
    }

    [Fact]
    public void Unclassified_or_nonfinite_lap_is_not_saved_as_a_valid_time()
    {
        var roster = new StandingsRoster<string>();
        roster.Enter(Event, 0);
        roster.Observe([new(1, 10, "One")]);
        roster.ObserveTiming(1, 0, 90, -1, 0);
        Assert.Null(roster.Timing(1).BestLap);
        roster.ObserveTiming(1, 1, double.PositiveInfinity, double.NaN, 0);
        Assert.Null(roster.Timing(1).BestLap);
        Assert.Null(roster.Timing(1).LastLap);
    }

    [Fact]
    public void A_revised_official_best_lap_replaces_a_previously_faster_deleted_lap()
    {
        var roster = new StandingsRoster<string>();
        roster.Enter(Event, 1);
        roster.Observe([new(1, 10, "One"), new(2, 10, "Two")]);
        roster.ObserveTiming(1, 1, 89, 89, 2);
        roster.ObserveTiming(2, 2, 90, 90, 2);
        roster.ObserveOfficialTiming(1, 2, 92, 92, 2); // 89 s lap was deleted; official best is now 92 s
        Assert.Equal(92, roster.Timing(1).BestLap);
        Assert.Equal(1, roster.TimedPositions().Overall[2]);
        roster.ObserveTiming(1, 0, null, null, -1); // a later disconnect preserves that revision
        Assert.Equal(92, roster.Timing(1).BestLap);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 89)]
    [InlineData(1, 0)]
    [InlineData(0, -1)]
    public void Explicit_official_deletion_of_the_only_lap_clears_time_rank_count_and_qualifying_rating_gate(int position, double bestLap)
    {
        var roster = new StandingsRoster<string>();
        roster.Enter(Event, 1);
        roster.Observe([new(1, 10, "One"), new(2, 10, "Two")]);
        roster.ObserveTiming(1, 1, 89, 89, 1);
        roster.ObserveTiming(2, 2, 90, 90, 1);
        Assert.True(roster.Timing(1).HasValidLap);

        roster.ObserveOfficialTiming(1, position, bestLap, -1, 1);
        Assert.Null(roster.Timing(1).BestLap);
        Assert.Equal(0, roster.Timing(1).Position);
        Assert.False(roster.Timing(1).HasValidLap);
        Assert.Equal(1, roster.TimedPositions().Overall[2]);
        Assert.Equal(2, roster.TimedPositions().Overall[1]);
        var counts = ClassDriverCounts.Compute(roster.Drivers.Select(d => (d.CarIdx, d.ClassId)),
            roster.Drivers.Where(d => roster.Timing(d.CarIdx).HasValidLap).Select(d => d.CarIdx));
        Assert.Equal(new ClassDriverCount(2, 1), counts[10]);
        Assert.False(IRatingProjection.ShowFor(SessionKind.Qualify, playerHasValidLap: true, carHasValidLap: roster.Timing(1).HasValidLap));

        // Telemetry disappearing after the official deletion must preserve the cleared state.
        roster.ObserveTiming(1, 0, null, null, -1);
        Assert.Null(roster.Timing(1).BestLap);
        Assert.False(roster.Timing(1).HasValidLap);
        Assert.Equal(2, roster.Count); // the entrant and identity remain available
    }
}
