using System.Text.Json;
using Ams2.Shared.Ipc;
using Ams2.Shared.Profiles;

namespace Ams2.Shared.Tests;

/// <summary>Exibicao por widget/peca (WidgetSettings.Show): padroes do catalogo, legado, limites, JSON e envelope do gatilho.</summary>
public class ShowSettingsTests
{
    static WidgetSettings W(string id, ShowSettings? show = null, Dictionary<string, string>? options = null)
        => new() { Id = id, Show = show, Options = options };

    [Fact]
    public void Profiles_without_show_keep_the_old_values()
    {
        var p = ProfileFactory.CreateDefault("x", "f1-2018", 1920, 1080).Normalized();
        Assert.All(p.Widgets, w => Assert.Null(w.Show));
        Assert.DoesNotContain("\"show\"", JsonSerializer.Serialize(p, ProfileStore.Json));
        // Valores que antes eram constantes no codigo.
        Assert.Equal(6, ShowPlan.For(W("qualilap"), "f1-2018").Duration);
        Assert.Equal(3, ShowPlan.For(W("qualilap"), "f1-2018").Timing("split", 0));
        Assert.Equal(4, ShowPlan.For(W("qualilap"), "f1-2018").Timing("sectorPanel", 0));
        Assert.Equal(15, ShowPlan.For(W("qualiresult"), "f1-2018").Duration);
        Assert.Equal(12, ShowPlan.For(W("winner"), "f1-2018").Duration);
        Assert.Equal(10, ShowPlan.For(W("winner"), "f1-1998").Duration);
        Assert.Equal(10, ShowPlan.For(W("standings"), "f1-2018").Timing("mode", 0));
        Assert.Equal(6, ShowPlan.For(W("ticker"), "f1-2012").Timing("page", 0));
        Assert.Equal((.16, .16), (ShowPlan.For(W("pitstops"), "f1-2004").FadeIn, ShowPlan.For(W("pitstops"), "f1-2004").FadeOut));
        Assert.Equal((.3, .6), (ShowPlan.For(W("pitstops"), "f1-1998").FadeIn, ShowPlan.For(W("pitstops"), "f1-1998").FadeOut));
        Assert.True(ShowPlan.For(W("qualiresult"), "f1-2018").IsNative);
    }

    [Fact]
    public void Legacy_option_is_the_fallback_and_show_wins()
    {
        var legacy = W("qualilap", options: new() { ["showFor"] = "9" });
        Assert.Equal(9, ShowPlan.For(legacy, "f1-2018").Duration);
        Assert.Equal(12, ShowPlan.For(legacy with { Show = new ShowSettings { DurationSeconds = 12 } }, "f1-2018").Duration);
        Assert.Equal(15, ShowPlan.For(legacy with { Show = new ShowSettings { DurationSeconds = 99 } }, "f1-2018").Duration);
        var board = W("board", options: new() { ["gapHoldSeconds"] = "10", ["showFastest"] = "false" });
        var plan = ShowPlan.For(board, "f1-1993");
        Assert.Equal(10, plan.PieceDuration("gap", 0));
        Assert.False(plan.PieceEnabled("fastest"));
        Assert.True(plan.PieceEnabled("caption"));
        Assert.Equal(3, plan.PieceDuration("pit", 0));
        Assert.Equal(7, plan.PieceDuration("fastest", 0));
        Assert.Equal(6, plan.PieceDuration("lapsToGo", 0));
        var on = board with { Show = new ShowSettings { Pieces = new() { ["fastest"] = new ShowSettings { Enabled = true, DurationSeconds = 12 } } } };
        Assert.True(ShowPlan.For(on, "f1-1993").PieceEnabled("fastest"));
        Assert.Equal(12, ShowPlan.For(on, "f1-1993").PieceDuration("fastest", 0));
        Assert.Contains("gapHoldSeconds", ShowCatalog.For("f1-1993", "board").LegacyOptionIds);
        Assert.Contains("showFastest", ShowCatalog.For("f1-1993", "board").LegacyOptionIds);
    }

    [Fact]
    public void Normalisation_clamps_and_drops_defaults()
    {
        var def = ShowCatalog.For("f1-2018", "qualilap");
        Assert.Null(new ShowSettings { Trigger = "native", DurationSeconds = 6, FadeInSeconds = .3, FadeOutSeconds = .6, Events = ["sector", "playerLap"] }.Normalized(def));
        // Igual a opcao antiga = padrao efetivo, sai.
        Assert.Null(new ShowSettings { DurationSeconds = 9 }.Normalized(def, W("qualilap", options: new() { ["showFor"] = "9" })));
        var n = new ShowSettings { Trigger = "PERIODIC", PeriodSeconds = 1, DurationSeconds = 400, FadeInSeconds = -1, FadeOutSeconds = 30, Events = ["nope", "flag", "flag"],
            Timings = new() { ["split"] = 50, ["unknown"] = 2 }, Rows = 4 }.Normalized(def)!;
        Assert.Equal(ShowTriggers.Periodic, n.Trigger);
        Assert.Equal(ShowSettings.MinPeriod, n.PeriodSeconds);
        Assert.Equal(15, n.DurationSeconds);
        Assert.Equal(0, n.FadeInSeconds);        // limitado a 0 (diferente do padrao .3, fica)
        Assert.Equal(ShowSettings.MaxFade, n.FadeOutSeconds);
        Assert.Equal(["flag"], n.Events);
        Assert.Equal(10, n.Timings!["split"]);
        Assert.False(n.Timings.ContainsKey("unknown"));
        Assert.Null(n.Rows);                     // widget sem lista
        // GPxSAT nao aceita gatilho generico.
        Assert.Null(new ShowSettings { Trigger = "always" }.Normalized(ShowCatalog.For("f1-1998", "gpxsat")));
        // Lista: forma, linhas e janela.
        var tower = new ShowSettings { Form = "dynamic", Rows = 99, Around = 50 }.Normalized(ShowCatalog.For("f1-1993", "qualitower"))!;
        Assert.Equal((ShowForms.Dynamic, 20, ShowSettings.MaxAround), (tower.Form, tower.Rows, tower.Around));
    }

    [Fact]
    public void Fade_in_zero_is_kept_when_it_differs_from_the_default()
    {
        var n = new ShowSettings { FadeInSeconds = 0 }.Normalized(ShowCatalog.For("f1-1998", "pitstops"))!;
        Assert.Equal(0, n.FadeInSeconds);
    }

    [Fact]
    public void Show_block_round_trips_in_profile_json_and_ipc_patch()
    {
        var show = new ShowSettings
        {
            Trigger = "events", Events = ["leaderLap", "flag"], DurationSeconds = 5,
            Pieces = new() { ["caption"] = new ShowSettings { Enabled = false } },
        };
        var p = ProfileFactory.CreateDefault("x", "f1-1993", 1920, 1080);
        p = p.WithWidget(p.Get("board")! with { Show = show }).Normalized();
        var json = JsonSerializer.Serialize(p, ProfileStore.Json);
        Assert.Contains("\"show\"", json);
        Assert.Contains("\"leaderLap\"", json);
        var back = JsonSerializer.Deserialize<Profile>(json, ProfileStore.Json)!.Normalized().Get("board")!.Show!;
        Assert.Equal("events", back.Trigger);
        Assert.Equal(["leaderLap", "flag"], back.Events);
        Assert.False(back.Pieces!["caption"].Enabled);

        var patched = new WidgetPatch { Show = show }.ApplyTo(W("board"));
        Assert.Equal(5, patched.Show!.DurationSeconds);
        Assert.Null(new WidgetPatch { Show = new ShowSettings() }.ApplyTo(patched).Show);   // vazio = restaurar padrao
        Assert.Equal(5, new WidgetPatch { Scale = 1 }.ApplyTo(patched).Show!.DurationSeconds);
        var msg = IpcProtocol.TryParse(IpcProtocol.Serialize(new IpcMessage { Patch = new WidgetPatch { Show = show } }))!;
        Assert.Equal("events", msg.Patch!.Show!.Trigger);
    }

    [Fact]
    public void Envelope_matches_the_broadcast_fade()
    {
        Assert.Equal(0f, ShowPlan.Envelope(-0.1, 6, .3, .6));
        Assert.Equal(.5f, ShowPlan.Envelope(.15, 6, .3, .6), 3);
        Assert.Equal(1f, ShowPlan.Envelope(3, 6, .3, .6));
        Assert.Equal(.5f, ShowPlan.Envelope(5.7, 6, .3, .6), 3);
        Assert.Equal(0f, ShowPlan.Envelope(6, 6, .3, .6));
        Assert.Equal(1f, ShowPlan.Envelope(0, 6, 0, 0));
    }

    [Fact]
    public void Gate_alpha_follows_the_trigger()
    {
        double? Never(string _) => null;
        Assert.Equal(1f, ShowPlan.For(W("pitstops"), "f1-1998").GateAlpha(100, Never));
        Assert.Equal(1f, ShowPlan.For(W("pitstops", new ShowSettings { Trigger = "always" }), "f1-1998").GateAlpha(100, Never));

        var events = ShowPlan.For(W("pitstops", new ShowSettings { Trigger = "events", Events = ["flag", "leaderLap"], DurationSeconds = 5, FadeInSeconds = 0, FadeOutSeconds = 0 }), "f1-1998");
        Assert.Equal(0f, events.GateAlpha(100, Never));
        Assert.Equal(1f, events.GateAlpha(100, id => id == "flag" ? 97 : null));
        Assert.Equal(0f, events.GateAlpha(100, id => id == "flag" ? 94 : null));
        Assert.Equal(0f, events.GateAlpha(100, id => id == "pit" ? 99 : null));   // evento nao escolhido
        Assert.Equal(1f, events.GateAlpha(100, id => id == "flag" ? 50 : id == "leaderLap" ? 99 : null));

        var periodic = ShowPlan.For(W("pitstops", new ShowSettings { Trigger = "periodic", PeriodSeconds = 30, DurationSeconds = 10, FadeInSeconds = 0, FadeOutSeconds = 0 }), "f1-1998");
        Assert.Equal(1f, periodic.GateAlpha(65, Never));
        Assert.Equal(0f, periodic.GateAlpha(75, Never));
        Assert.True(periodic.ContentAlways);
    }

    [Fact]
    public void Generic_trigger_forces_the_widget_always_on_so_only_the_gate_decides()
    {
        var native = W("pitstops") with { Columns = ["pos"] };
        Assert.Same(native, ShowPlan.ContentSettings(native, "f1-2018"));
        var gated = native with { Show = new ShowSettings { Trigger = "periodic" } };
        Assert.True(ShowPlan.ContentSettings(gated, "f1-2018").ColumnVisible("always"));
        Assert.True(ShowPlan.ContentSettings(gated with { Columns = null }, "f1-2018").ColumnVisible("always"));
    }

    [Fact]
    public void Board_pieces_and_list_defaults_per_theme()
    {
        Assert.Equal(["pit", "fastest", "gap", "lapsToGo", "caption"], ShowCatalog.For("f1-1993", "board").Pieces.Select(p => p.Id));
        Assert.Equal(["result", "fastest", "split", "live", "caption"], ShowCatalog.For("f1-1993", "qualiboard").Pieces.Select(p => p.Id));
        Assert.Equal(["result", "split"], ShowCatalog.For("f1-1998", "qualiboard").Pieces.Select(p => p.Id));
        var t18 = ShowPlan.For(W("qualitower"), "f1-2018");
        Assert.Equal((ShowForms.Dynamic, 10, 3), (t18.Form, t18.Rows, t18.Around));
        var t93 = ShowPlan.For(W("standings", options: new() { ["rows"] = "8" }), "f1-1993");
        Assert.Equal((ShowForms.Static, 8), (t93.Form, t93.Rows));
        Assert.False(t93.Dynamic);
    }
}
