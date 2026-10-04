using System.Text.Json;
using Ams2.Shared.Ipc;
using Ams2.Shared.Profiles;

namespace Ams2.Shared.Tests;

/// <summary>Filtro de visibilidade por tipo de sessao (WidgetSettings.Sessions) e widgets de classificacao no catalogo.</summary>
public class SessionsTests
{
    static readonly string[] Quali = ["qualitower", "qualilap", "qualiresult"];

    [Fact]
    public void Existing_widgets_default_to_every_session_and_quali_widgets_to_qualify()
    {
        foreach (var d in WidgetCatalog.All)
        {
            var s = new WidgetSettings { Id = d.Id };
            if (Quali.Contains(d.Id))
            {
                Assert.Equal([SessionIds.Qualify], s.EffectiveSessions);
                Assert.True(s.ShowsIn("qualify"));
                Assert.False(s.ShowsIn("race"));
                Assert.False(s.ShowsIn("practice"));
            }
            else Assert.Equal(SessionIds.All, s.EffectiveSessions);
            Assert.True(s.ShowsIn(null));   // sem sessao: nao filtra
        }
    }

    [Fact]
    public void Quali_widgets_exist_in_every_theme_and_are_visible_in_the_default_profile()
    {
        foreach (var t in ThemeCatalog.All)
        {
            var p = ProfileFactory.CreateDefault("P", t.Id);
            foreach (var id in Quali)
            {
                var w = p.Get(id);
                Assert.NotNull(w);
                Assert.True(w!.Visible, $"{t.Id}/{id}");
                Assert.Null(w.Sessions);
            }
        }
        Assert.Contains(WidgetLayout.ExclusiveGroups, g => g.Contains("qualitower") && g.Contains("standings"));
        Assert.Contains(WidgetLayout.ExclusiveGroups, g => g.Contains("qualilap") && g.Contains("board"));
    }

    [Fact]
    public void Normalization_canonicalizes_and_drops_unknown_empty_and_default()
    {
        var s = new WidgetSettings { Id = "fuel", Sessions = ["RACE", "bogus", "practice", "race"] }.Normalized();
        Assert.Equal(["practice", "race"], s.Sessions);
        Assert.False(s.ShowsIn("qualify"));
        Assert.True(s.ShowsIn("Race"));

        Assert.Null(new WidgetSettings { Id = "fuel", Sessions = ["race", "qualify", "practice"] }.Normalized().Sessions);   // = padrao (todas)
        Assert.Null(new WidgetSettings { Id = "qualitower", Sessions = ["qualify"] }.Normalized().Sessions);                // = padrao do widget
        Assert.Null(new WidgetSettings { Id = "fuel", Sessions = [] }.Normalized().Sessions);                               // vazio = padrao
        Assert.Null(new WidgetSettings { Id = "fuel", Sessions = ["nope"] }.Normalized().Sessions);
        Assert.Equal(["practice", "qualify", "race"], new WidgetSettings { Id = "qualitower", Sessions = ["race", "practice", "qualify"] }.Normalized().Sessions);
    }

    [Fact]
    public void Patch_sets_and_empty_patch_restores_the_default()
    {
        var cur = new WidgetSettings { Id = "standings" }.Normalized();
        var next = new WidgetPatch { Sessions = ["race"] }.ApplyTo(cur);
        Assert.Equal(["race"], next.Sessions);
        Assert.Equal(["race"], new WidgetPatch { Scale = 1.2f }.ApplyTo(next).Sessions);   // patch sem o campo nao mexe
        Assert.Null(new WidgetPatch { Sessions = [] }.ApplyTo(next).Sessions);
        Assert.Equal(["qualify"], WidgetPatch.Merge(new WidgetPatch { Sessions = ["race"] }, new WidgetPatch { Sessions = ["qualify"] }).Sessions);
        Assert.Equal(["race"], WidgetPatch.Merge(new WidgetPatch { Sessions = ["race"] }, new WidgetPatch { Scale = 2 }).Sessions);
    }

    [Fact]
    public void Json_has_no_sessions_block_by_default_and_round_trips_custom_values()
    {
        var p = ProfileFactory.CreateDefault("P", "f1-1998").Normalized();
        Assert.DoesNotContain("\"sessions\"", JsonSerializer.Serialize(p, ProfileStore.Json));

        p = p.WithWidget(p.Get("fuel")! with { Sessions = ["practice"] }).Normalized();
        var json = JsonSerializer.Serialize(p, ProfileStore.Json);
        Assert.Contains("\"sessions\"", json);
        var back = JsonSerializer.Deserialize<Profile>(json, ProfileStore.Json)!.Normalized();
        Assert.Equal(["practice"], back.Get("fuel")!.Sessions);

        // Perfil antigo (sem o campo e sem os widgets de classificacao): os widgets existentes continuam em todas as sessoes; os novos entram so na classificacao.
        var old = new Profile { Name = "Velho", ThemeId = "f1-1998", Widgets = [new WidgetSettings { Id = "standings", X = 5 }] }.Normalized();
        Assert.Equal(SessionIds.All, old.Get("standings")!.EffectiveSessions);
        Assert.Equal([SessionIds.Qualify], old.Get("qualitower")!.EffectiveSessions);

        var msg = new IpcMessage { Patch = new WidgetPatch { Sessions = ["race"] }, State = new HostState { Session = "qualify", HiddenBySession = ["fuel"] } };
        var rt = IpcProtocol.TryParse(IpcProtocol.Serialize(msg))!;
        Assert.Equal(["race"], rt.Patch!.Sessions);
        Assert.Equal(("qualify", "fuel"), (rt.State!.Session, rt.State.HiddenBySession.Single()));
        Assert.DoesNotContain("sessions", IpcProtocol.Serialize(new IpcMessage { Patch = new WidgetPatch { Scale = 1f } }));
    }

    [Fact]
    public void Labels_are_portuguese()
        => Assert.Equal(["Treino", "Classificação", "Corrida"], SessionIds.All.Select(SessionIds.Label));
}
