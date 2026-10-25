using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Gfx;
using Ams2.OverlayHost.Theme;
using Ams2.OverlayHost.Widgets;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

/// <summary>Exibicao nos widgets: listas estatica/dinamica do 1993 e pecas do Race/Qualy Board (padrao = comportamento antigo).</summary>
public class ShowPiecesTests
{
    const double Now = 200;
    static readonly WidgetSettings Board = new() { Id = "board", Columns = [] };

    static OverlayModel Race(int lapsRemaining = 10)
    {
        var m = LayoutPreview.WithCountries(LayoutPreview.ForWidget("board")) with { Now = Now, PlayerDriving = true };
        return m with
        {
            Session = m.Session! with { InSession = true, GameState = 2, LapsInEvent = 20 },
            Board = m.Board! with { Gap93 = null, LapsRemaining = lapsRemaining },
            Broadcast = BroadcastState.Empty,
        };
    }

    static byte[] Render(Action<ThemeCanvas> draw)
    {
        using var gfx = DeviceResources.CreateOffscreen(1920, 300);
        using var c = new ThemeCanvas(gfx, Themes.F1_1993);
        gfx.BeginFrame(); c.Begin(); draw(c); c.End(); gfx.EndFrame();
        return gfx.ReadPixelsBgra();
    }

    static WidgetSettings Pieces(params (string Id, bool? Enabled, double? Duration)[] pieces) => Board with
    {
        Show = new ShowSettings { Pieces = pieces.ToDictionary(p => p.Id, p => new ShowSettings { Enabled = p.Enabled, DurationSeconds = p.Duration }) },
    };

    [Fact]
    public void List_pick_static_is_the_top_and_dynamic_follows_the_player()
    {
        Assert.Equal([0, 1, 2, 3, 4, 5], Broadcast93List.Pick(20, 14, 6, dynamic: false, around: 3));
        Assert.Equal([0, 1, 2, 3, 4, 5], Broadcast93List.Pick(20, 3, 6, dynamic: true, around: 3));   // jogador ja no topo
        Assert.Equal([0, 1, 2, 13, 14, 15], Broadcast93List.Pick(20, 14, 6, dynamic: true, around: 3));
        Assert.Equal([17, 18, 19], Broadcast93List.Pick(20, 19, 3, dynamic: true, around: 10));       // janela limitada as linhas
        Assert.Equal([0, 1], Broadcast93List.Pick(2, 1, 6, dynamic: true, around: 3));
    }

    [Fact]
    public void Default_pieces_keep_the_old_board_timings()
    {
        var h = Broadcast93RaceBoard.Holds.From(ShowPlan.For(Board, "f1-1993"));
        Assert.Equal(Broadcast93RaceBoard.Holds.Default, h);
        Assert.Equal((Broadcast93Rules.PitResultHold, FastestLapDetector.DefaultHold, Broadcast93Rules.LapsToGoHold), (h.Pit, h.Fastest, h.LapsToGo));
    }

    [Fact]
    public void Piece_durations_change_how_long_each_plate_stays()
    {
        var m = Race(10) with { Broadcast = BroadcastState.Empty with { PlayerLapChangedT = Now - 8 } };
        Assert.Equal(Broadcast93RaceBoard.Mode.Caption, Broadcast93RaceBoard.Select(m));
        var longer = Broadcast93RaceBoard.Holds.From(ShowPlan.For(Pieces(("lapsToGo", null, 10)), "f1-1993"));
        Assert.Equal(Broadcast93RaceBoard.Mode.LapsToGo, Broadcast93RaceBoard.Select(m, holds: longer));

        var left = BroadcastState.Empty with { PlayerStopEndT = Now - 5, PlayerStopSeconds = 2.4 };
        Assert.False(Broadcast93RaceBoard.PitActive(left, Now));
        Assert.True(Broadcast93RaceBoard.PitActive(left, Now, Broadcast93RaceBoard.Holds.From(ShowPlan.For(Pieces(("pit", null, 8)), "f1-1993")).Pit));

        var fastest = new FastestLapEvent(m.Session!.Cars[1], 87.4, Now - 9);
        Assert.False(fastest.ActiveAt(Now));
        var f = Broadcast93RaceBoard.Holds.From(ShowPlan.For(Pieces(("fastest", null, 12)), "f1-1993"));
        Assert.Equal(Broadcast93RaceBoard.Mode.Fastest, Broadcast93RaceBoard.Select(m, fastest, holds: f));
    }

    [Fact]
    public void Disabled_pieces_are_skipped()
    {
        var m = Race(10) with { Broadcast = BroadcastState.Empty with { PlayerLapChangedT = Now - 1 } };
        var p = new Broadcast93RacePresentation();
        Assert.Equal(Broadcast93RaceBoard.Mode.LapsToGo, p.Update(m).Mode);
        Assert.Equal(Broadcast93RaceBoard.Mode.Caption, new Broadcast93RacePresentation().Update(m, lapsToGoEnabled: false).Mode);
        var pit = m with { Broadcast = m.Broadcast! with { PlayerStopped = true, PlayerStopStartT = Now - 2, PlayerInPitLane = true, PlayerPitLaneStartT = Now - 10 } };
        Assert.Equal(Broadcast93RaceBoard.Mode.Pit, new Broadcast93RacePresentation().Update(pit).Mode);
        Assert.NotEqual(Broadcast93RaceBoard.Mode.Pit, new Broadcast93RacePresentation().Update(pit, pitEnabled: false).Mode);

        // Legenda desligada: o board fica vazio quando nao ha outra peca.
        var idle = Race(7);
        Assert.Contains(Render(c => new Broadcast93RaceBoard().Draw(c, idle, Board)), b => b != 0);
        Assert.DoesNotContain(Render(c => new Broadcast93RaceBoard().Draw(c, idle, Pieces(("caption", false, null)))), b => b != 0);
        // A opcao antiga "showFastest" continua valendo como padrao da peca.
        Assert.False(ShowPlan.For(Board with { Options = new() { ["showFastest"] = "false" } }, "f1-1993").PieceEnabled("fastest"));
    }

    [Fact]
    public void Qualy_board_1999_split_and_result_pieces()
    {
        var q = new WidgetSettings { Id = "qualiboard" };
        var plan = ShowPlan.For(q with { Show = new ShowSettings { Pieces = new() { ["split"] = new ShowSettings { DurationSeconds = 6 }, ["result"] = new ShowSettings { Enabled = false } } } }, "f1-1998");
        Assert.Equal(6, plan.PieceDuration("split", 0));
        Assert.False(plan.PieceEnabled("result"));
        Assert.Equal(QualiBoard99Presentation.SplitHold, ShowPlan.For(q, "f1-1998").PieceDuration("split", 0));
        Assert.Equal(QualiBoard99Presentation.ResultHold, ShowPlan.For(q, "f1-1998").PieceDuration("result", 0));
    }

    [Fact]
    public void Race_board_1999_pieces_reach_the_core_presentation()
    {
        Assert.Equal(Race99Options.Default, Broadcast98RaceBoard.CoreOptions(Board, "f1-1998"));
        Assert.Equal(Race99Options.Default, Broadcast98RaceBoard.CoreOptions(Pieces(("fastest", null, 12)), "f1-1993"));
        var o = Broadcast98RaceBoard.CoreOptions(Pieces(("fastest", null, 12), ("table", false, 6), ("gap", null, 9)), "f1-1999-digital");
        Assert.Equal((12d, 9d, 6d, false), (o.FastestHold, o.GapHold, o.PageHold!.Value, o.Table));
        Assert.True(o.Pit && o.Fastest && o.Gap && o.Remaining);
    }

    static WidgetSettings Triggered(string piece, ShowSettings s) => Board with { Show = new ShowSettings { Pieces = new() { [piece] = s } } };

    [Fact]
    public void Piece_trigger_defaults_normalize_away()
    {
        var def = ShowCatalog.For("f1-1993", "board");
        var native = new ShowSettings { Pieces = new() { ["caption"] = new ShowSettings { Enabled = true, Trigger = ShowTriggers.Always, DurationSeconds = 8, PeriodSeconds = 30 } } };
        Assert.Null(native.Normalized(def, Board));   // perfil sem mudanca continua sem bloco "show"
        var ev = new ShowSettings { Pieces = new() { ["caption"] = new ShowSettings { Trigger = ShowTriggers.Events, Events = [ShowEvents.Flag], DurationSeconds = 5 } } }.Normalized(def, Board)!;
        Assert.Equal(ShowTriggers.Events, ev.Pieces!["caption"].Trigger);
        Assert.Equal([ShowEvents.Flag], ev.Pieces["caption"].Events!);
        Assert.Equal(5, ev.Pieces["caption"].DurationSeconds);
    }

    [Fact]
    public void Piece_gate_follows_events_and_period()
    {
        var plan = ShowPlan.For(Triggered("caption", new ShowSettings { Trigger = ShowTriggers.Events, Events = [ShowEvents.PlayerLap], DurationSeconds = 6 }), "f1-1993");
        Assert.Equal(1f, plan.PieceGate("caption", Now, e => e == ShowEvents.PlayerLap ? Now - 3 : null), 3);
        Assert.Equal(0f, plan.PieceGate("caption", Now, e => e == ShowEvents.PlayerLap ? Now - 20 : null));
        Assert.Equal(0f, plan.PieceGate("caption", Now, _ => null));
        Assert.False(plan.At(Now, _ => null).PieceEnabled("caption"));
        Assert.True(plan.At(Now, e => e == ShowEvents.PlayerLap ? Now - 3 : null).PieceEnabled("caption"));

        var per = ShowPlan.For(Triggered("caption", new ShowSettings { Trigger = ShowTriggers.Periodic, PeriodSeconds = 60, DurationSeconds = 10 }), "f1-1993");
        Assert.True(per.PieceGate("caption", 125, _ => null) > 0.99f);
        Assert.Equal(0f, per.PieceGate("caption", 150, _ => null));
    }

    [Fact]
    public void Plan_without_piece_triggers_is_unchanged_at_any_instant()
    {
        var plan = ShowPlan.For(Pieces(("fastest", null, 12)), "f1-1993");
        Assert.Same(plan, plan.At(Now, _ => Now));
    }

    [Fact]
    public void Caption_waiting_for_its_event_leaves_the_board_empty()
    {
        var idle = Race(7);
        var cfg = Triggered("caption", new ShowSettings { Trigger = ShowTriggers.Events, Events = [ShowEvents.PlayerLap] });
        Assert.DoesNotContain(Render(c => new Broadcast93RaceBoard().Draw(c, idle, cfg)), b => b != 0);
        var fired = idle with { Events = ShowEventTimes.Empty with { PlayerLap = Now - 2 } };
        Assert.Contains(Render(c => new Broadcast93RaceBoard().Draw(c, fired, cfg)), b => b != 0);
    }

    [Fact]
    public void Driver_caption_follows_the_winner_duration()
    {
        var profile = ProfileFactory.CreateDefault("t", "f1-2004");
        var caption = profile.Get("drivercaption")!;
        Assert.Same(caption, RenderSettings.For(profile, caption));   // sem Exibicao: as proprias settings (perfil identico)
        var winner = profile.Get("winner")! with { Show = new ShowSettings { DurationSeconds = 20 } };
        var changed = profile.WithWidget(winner);
        Assert.Equal("20", RenderSettings.For(changed, changed.Get("drivercaption")!).Option(RenderSettings.WinnerHoldOption));
        Assert.Contains("drivercaption", RenderSettings.Dependents("winner"));
    }
}
