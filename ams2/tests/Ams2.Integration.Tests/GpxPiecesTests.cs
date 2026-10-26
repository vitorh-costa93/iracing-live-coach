using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Widgets.GpxSat;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

/// <summary>GPxSAT em pecas: cada tela do GPxPatch pertence a uma peca da Exibicao (liga/desliga, gatilho, janela propria).</summary>
public class GpxPiecesTests
{
    static readonly Dictionary<string, string> Expected = new()
    {
        ["time"] = "time", ["fl"] = "sky", ["out"] = "sky", ["dt"] = "sky", ["ride"] = "sky", ["win"] = "sky", ["pit"] = "sky",
        ["pitt"] = "pittimer", ["card"] = "split", ["split"] = "split", ["splitgreen"] = "split", ["gapa"] = "split", ["gapb"] = "split",
        ["trap"] = "split", ["str"] = "standings", ["stq"] = "standings", ["stp"] = "standings", ["sti"] = "standings",
        ["cap"] = "caption", ["bottom"] = "tower", ["progression"] = "progression",
    };

    static Ams2.OverlayHost.Data.OverlayModel Model() => LayoutPreview.ForWidget("lapcounter") with { Inputs = null };

    static IReadOnlyList<(GpxScreen Screen, float Alpha)> Screens(string theme, WidgetSettings cfg)
    {
        var w = new GpxSatWidget();
        w.UseTheme(Ams2.OverlayHost.Theme.Themes.Get(theme));
        w.Configure(cfg);
        return w.Screens(Model());
    }

    static WidgetSettings Preview(string theme, string screen, ShowSettings? show = null, Dictionary<string, string>? extra = null)
    {
        // previewScreen e as opcoes de janela de peca sao de execucao (fora do catalogo): entram depois do Normalized, como no host.
        var cfg = new WidgetSettings { Id = "gpxsat", Show = show }.Normalized(theme);
        var options = new Dictionary<string, string>(cfg.Options ?? [], StringComparer.OrdinalIgnoreCase) { ["previewScreen"] = screen };
        foreach (var (k, v) in extra ?? []) options[k] = v;
        return cfg with { Options = options };
    }

    [Theory]
    [InlineData("f1-1998")]
    [InlineData("f1-1999-digital")]
    [InlineData("f1-2004")]
    [InlineData("f1-2012")]
    public void Every_screen_of_the_mode_belongs_to_a_piece_of_the_theme(string theme)
    {
        var pieces = ShowCatalog.For(theme, "gpxsat").Pieces.Select(p => p.Id).ToHashSet();
        int seen = 0;
        foreach (var (id, piece) in Expected)
        {
            if (Screens(theme, Preview(theme, id)) is not [var only]) continue;
            seen++;
            string? actual = GpxPieces.Of(only.Screen, Model().Session!.Cars.FirstOrDefault(c => c.IsPlayer)?.Index);
            // "pit" na previa usa o 3o colocado; se ele for o carro em foco, o GPxPatch mostra o cronometro do jogador.
            Assert.Equal((id, piece), (id, id == "pit" && actual == "pittimer" ? "sky" : actual));
            Assert.Contains(actual!, pieces);
            Assert.Equal(1f, only.Alpha);
        }
        Assert.True(seen >= 15, $"so {seen} telas de previa");
    }

    [Theory]
    [InlineData("f1-1998")]
    [InlineData("f1-1999-digital")]
    [InlineData("f1-2004")]
    [InlineData("f1-2012")]
    public void Default_profile_dispatches_exactly_the_gpxpatch_screens(string theme)
    {
        foreach (var id in Expected.Keys)
        {
            var plain = Screens(theme, Preview(theme, id));
            var empty = Screens(theme, Preview(theme, id, new ShowSettings()));
            Assert.Equal(plain.Select(s => (s.Screen.Slot, s.Screen.Key, s.Alpha)), empty.Select(s => (s.Screen.Slot, s.Screen.Key, s.Alpha)));
            Assert.All(plain, s => Assert.Equal(1f, s.Alpha));
        }
    }

    [Theory]
    [InlineData("f1-1998")]
    [InlineData("f1-1999-digital")]
    [InlineData("f1-2004")]
    [InlineData("f1-2012")]
    public void Disabled_piece_or_other_piece_window_hides_the_screen(string theme)
    {
        var off = new ShowSettings { Pieces = new() { ["time"] = new ShowSettings { Enabled = false } } };
        Assert.Empty(Screens(theme, Preview(theme, "time", off)));
        Assert.Single(Screens(theme, Preview(theme, "str", off)));
        Assert.Empty(Screens(theme, Preview(theme, "time", extra: new() { [RenderSettings.PieceSoloOption] = "sky" })));
        Assert.Single(Screens(theme, Preview(theme, "time", extra: new() { [RenderSettings.PieceSoloOption] = "time" })));
        Assert.Empty(Screens(theme, Preview(theme, "time", extra: new() { [RenderSettings.PiecesDetachedOption] = "time" })));
    }

    [Theory]
    [InlineData("f1-1998")]
    [InlineData("f1-1999-digital")]
    [InlineData("f1-2004")]
    [InlineData("f1-2012")]
    public void Piece_with_event_trigger_waits_for_its_event(string theme)
    {
        // A previa de layout termina a corrida: o vencedor (sky) fica na tela; com gatilho por bandeira e nenhum evento, sai.
        var cfg = new WidgetSettings { Id = "gpxsat" }.Normalized(theme);
        Assert.Contains(Screens(theme, cfg), s => GpxPieces.Of(s.Screen, null) == "sky");
        var gated = cfg with { Show = new ShowSettings { Pieces = new() { ["sky"] = new ShowSettings { Trigger = ShowTriggers.Events, Events = [ShowEvents.Flag] } } } };
        Assert.DoesNotContain(Screens(theme, gated), s => GpxPieces.Of(s.Screen, null) == "sky");
    }
}
