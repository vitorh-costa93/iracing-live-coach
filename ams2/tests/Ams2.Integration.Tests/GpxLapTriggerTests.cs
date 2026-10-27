using Ams2.Core.Calc;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

/// <summary>Gatilho de peca "a cada N voltas" (Race Board mesclado): janela abre quando a volta fecha e e multipla de N.</summary>
public sealed class GpxLapTriggerTests
{
    const string Theme = "f1-2004";

    static ShowPlan Plan(ShowSettings? piece, string id = "time")
        => ShowPlan.For(new WidgetSettings { Id = "gpxsat", Show = new ShowSettings { Pieces = new() { [id] = piece! } } }.Normalized(Theme), Theme);

    [Fact]
    public void Gate_opens_on_every_nth_lap_for_the_rule_duration()
    {
        var plan = Plan(new ShowSettings { Trigger = ShowTriggers.Laps });
        Assert.Equal((5, ShowLapsOf.Player), (plan.PieceEveryLaps("time"), plan.PieceLapsOf("time")));
        Assert.Equal(ShowTriggers.Laps, plan.PieceTrigger("time"));
        Func<string, double?> last = id => id == "playerLap" ? 100 : null;
        float Alpha(int laps, double now) => plan.At(now, last, _ => laps).PieceAlpha("time");
        Assert.Equal(1f, Alpha(5, 103));      // volta 5, dentro dos 6 s
        Assert.Equal(0f, Alpha(5, 107));      // fora da janela
        Assert.Equal(0f, Alpha(4, 101));      // volta 4 nao e multipla
        Assert.Equal(0f, Alpha(0, 101));      // volta 0 nao conta
        Assert.Equal(1f, Alpha(10, 101));
    }

    [Fact]
    public void Leader_laps_and_custom_count_and_duration()
    {
        var plan = Plan(new ShowSettings { Trigger = ShowTriggers.Laps, EveryLaps = 5, LapsOf = "leader", DurationSeconds = 10 }, "time");
        Assert.Equal((5, ShowLapsOf.Leader), (plan.PieceEveryLaps("time"), plan.PieceLapsOf("time")));
        Func<string, double?> last = id => id == "leaderLap" ? 50 : id == "playerLap" ? 0 : null;
        Assert.Equal(1f, plan.At(58, last, of => of == "leader" ? 10 : 7).PieceAlpha("time"));
        Assert.Equal(0f, plan.At(61, last, of => of == "leader" ? 10 : 7).PieceAlpha("time"));
        Assert.Equal(0f, plan.At(52, last, of => of == "leader" ? 9 : 10).PieceAlpha("time"));
        Assert.Equal(0f, plan.At(52, last).PieceAlpha("time"));   // sem contador de voltas = fechado
    }

    [Fact]
    public void Only_pieces_with_a_lap_rule_accept_the_trigger_and_values_are_clamped()
    {
        var other = Plan(new ShowSettings { Trigger = ShowTriggers.Laps }, "standings");
        Assert.Equal(ShowTriggers.Native, other.PieceTrigger("standings"));
        // O gap (peca split) abre por evento de setor: nao aceita a regra de voltas (so o comparativo de melhor volta e a cada 3 voltas).
        Assert.Equal(ShowTriggers.Native, Plan(new ShowSettings { Trigger = ShowTriggers.Laps }, "split").PieceTrigger("split"));
        var wild = Plan(new ShowSettings { Trigger = ShowTriggers.Laps, EveryLaps = 9999, LapsOf = "xx" });
        Assert.Equal((ShowSettings.MaxEveryLaps, ShowLapsOf.Player), (wild.PieceEveryLaps("time"), wild.PieceLapsOf("time")));
        var zero = Plan(new ShowSettings { Trigger = ShowTriggers.Laps, EveryLaps = 0 });
        Assert.Equal(1, zero.PieceEveryLaps("time"));
    }

    [Fact]
    public void Old_profiles_without_lap_fields_still_load_and_defaults_are_not_stored()
    {
        var old = new WidgetSettings { Id = "gpxsat", Show = new ShowSettings { Pieces = new() { ["time"] = new ShowSettings { Enabled = false } } } }.Normalized(Theme);
        Assert.False(ShowPlan.For(old, Theme).PieceEnabled("time"));
        Assert.Equal(ShowTriggers.Laps, ShowPlan.For(old, Theme).PieceTrigger("time"));   // mescla e o padrao
        Assert.Null(new WidgetSettings { Id = "gpxsat", Show = ShowCatalog.RaceBoardRules }.Normalized(Theme).Show);   // padrao nao se grava
        var cfg = new WidgetSettings { Id = "gpxsat", Show = ShowCatalog.PureGpx }.Normalized(Theme);
        var time = cfg.Show!.Pieces!["time"];
        Assert.Equal((ShowTriggers.Native, null, null, null), (time.Trigger, time.EveryLaps, time.LapsOf, time.DurationSeconds));
        string json = System.Text.Json.JsonSerializer.Serialize(cfg.Show);
        var back = System.Text.Json.JsonSerializer.Deserialize<ShowSettings>(json)!;
        Assert.Equal(ShowTriggers.Native, back.Pieces!["time"].Trigger);
    }

    [Fact]
    public void Race_board_rules_apply_to_all_gpxsat_themes()
    {
        foreach (var theme in new[] { "f1-1998", "f1-1999-digital", "f1-2004", "f1-2012" })
        {
            var plan = ShowPlan.For(new WidgetSettings { Id = "gpxsat" }.Normalized(theme), theme);
            Assert.Equal((ShowTriggers.Native, ShowTriggers.Laps), (plan.PieceTrigger("split"), plan.PieceTrigger("time")));
            var pure = ShowPlan.For(new WidgetSettings { Id = "gpxsat", Show = ShowCatalog.PureGpx }.Normalized(theme), theme);
            Assert.Equal((ShowTriggers.Native, ShowTriggers.Native), (pure.PieceTrigger("split"), pure.PieceTrigger("time")));
            Assert.Same(pure, pure.At(100, _ => 99, _ => 3));   // puro GPx: sem janela, despacho do GPxPatch
            Assert.Equal(0f, plan.At(100, _ => 99, _ => 2).PieceAlpha("time"));   // mesclado: voltas restantes so a cada 5 voltas
            Assert.Equal(1f, plan.At(100, _ => 99, _ => 5).PieceAlpha("time"));
            Assert.Equal(1f, plan.At(100, _ => null, _ => 2).PieceAlpha("split"));   // gap: sem regra de voltas (abre por evento de setor)
            Assert.Equal(5, plan.PieceEveryLaps("time"));
            Assert.Equal(6d, plan.PieceDuration("time", 6));
            Assert.Equal(ShowTriggers.Native, plan.PieceTrigger("sky"));
        }
    }

    [Fact]
    public void Best_lap_comparison_opens_every_3_laps_for_6s_and_is_closed_in_pure_gpx()
    {
        foreach (var theme in new[] { "f1-1998", "f1-1999-digital", "f1-2004", "f1-2012" })
        {
            var plan = ShowPlan.For(new WidgetSettings { Id = "gpxsat" }.Normalized(theme), theme);
            Assert.Equal((ShowTriggers.Laps, 3, ShowLapsOf.Player, 6d), (plan.PieceTrigger("lapcompare"), plan.PieceEveryLaps("lapcompare"), plan.PieceLapsOf("lapcompare"), plan.PieceDuration("lapcompare", 6)));
            Func<string, double?> last = id => id == "playerLap" ? 100 : null;
            float Alpha(int laps, double now) => plan.At(now, last, _ => laps).PieceAlpha("lapcompare");
            Assert.Equal(1f, Alpha(3, 103)); Assert.Equal(1f, Alpha(6, 105));
            Assert.Equal(0f, Alpha(3, 107));   // fora dos 6 s
            foreach (int laps in new[] { 0, 1, 2, 4, 5, 7 }) Assert.Equal(0f, Alpha(laps, 101));
            Assert.True(plan.At(103, last, _ => 3).PieceWindowOpen("lapcompare"));
            Assert.False(plan.At(103, last, _ => 4).PieceWindowOpen("lapcompare"));
            // Duracao e contagem configuraveis.
            var custom = ShowPlan.For(new WidgetSettings { Id = "gpxsat", Show = new ShowSettings { Pieces = new() { ["lapcompare"] = new ShowSettings { Trigger = ShowTriggers.Laps, EveryLaps = 2, DurationSeconds = 9 } } } }.Normalized(theme), theme);
            Assert.Equal((2, 9d), (custom.PieceEveryLaps("lapcompare"), custom.PieceDuration("lapcompare", 6)));
            Assert.Equal(1f, custom.At(108, last, _ => 4).PieceAlpha("lapcompare"));
            // Puro GPxPatch: a peca nao existe la, fica fechada (o liga/desliga do Control Center segue ligado).
            var pure = ShowPlan.For(new WidgetSettings { Id = "gpxsat", Show = ShowCatalog.PureGpx }.Normalized(theme), theme);
            Assert.Equal(ShowTriggers.Native, pure.PieceTrigger("lapcompare"));
            Assert.True(pure.PieceEnabled("lapcompare"));
            Assert.Equal(0f, pure.At(103, last, _ => 3).PieceAlpha("lapcompare"));
            Assert.False(pure.At(103, last, _ => 3).PieceWindowOpen("lapcompare"));
            // Desligada na Exibicao = fechada tambem na mescla.
            var off = ShowPlan.For(new WidgetSettings { Id = "gpxsat", Show = new ShowSettings { Pieces = new() { ["lapcompare"] = new ShowSettings { Enabled = false } } } }.Normalized(theme), theme);
            Assert.Equal(0f, off.At(103, last, _ => 3).PieceAlpha("lapcompare"));
            // Perfil antigo (sem bloco da peca) ganha a peca ligada por padrao, sem migracao.
            var fresh = ProfileFactory.CreateDefault("x", theme, 1920, 1080);
            var old = (fresh with { SchemaVersion = 10 }).Normalized();
            Assert.Equal(ShowTriggers.Laps, ShowPlan.For(old.Get("gpxsat")!, theme).PieceTrigger("lapcompare"));
            Assert.Null(old.Get("gpxsat")!.Show);
        }
    }

    [Fact]
    public void Migration_v11_keeps_pure_gpx_only_where_the_piece_was_customized()
    {
        foreach (var theme in new[] { "f1-1998", "f1-1999-digital", "f1-2004", "f1-2012" })
        {
            var fresh = ProfileFactory.CreateDefault("x", theme, 1920, 1080);
            Assert.Null(fresh.Get("gpxsat")!.Show);
            Profile Old(ShowSettings? show) => (fresh with { SchemaVersion = 10, Widgets = fresh.Widgets.Select(w => w.Id == "gpxsat" ? w with { Show = show } : w).ToList() }).Normalized();
            ShowPlan P(Profile p) => ShowPlan.For(p.Get("gpxsat")!, theme);
            Assert.Equal((ShowTriggers.Native, ShowTriggers.Laps), (P(Old(null)).PieceTrigger("split"), P(Old(null)).PieceTrigger("time")));
            var custom = Old(new ShowSettings { Pieces = new() { ["split"] = new ShowSettings { Enabled = false }, ["time"] = new ShowSettings { Trigger = ShowTriggers.Laps, EveryLaps = 4 } } });
            Assert.Equal((ShowTriggers.Native, false), (P(custom).PieceTrigger("split"), P(custom).PieceEnabled("split")));
            Assert.Equal((ShowTriggers.Laps, 4), (P(custom).PieceTrigger("time"), P(custom).PieceEveryLaps("time")));
            Assert.Equal(Profile.CurrentSchemaVersion, custom.SchemaVersion);
        }
    }

    [Fact]
    public void Event_tracker_reports_laps_of_leader_and_player()
    {
        var s = Ams2.OverlayHost.Data.LayoutPreview.ForWidget("lapcounter").Session!;
        var times = new ShowEventTracker().Update(10, s);
        Assert.Equal(s.Cars.First(c => c.Position == 1).LapsCompleted, times.LeaderLaps);
        Assert.Equal(s.PlayerCar!.LapsCompleted, times.PlayerLaps);
        Assert.Equal(times.LeaderLaps, times.Laps("leader"));
        Assert.Equal(times.PlayerLaps, times.Laps("player"));
    }
}
