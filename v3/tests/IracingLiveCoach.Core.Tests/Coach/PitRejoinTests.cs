using IracingLiveCoach.Core.Coach;
using Xunit;

namespace IracingLiveCoach.Core.Tests.Coach;

public class PitRejoinTests
{
    private const int Me = 0;

    private static RejoinCar Car(int idx, double progress, int cls = 1, bool pit = false, double remaining = 0,
        string clsName = "", double lap = 0)
    {
        int lapc = (int)Math.Floor(progress);
        return new RejoinCar(idx, idx.ToString(), "Driver " + idx, cls, lapc, Math.Round(progress - lapc, 9), pit, remaining, clsName, null, 0, lap);
    }

    // Single class, lap 100 s, loss 25 s. Player 10.50; A 10.60, B 10.55, C 10.48, D 10.30, E 10.10, F 10.02.
    // Rivals move +0.25 lap during the stop: A 10.85, B 10.80, C 10.73, D 10.55, E 10.35, F 10.27.
    private static List<RejoinCar> Field(double cRemaining = 0, bool cInPit = false) => new()
    {
        Car(1, 10.60), Car(2, 10.55), Car(3, 10.48, pit: cInPit, remaining: cRemaining),
        Car(4, 10.30), Car(5, 10.10), Car(6, 10.02),
    };

    [Fact]
    public void Single_class_loss_25s_drops_from_P3_to_P5_behind_D_ahead_of_E()
    {
        var p = PitRejoinPredictor.Predict(Car(Me, 10.50), Field(), 100, 25, raceOrder: true);
        Assert.True(p.Available);
        Assert.Equal(3, p.CurrentPosition);
        Assert.Equal(5, p.PredictedPosition);
        Assert.Equal(4, p.Ahead!.CarIdx);
        Assert.Equal(5.0, p.Ahead.GapSec, 6);
        Assert.Equal(0, p.Ahead.Laps);
        Assert.Equal(5, p.Behind!.CarIdx);
        Assert.Equal(15.0, p.Behind.GapSec, 6);
        Assert.False(p.MultiClass);
        Assert.Empty(p.OtherClasses);
        Assert.Equal(25, p.LossSec);
    }

    [Fact]
    public void Zero_loss_keeps_the_position()
    {
        var p = PitRejoinPredictor.Predict(Car(Me, 10.50), Field(), 100, 0, raceOrder: true);
        Assert.Equal(3, p.CurrentPosition);
        Assert.Equal(3, p.PredictedPosition);
        Assert.Equal(2, p.Ahead!.CarIdx);
        Assert.Equal(5.0, p.Ahead.GapSec, 6);
        Assert.Equal(3, p.Behind!.CarIdx);
        Assert.Equal(2.0, p.Behind.GapSec, 6);
    }

    [Fact]
    public void Lapped_car_counts_behind_and_a_car_a_lap_up_counts_ahead()
    {
        var field = Field();
        field.Add(Car(7, 9.40));  // a lap down: 9.65 after the stop, still behind
        field.Add(Car(8, 11.30)); // a lap up (leader): ahead now and after
        var p = PitRejoinPredictor.Predict(Car(Me, 10.50), field, 100, 25, raceOrder: true);
        Assert.Equal(4, p.CurrentPosition);
        Assert.Equal(6, p.PredictedPosition);
        Assert.Equal(4, p.Ahead!.CarIdx);
        Assert.Equal(5, p.Behind!.CarIdx);

        // A car 1.3 laps behind after the stop: gap 130 s, Laps = 1.
        var q = PitRejoinPredictor.Predict(Car(Me, 10.50), new List<RejoinCar> { Car(9, 8.95) }, 100, 25, raceOrder: true);
        Assert.Equal(1, q.PredictedPosition);
        Assert.Equal(130.0, q.Behind!.GapSec, 6);
        Assert.Equal(1, q.Behind.Laps);
    }

    [Fact]
    public void Leader_pitting_with_nobody_ahead_has_no_ahead_neighbour()
    {
        var p = PitRejoinPredictor.Predict(Car(Me, 11.0), Field(), 100, 25, raceOrder: true);
        Assert.Equal(1, p.CurrentPosition);
        Assert.Equal(1, p.PredictedPosition);
        Assert.Null(p.Ahead);
        Assert.Equal(1, p.Behind!.CarIdx); // A 10.85 -> 15 s
        Assert.Equal(15.0, p.Behind.GapSec, 6);
    }

    [Fact]
    public void Car_in_the_pits_counts_its_remaining_loss()
    {
        // C (10.48) has 20 s of pit loss left: 10.48 + (25 - 20)/100 = 10.53 -> still ahead, 3.0 s.
        var p = PitRejoinPredictor.Predict(Car(Me, 10.50), Field(20, true), 100, 25, raceOrder: true);
        Assert.Equal(5, p.PredictedPosition);
        Assert.Equal(3, p.Ahead!.CarIdx);
        Assert.Equal(3.0, p.Ahead.GapSec, 6);
        Assert.True(p.Ahead.InPit);

        // 30 s left: 10.43 -> behind the player, 7.0 s; D is ahead again.
        var q = PitRejoinPredictor.Predict(Car(Me, 10.50), Field(30, true), 100, 25, raceOrder: true);
        Assert.Equal(4, q.PredictedPosition);
        Assert.Equal(4, q.Ahead!.CarIdx);
        Assert.Equal(3, q.Behind!.CarIdx);
        Assert.Equal(7.0, q.Behind.GapSec, 6);
    }

    [Fact]
    public void Cars_not_in_the_world_are_excluded()
    {
        var field = Field();
        field.Add(new RejoinCar(10, "10", "Gone", 1, 12, -1)); // LapDistPct -1 = disconnected / out
        field.Add(new RejoinCar(11, "11", "NaN", 1, 12, double.NaN));
        var p = PitRejoinPredictor.Predict(Car(Me, 10.50), field, 100, 25, raceOrder: true);
        Assert.Equal(3, p.CurrentPosition);
        Assert.Equal(5, p.PredictedPosition);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(-5)]
    public void Missing_lap_time_is_unavailable_and_never_throws(double lap)
    {
        var p = PitRejoinPredictor.Predict(Car(Me, 10.5), Field(), lap, 25, true);
        Assert.False(p.Available);
        Assert.Equal("no reference lap time", p.Reason);
    }

    [Fact]
    public void Missing_inputs_are_unavailable()
    {
        Assert.False(PitRejoinPredictor.Predict(null, Field(), 100, 25, true).Available);
        Assert.False(PitRejoinPredictor.Predict(Car(Me, 10.5), Field(), 100, double.NaN, true).Available);
        Assert.False(PitRejoinPredictor.Predict(new RejoinCar(0, "0", "me", 1, 3, -1), Field(), 100, 25, true).Available);
        var empty = PitRejoinPredictor.Predict(Car(Me, 10.5), null, 100, 25, true);
        Assert.True(empty.Available);
        Assert.Equal(1, empty.PredictedPosition);
        Assert.Null(empty.Ahead);
        Assert.Null(empty.Behind);
    }

    // Multiclass, loss 33 s. Player class 1 (lap 100 s, +0.33 lap), GTP faster (lap 90 s, +0.366667),
    // GTD slower (lap 110 s, +0.30). Player at 5.50.
    private static List<RejoinCar> Multi() => new()
    {
        Car(1, 5.70, 1, clsName: "LMP2", lap: 100), // -> 6.03 ahead in class, 53 s
        Car(2, 5.10, 1, clsName: "LMP2", lap: 100), // -> 5.43 behind in class, 7 s
        Car(7, 5.10, 2, clsName: "GTP", lap: 90),   // -> 5.466667: 0.033333 lap behind on track = 3.0 s at 90 s
        Car(6, 6.00, 2, clsName: "GTP", lap: 90),   // -> 6.366667: a lap up, 0.133333 behind on track = 12.0 s
        Car(22, 4.26, 3, clsName: "GTD", lap: 110), // -> 4.56: a lap down, 0.06 ahead on track = 6.0 s at 100 s
        Car(30, 4.10, 3, clsName: "GTD", lap: 110), // -> 4.40: 0.10 behind on track = 11.0 s at 110 s
    };

    [Fact]
    public void Multiclass_reports_class_and_overall_position_and_the_nearest_car_of_each_class()
    {
        var me = Car(Me, 5.50, 1, clsName: "LMP2", lap: 100);
        var p = PitRejoinPredictor.Predict(me, Multi(), 100, 33, raceOrder: true);
        Assert.True(p.MultiClass);
        Assert.Equal("LMP2", p.ClassName);
        Assert.Equal(2, p.CurrentPosition);
        Assert.Equal(2, p.PredictedPosition);
        Assert.Equal(1, p.Ahead!.CarIdx);
        Assert.Equal(53.0, p.Ahead.GapSec, 6);
        Assert.Equal(2, p.Behind!.CarIdx);
        Assert.Equal(7.0, p.Behind.GapSec, 6);
        Assert.Equal(3, p.OverallCurrentPosition);   // car 1 (5.70) and GTP 6 (6.00) ahead
        Assert.Equal(3, p.OverallPredictedPosition); // car 1 (6.03) and GTP 6 (6.37); GTP 7 (5.47) now behind

        var gtp = p.OtherClasses.Single(c => c.ClassName == "GTP");
        Assert.True(gtp.Faster);
        Assert.Equal(7, gtp.Behind!.CarIdx);
        Assert.Equal(3.0, gtp.Behind.GapSec, 6);
        Assert.Equal(0, gtp.Behind.Laps);
        Assert.Equal(22.0 / 3.0 * 0 + 6, gtp.Ahead is null ? 6 : 6); // GTP 6 is behind on track too, so no GTP ahead
        Assert.Null(gtp.Ahead);

        var gtd = p.OtherClasses.Single(c => c.ClassName == "GTD");
        Assert.False(gtd.Faster);
        Assert.Equal(22, gtd.Ahead!.CarIdx);
        Assert.Equal(6.0, gtd.Ahead.GapSec, 6);
        Assert.Equal(-1, gtd.Ahead.Laps); // a lap down but physically ahead: traffic
        Assert.Equal(30, gtd.Behind!.CarIdx);
        Assert.Equal(11.0, gtd.Behind.GapSec, 6);

        Assert.Equal(7, p.FasterBehind!.CarIdx);
        Assert.Equal(22, p.SlowerAhead!.CarIdx);
        Assert.Equal("GTP", p.OtherClasses[0].ClassName); // nearest class first (3 s)
    }

    [Fact]
    public void Multiclass_car_a_lap_up_behind_on_track_reports_the_lap_difference()
    {
        var me = Car(Me, 5.50, 1, lap: 100);
        var p = PitRejoinPredictor.Predict(me, new List<RejoinCar> { Car(6, 6.00, 2, clsName: "GTP", lap: 90) }, 100, 33, true);
        var n = p.OtherClasses.Single().Behind!;
        Assert.Equal(6, n.CarIdx);
        Assert.Equal(12.0, n.GapSec, 6);
        Assert.Equal(1, n.Laps);
        Assert.Equal(2, p.OverallPredictedPosition);
        Assert.Equal(1, p.PredictedPosition); // alone in class
    }

    [Fact]
    public void Multiclass_tie_on_track_is_broken_by_car_index()
    {
        var me = Car(Me, 5.50, 1, lap: 100);
        var field = new List<RejoinCar> { Car(41, 4.26, 3, clsName: "GTD", lap: 110), Car(40, 4.26, 3, clsName: "GTD", lap: 110) };
        var p = PitRejoinPredictor.Predict(me, field, 100, 33, true);
        Assert.Equal(40, p.OtherClasses.Single().Ahead!.CarIdx);
    }

    [Fact]
    public void Practice_mode_reports_the_traffic_on_track_without_positions()
    {
        var p = PitRejoinPredictor.Predict(Car(Me, 3.50), new List<RejoinCar> { Car(1, 7.60), Car(2, 1.10) }, 100, 25, raceOrder: false);
        Assert.True(p.Available);
        Assert.False(p.RaceOrder);
        Assert.Equal(0, p.PredictedPosition);
        // car 1 -> .85 (35 s ahead on track), car 2 -> .35 (15 s behind)
        Assert.Equal(1, p.Ahead!.CarIdx);
        Assert.Equal(35.0, p.Ahead.GapSec, 6);
        Assert.Equal(2, p.Behind!.CarIdx);
        Assert.Equal(15.0, p.Behind.GapSec, 6);
    }

    // ------------------------------------------------------------ loss model

    /// <summary>Car on pit road: enters at t0 / 0.95, 1 s later 0.96, stationary at 0.97 for <paramref name="still"/> s,
    /// leaves at t0 + total at 0.05 (lap fraction 0.10).</summary>
    private static void Visit(PitLossModel m, int car, double t0, double still, double total, double lap = 100,
        bool player = false, double fuelIn = -1, double fuelOut = -1, bool tires = false)
    {
        m.Observe(car, false, 0.90, t0 - 1, lap, player, fuelIn, tires);
        m.Observe(car, true, 0.95, t0, lap, player, fuelIn, tires);
        m.Observe(car, true, 0.96, t0 + 1, lap, player, fuelIn, tires);
        m.Observe(car, true, 0.97, t0 + 2, lap, player, fuelIn, tires);
        for (int i = 1; i <= (int)still; i++)
            m.Observe(car, true, 0.97, t0 + 2 + i, lap, player, fuelIn + (fuelOut - fuelIn) * i / still, tires);
        m.Observe(car, false, 0.05, t0 + total, lap, player, fuelOut, tires);
    }

    [Fact]
    public void Lane_loss_is_learned_from_a_real_visit()
    {
        var m = new PitLossModel();
        Visit(m, 3, 1, still: 10, total: 39); // (39 - 10) - 0.10 x 100 = 19
        Assert.Equal(19.0, m.LearnedLaneLossSec!.Value, 6);
        Assert.Equal(10.0, m.TypicalStopSec!.Value, 6);

        var e = m.Estimate(double.NaN, false, 0, 100, double.NaN, double.NaN);
        Assert.True(e.Available);
        Assert.Equal(19.0, e.LaneLossSec, 6);
        Assert.Equal(10.0, e.ServiceSec, 6); // no own refuel rate: other cars' typical stop
        Assert.True(e.Estimated);
        Assert.Equal("service from other cars' stops", e.Note);
        Assert.Equal(29.0, e.TotalSec, 6);
    }

    [Fact]
    public void Player_refuel_rate_gives_the_service_time()
    {
        var m = new PitLossModel();
        Visit(m, 0, 100, still: 8, total: 30, player: true, fuelIn: 10, fuelOut: 50); // 40 L / 8 s; lane (30-8)-10 = 12
        Assert.Equal(5.0, m.LearnedFillRateLps!.Value, 6);
        Assert.Equal(12.0, m.LearnedLaneLossSec!.Value, 6);

        var e = m.Estimate(30, false, 0, 100, double.NaN, double.NaN);
        Assert.Equal(12.0, e.LaneLossSec, 6);
        Assert.Equal(6.0, e.ServiceSec, 6);
        Assert.False(e.Estimated);
        Assert.Equal(18.0, e.TotalSec, 6);

        Assert.Equal(11.0, m.Estimate(30, false, 5, 100, double.NaN, double.NaN).ServiceSec, 6); // + mandatory repair
        Assert.Equal(0.0, m.Estimate(0, false, 0, 100, double.NaN, double.NaN).ServiceSec, 6);  // drive-through
        var tires = m.Estimate(30, true, 0, 100, double.NaN, double.NaN);                       // tyre time unknown
        Assert.True(tires.Estimated);
        Assert.Contains("tyres not included", tires.Note);
        Assert.Equal(6.0, tires.ServiceSec, 6);
    }

    [Fact]
    public void Lane_falls_back_to_pit_speed_limit_when_nothing_was_learned()
    {
        var m = new PitLossModel();
        m.Observe(4, false, 0.90, 0, 100);
        m.Observe(4, true, 0.95, 1, 100);
        m.Observe(4, false, 0.05, 2, 100); // 1 s visit: rejected as a sample, entry/exit points kept
        Assert.Null(m.LearnedLaneLossSec);
        Assert.Equal(0.10, m.LaneFraction(), 6);

        double limit = PitRejoinPredictor.ParseSpeedMs("80.00 kph"); // 22.2222 m/s
        var e = m.Estimate(0, false, 0, 100, 5000, limit);           // 500 m / 22.22 - 10 = 12.5
        Assert.True(e.Available);
        Assert.Equal(12.5, e.LaneLossSec, 6);
        Assert.Equal(0.0, e.ServiceSec, 6);
        Assert.True(e.Estimated);
        Assert.Equal("lane from pit speed limit", e.Note);
    }

    [Fact]
    public void No_pit_data_is_unavailable()
    {
        var e = new PitLossModel().Estimate(10, false, 0, 100, 5000, 22.2);
        Assert.False(e.Available);
        Assert.Equal("no pit lane data yet", e.Note);
        Assert.False(new PitLossModel().Estimate(10, false, 0, double.NaN, 5000, 22.2).Available);
    }

    [Fact]
    public void Remaining_lane_loss_of_a_car_already_on_pit_road()
    {
        var m = new PitLossModel();
        m.Observe(5, false, 0.94, 199, 100);
        m.Observe(5, true, 0.95, 200, 100);
        m.Observe(5, true, 0.97, 205, 100);
        // lost so far = 5 - 0.02 x 100 = 3 -> 19 - 3 = 16 s left
        Assert.Equal(16.0, m.RemainingLaneLoss(5, 0.97, 205, 100, 19), 6);
        Assert.Equal(0.0, m.RemainingLaneLoss(9, 0.97, 205, 100, 19)); // entry never seen
    }

    [Theory]
    [InlineData("80.00 kph", 22.222222)]
    [InlineData("45.00 mph", 20.1168)]
    [InlineData("20 m/s", 20.0)]
    public void Pit_speed_limit_parsing(string text, double ms) =>
        Assert.Equal(ms, PitRejoinPredictor.ParseSpeedMs(text), 5);

    [Fact]
    public void Pit_speed_limit_parsing_rejects_garbage()
    {
        Assert.True(double.IsNaN(PitRejoinPredictor.ParseSpeedMs(null)));
        Assert.True(double.IsNaN(PitRejoinPredictor.ParseSpeedMs("fast")));
    }

    // ------------------------------------------------------------ phrases / voice

    private static PitRejoinPrediction Single(bool inPit = false) => new()
    {
        Available = true, RaceOrder = true, CurrentPosition = 5, PredictedPosition = 9, InPit = inPit,
        Ahead = new RejoinNeighbour(12, "12", "V. Costa", 1.8, 0, false),
    };

    private static PitRejoinPrediction MultiPrediction()
    {
        var me = Car(Me, 5.50, 1, clsName: "LMP2", lap: 100);
        return PitRejoinPredictor.Predict(me, Multi(), 100, 33, raceOrder: true);
    }

    [Theory]
    [InlineData("en", "If you pit now, you rejoin P9, behind car 12, 1.8 seconds.")]
    [InlineData("pt-BR", "Se parar agora, volta em P9, atrás do carro 12, 1,8 segundos.")]
    [InlineData("es", "Si entras ahora, sales P9, detrás del coche 12, a 1,8 segundos.")]
    [InlineData("de", "Wenn du jetzt reinkommst, kommst du raus auf P9, hinter Wagen 12, 1,8 Sekunden.")]
    [InlineData("fr", "Si tu rentres maintenant, tu ressors P9, derrière la voiture 12, à 1,8 secondes.")]
    public void Single_class_phrase_in_every_language(string lang, string expected)
    {
        var (key, args) = RaceEngineer.RejoinPhrase(Single(), lang);
        Assert.Equal("pit.rejoin", key);
        Assert.Equal(expected, CoachPhrases.Render(lang, key, args));
    }

    [Theory]
    [InlineData("en", "If you pit now, you rejoin P2 in class, GTP 7 3.0 seconds behind, GTD 22 ahead by 6.0 seconds.")]
    [InlineData("pt-BR", "Se parar agora, volta em P2 na classe, GTP 7 a 3,0 segundos atrás, GTD 22 à frente a 6,0 segundos.")]
    [InlineData("es", "Si entras ahora, sales P2 en la clase, GTP 7 a 3,0 segundos detrás, GTD 22 delante a 6,0 segundos.")]
    [InlineData("de", "Wenn du jetzt reinkommst, kommst du raus auf P2 in der Klasse, GTP 7 3,0 Sekunden hinter dir, GTD 22 6,0 Sekunden vor dir.")]
    [InlineData("fr", "Si tu rentres maintenant, tu ressors P2 dans la catégorie, GTP 7 à 3,0 secondes derrière, GTD 22 devant à 6,0 secondes.")]
    public void Multiclass_phrase_in_every_language(string lang, string expected)
    {
        var (key, args) = RaceEngineer.RejoinPhrase(MultiPrediction(), lang);
        Assert.Equal("pit.rejoin.mc", key);
        Assert.Equal(expected, CoachPhrases.Render(lang, key, args));
    }

    [Fact]
    public void Phrase_variants()
    {
        Assert.Equal("You rejoin P9, behind car 12, 1.8 seconds.",
            Render("en", Single(inPit: true)));
        Assert.Equal("If you pit now, you rejoin P1, clear ahead.",
            Render("en", Single() with { PredictedPosition = 1, Ahead = null }));
        // multiclass with the other classes far away: own class only
        var far = MultiPrediction() with { OtherClasses = Array.Empty<RejoinClassTraffic>(), MultiClass = true };
        Assert.Equal("If you pit now, you rejoin P2 in class, behind car 1, 53.0 seconds.", Render("en", far));
        Assert.Equal("If you pit now, you rejoin 35.0 seconds behind car 1.",
            Render("en", PitRejoinPredictor.Predict(Car(Me, 3.50), new List<RejoinCar> { Car(1, 7.60) }, 100, 25, false)));
    }

    private static string Render(string lang, PitRejoinPrediction p)
    {
        var (key, args) = RaceEngineer.RejoinPhrase(p, lang);
        return CoachPhrases.Render(lang, key, args);
    }

    [Fact]
    public void Voice_question_where_will_I_rejoin()
    {
        Assert.Contains("rejoin", CoachQuery.Intents);
        var st = EngineerStatus.Empty with { Rejoin = Single() };
        var q = new CoachQueryState("pt-BR", null, double.NaN, 0, null, null, st);
        Assert.Equal("Se parar agora, volta em P9, atrás do carro 12, 1,8 segundos.", CoachQuery.Answer("rejoin", q));
        Assert.Equal("No rejoin estimate yet.", CoachQuery.Answer("rejoin", q with { Language = "en", Engineer = EngineerStatus.Empty }));
        Assert.Equal("No rejoin estimate yet.",
            CoachQuery.Answer("rejoin", q with { Language = "en", Engineer = st with { Rejoin = PitRejoinPrediction.Unavailable("x") } }));
    }

    // ------------------------------------------------------------ proactive calls

    [Fact]
    public void Pit_entry_in_a_race_announces_the_rejoin()
    {
        var sim = new EngSim(50);
        sim.Step(0.10);
        sim.T += 1;
        sim.Step(0.20, pit: true, mod: d => d with { Rejoin = Single(inPit: true) });
        var m = Assert.Single(sim.Keys("pit.rejoin"));
        Assert.Equal("You rejoin P9, behind car 12, 1.8 seconds.", m.Text);
        Assert.True(m.AllowStopped);

        // staying on pit road does not repeat it
        sim.T += 1;
        sim.Step(0.21, pit: true, mod: d => d with { Rejoin = Single(inPit: true) });
        Assert.Single(sim.Keys("pit.rejoin"));
    }

    [Fact]
    public void No_rejoin_call_when_toggled_off_outside_races_or_without_prediction()
    {
        foreach (var setup in new Action<EngSim>[]
                 {
                     s => s.E.Options.Rejoin = false,
                     s => s.Session = "Practice",
                 })
        {
            var sim = new EngSim(50);
            setup(sim);
            sim.Step(0.10);
            sim.T += 1;
            sim.Step(0.20, pit: true, mod: d => d with { Rejoin = Single() });
            Assert.Empty(sim.Keys("pit.rejoin"));
        }
        var none = new EngSim(50);
        none.Step(0.10);
        none.T += 1;
        none.Step(0.20, pit: true, mod: d => d with { Rejoin = PitRejoinPrediction.Unavailable("no pit lane data yet") });
        Assert.Empty(none.Keys("pit.rejoin"));
    }

    [Fact]
    public void Rejoin_is_announced_periodically_when_the_pit_window_is_close()
    {
        // 1 L/lap, 40-lap race, 12 L: the window (<= 3.5 laps of fuel, short of the finish) opens in lap 9.
        var sim = new EngSim(12) { TotalLaps = 40 };
        sim.Data = sim.Data with { Rejoin = Single() };
        for (int i = 0; i < 8; i++) sim.DriveLap(1.0);
        Assert.Empty(sim.Keys("pit.rejoin"));
        for (int i = 0; i < 4; i++) sim.DriveLap(1.0);
        var calls = sim.Keys("pit.rejoin");
        Assert.InRange(calls.Count, 2, 4); // every 2 laps (plus "box this lap"), never every sample
        Assert.NotNull(sim.E.Status.Rejoin);
    }
}
