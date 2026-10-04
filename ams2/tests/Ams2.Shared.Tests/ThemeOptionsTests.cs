using System.Text.Json;
using Ams2.Shared.Ipc;
using Ams2.Shared.Profiles;

namespace Ams2.Shared.Tests;

/// <summary>Opcoes proprias por tema (WidgetSettings.Options) e widgets exclusivos de tema (WidgetDef.Themes).</summary>
public class ThemeOptionsTests
{
    const string T2018 = "f1-2018";

    [Fact]
    public void Standings_2018_has_mode_option_and_other_themes_have_none()
    {
        var defs = WidgetCatalog.OptionsFor(T2018, "standings");
        var o = defs[0];
        Assert.Equal(("mode", OptionKind.Choice, "gap"), (o.Id, o.Kind, o.Default));
        Assert.Equal(["gap", "interval", "gainedlost", "pitstops", "bestlap", "auto"], o.Choices!.Select(c => c.Value));
        Assert.Equal(5, WidgetCatalog.OptionsFor("f1-2010s", "standings").Count);   // id antigo do tema
        Assert.Empty(WidgetCatalog.OptionsFor("f1-1998", "standings"));
        Assert.Empty(WidgetCatalog.OptionsFor(T2018, "fuel"));
        Assert.Empty(WidgetCatalog.OptionsFor("nao-existe", "standings"));
    }

    [Fact]
    public void Standings_2018_options_mode_seconds_battle_full_names_and_out_block()
    {
        var defs = WidgetCatalog.OptionsFor(T2018, "standings");
        Assert.Equal(["mode", "modeSeconds", "battle", "fullNames", "outBlock"], defs.Select(d => d.Id));
        var secs = defs.Single(d => d.Id == "modeSeconds");
        Assert.Equal((OptionKind.Number, "10", 5.0, 30.0), (secs.Kind, secs.Default, secs.Min, secs.Max));
        foreach (var id in new[] { "battle", "fullNames", "outBlock" })
        {
            var d = defs.Single(x => x.Id == id);
            Assert.Equal((OptionKind.Toggle, "true"), (d.Kind, d.Default));
        }
        Assert.All(defs, d => Assert.False(string.IsNullOrWhiteSpace(d.Label)));

        // Valores: numero limitado a 5..30, toggles canonicos, padroes nao sao gravados.
        var s = new WidgetSettings
        {
            Id = "standings",
            Options = new() { ["modeSeconds"] = "99", ["battle"] = "False", ["fullNames"] = "true", ["outBlock"] = "sim" },
        }.Normalized(T2018);
        Assert.Equal(new Dictionary<string, string> { ["modeSeconds"] = "30", ["battle"] = "false" }, s.Options);
        Assert.Equal("5", new WidgetSettings { Id = "standings", Options = new() { ["modeSeconds"] = "1" } }.Normalized(T2018).Option("modeSeconds"));
        Assert.Null(new WidgetSettings { Id = "standings", Options = new() { ["modeSeconds"] = "10" } }.Normalized(T2018).Options);
        Assert.Equal("true", new WidgetSettings { Id = "standings" }.OptionOr("outBlock", "true"));
    }

    [Fact]
    public void Normalized_with_theme_drops_unknown_keys_invalid_values_and_defaults()
    {
        var s = new WidgetSettings { Id = "standings", Options = new() { ["MODE"] = "Interval", ["bogus"] = "1" } }.Normalized(T2018);
        Assert.Equal(new Dictionary<string, string> { ["mode"] = "interval" }, s.Options);
        Assert.Equal("interval", s.Option("mode"));
        Assert.Equal("interval", s.OptionOr("Mode", "gap"));

        Assert.Null(new WidgetSettings { Id = "standings", Options = new() { ["mode"] = "xyz" } }.Normalized(T2018).Options);
        Assert.Null(new WidgetSettings { Id = "standings", Options = new() { ["mode"] = "gap" } }.Normalized(T2018).Options);   // padrao: sem bloco
        Assert.Null(new WidgetSettings { Id = "standings", Options = [] }.Normalized(T2018).Options);
        // tema sem a opcao: tudo sai
        Assert.Null(new WidgetSettings { Id = "standings", Options = new() { ["mode"] = "interval" } }.Normalized("f1-1998").Options);
        Assert.Equal("gap", new WidgetSettings { Id = "standings" }.OptionOr("mode", "gap"));
        Assert.Null(new WidgetSettings { Id = "standings" }.Option("mode"));
    }

    [Fact]
    public void Normalized_without_theme_keeps_options_but_drops_empty_entries()
    {
        var s = new WidgetSettings { Id = "standings", Options = new() { ["mode"] = "interval", ["x"] = "", [" "] = "1" } }.Normalized();
        Assert.Equal(new Dictionary<string, string> { ["mode"] = "interval" }, s.Options);
    }

    [Fact]
    public void OptionDef_normalizes_toggle_and_number()
    {
        var t = new OptionDef("on", "Ligado", OptionKind.Toggle, null, "false");
        Assert.Equal("true", t.Normalize("True"));
        Assert.Null(t.Normalize("sim"));
        var n = new OptionDef("n", "Numero", OptionKind.Number, null, "5", Min: 1, Max: 10);
        Assert.Equal("10", n.Normalize("99"));
        Assert.Equal("1", n.Normalize("-3"));
        Assert.Equal("2.5", n.Normalize("2.5"));
        Assert.Null(n.Normalize("abc"));
        Assert.Null(n.Normalize("NaN"));
    }

    [Fact]
    public void Patch_replaces_options_map_and_empty_map_resets()
    {
        var s = ProfileFactory.CreateDefault("x", T2018).Get("standings")!;
        Assert.Null(s.Options);
        var r = new WidgetPatch { Options = new() { ["mode"] = "bestlap" } }.ApplyTo(s, T2018);
        Assert.Equal("bestlap", r.Option("mode"));
        r = new WidgetPatch { Scale = 1.2f }.ApplyTo(r, T2018);   // patch sem Options mantem o mapa
        Assert.Equal("bestlap", r.Option("mode"));
        r = new WidgetPatch { Options = new() { ["mode"] = "bogus" } }.ApplyTo(r, T2018);
        Assert.Null(r.Options);
        r = new WidgetPatch { Options = new() { ["mode"] = "auto" } }.ApplyTo(r, T2018);
        r = new WidgetPatch { Options = [] }.ApplyTo(r, T2018);
        Assert.Null(r.Options);

        var m = WidgetPatch.Merge(new WidgetPatch { Options = new() { ["mode"] = "gap" } }, new WidgetPatch { Scale = 2f });
        Assert.Equal("gap", m.Options!["mode"]);
    }

    [Fact]
    public void Options_survive_ipc_serialization()
    {
        var msg = new IpcMessage { Cmd = IpcCommands.SetWidget, Widget = "standings", Patch = new WidgetPatch { Options = new() { ["mode"] = "interval" } } };
        var back = IpcProtocol.TryParse(IpcProtocol.Serialize(msg))!;
        Assert.Equal("interval", back.Patch!.Options!["mode"]);
        Assert.DoesNotContain("options", IpcProtocol.Serialize(new IpcMessage { Patch = new WidgetPatch { Scale = 1f } }));
    }

    [Fact]
    public void Profile_roundtrip_keeps_options_and_default_profile_json_has_no_options_block()
    {
        using var t = new TempStore();
        var p = ProfileFactory.CreateDefault("Corrida", T2018);
        t.Store.Save(p);
        var json = File.ReadAllText(Directory.GetFiles(Path.Combine(t.Dir, "profiles", T2018), "*.json").Single());
        Assert.DoesNotContain("\"options\"", json);

        t.Store.Save(p.WithWidget(p.Get("standings")! with { Options = new() { ["mode"] = "gainedlost", ["lixo"] = "1" } }));
        var back = t.Store.Load(T2018, "Corrida")!;
        Assert.Equal(new Dictionary<string, string> { ["mode"] = "gainedlost" }, back.Get("standings")!.Options);
        json = File.ReadAllText(Directory.GetFiles(Path.Combine(t.Dir, "profiles", T2018), "*.json").Single());
        Assert.Contains("\"options\"", json);
        Assert.DoesNotContain("lixo", json);
    }

    [Fact]
    public void Old_profile_without_options_loads_unchanged()
    {
        using var t = new TempStore();
        var p = ProfileFactory.CreateDefault("Antigo", T2018);
        var json = JsonSerializer.Serialize(p, ProfileStore.Json);
        Assert.DoesNotContain("options", json, StringComparison.OrdinalIgnoreCase);
        var dir = Path.Combine(t.Dir, "profiles", T2018);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Antigo.json"), json);
        var back = t.Store.Load(T2018, "Antigo")!;
        Assert.All(back.Widgets, w => Assert.Null(w.Options));
        Assert.Equal(JsonSerializer.Serialize(p.Normalized(), ProfileStore.Json), JsonSerializer.Serialize(back, ProfileStore.Json));
        Assert.Equal("gap", back.Get("standings")!.OptionOr("mode", WidgetCatalog.OptionsFor(T2018, "standings")[0].Default));
    }

    [Fact]
    public void Widget_themes_filter()
    {
        var only2018 = new WidgetDef("x", "X", null, null, null, "", [], 0, 0, Themes: ["f1-2018"]);
        Assert.True(only2018.InTheme("f1-2018"));
        Assert.True(only2018.InTheme("F1-2010S"));   // id antigo vira o novo
        Assert.False(only2018.InTheme("f1-1998"));
        Assert.True(WidgetCatalog.Find("standings")!.InTheme("qualquer"));   // Themes nulo = todos

        foreach (var theme in ThemeCatalog.All.Select(t => t.Id))
        {
            var ids = WidgetCatalog.ForTheme(theme).Select(d => d.Id).ToList();
            Assert.Equal(WidgetCatalog.All.Where(d => d.InTheme(theme)).Select(d => d.Id), ids);
            Assert.Equal(ids, ProfileFactory.CreateDefault("x", theme).Ordered.Select(w => w.Id));
            Assert.Equal(ids, (new Profile { Name = "x", ThemeId = theme }).Normalized().Ordered.Select(w => w.Id));
        }
    }

    [Fact]
    public void Profile_normalized_drops_widgets_not_in_theme()
    {
        var p = ProfileFactory.CreateDefault("x", "f1-1998");
        var extra = p with { Widgets = [.. p.Widgets, new WidgetSettings { Id = "nao-existe", Order = 99 }] };
        Assert.DoesNotContain(extra.Normalized().Widgets, w => w.Id == "nao-existe");
    }

    [Fact]
    public void Driver_caption_2018_options_variant_and_show_for()
    {
        var defs = WidgetCatalog.OptionsFor(T2018, "drivercaption");
        Assert.Equal(["variant", "showFor"], defs.Select(d => d.Id));
        var v = defs[0];
        Assert.Equal((OptionKind.Choice, "auto"), (v.Kind, v.Default));
        Assert.Equal(["driver", "startednow", "result", "auto"], v.Choices!.Select(c => c.Value));
        var secs = defs[1];
        Assert.Equal((OptionKind.Number, "6", 3.0, 15.0), (secs.Kind, secs.Default, secs.Min, secs.Max));
        Assert.All(defs, d => Assert.False(string.IsNullOrWhiteSpace(d.Label)));
        Assert.All(v.Choices!, c => Assert.False(string.IsNullOrWhiteSpace(c.Label)));
        Assert.Empty(WidgetCatalog.OptionsFor("f1-1998", "drivercaption"));
        Assert.Empty(WidgetCatalog.OptionsFor("f1-2004", "drivercaption"));
        // As colunas continuam as do catalogo (sempre visivel, equipe, pneus).
        Assert.Equal(["always", "team", "tyre"], WidgetCatalog.Find("drivercaption")!.Columns.Select(c => c.Id));

        // Valores: variante canonica, segundos limitados a 3..15, padroes nao gravados.
        var s = new WidgetSettings { Id = "drivercaption", Options = new() { ["variant"] = "StartedNow", ["showFor"] = "99" } }.Normalized(T2018);
        Assert.Equal(new Dictionary<string, string> { ["variant"] = "startednow", ["showFor"] = "15" }, s.Options);
        Assert.Equal("3", new WidgetSettings { Id = "drivercaption", Options = new() { ["showFor"] = "1" } }.Normalized(T2018).Option("showFor"));
        Assert.Null(new WidgetSettings { Id = "drivercaption", Options = new() { ["variant"] = "auto", ["showFor"] = "6" } }.Normalized(T2018).Options);
        Assert.Null(new WidgetSettings { Id = "drivercaption", Options = new() { ["variant"] = "xyz" } }.Normalized(T2018).Options);
        Assert.Null(new WidgetSettings { Id = "drivercaption", Options = new() { ["variant"] = "result" } }.Normalized("f1-1998").Options);
        Assert.Equal("auto", new WidgetSettings { Id = "drivercaption" }.OptionOr("variant", "auto"));
    }
}
