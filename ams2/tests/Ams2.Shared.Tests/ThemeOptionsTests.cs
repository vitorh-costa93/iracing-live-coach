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

    [Fact]
    public void Winner_2018_options_style_and_show_for()
    {
        var defs = WidgetCatalog.OptionsFor(T2018, "winner");
        Assert.Equal(["style", "showFor"], defs.Select(d => d.Id));
        var st = defs[0];
        Assert.Equal((OptionKind.Choice, "banner"), (st.Kind, st.Default));
        Assert.Equal(["banner", "podium", "both"], st.Choices!.Select(c => c.Value));
        var secs = defs[1];
        Assert.Equal((OptionKind.Number, "12", 5.0, 30.0), (secs.Kind, secs.Default, secs.Min, secs.Max));
        Assert.All(defs, d => Assert.False(string.IsNullOrWhiteSpace(d.Label)));
        Assert.All(st.Choices!, c => Assert.False(string.IsNullOrWhiteSpace(c.Label)));
        Assert.Empty(WidgetCatalog.OptionsFor("f1-1998", "winner"));
        Assert.Empty(WidgetCatalog.OptionsFor("f1-2004", "winner"));
        // As colunas existentes continuam valendo (sempre visivel, equipe, estatisticas).
        Assert.Equal(["always", "team", "stats"], WidgetCatalog.Find("winner")!.Columns.Select(c => c.Id));

        var s = new WidgetSettings { Id = "winner", Options = new() { ["style"] = "Podium", ["showFor"] = "99" } }.Normalized(T2018);
        Assert.Equal(new Dictionary<string, string> { ["style"] = "podium", ["showFor"] = "30" }, s.Options);
        Assert.Equal("5", new WidgetSettings { Id = "winner", Options = new() { ["showFor"] = "1" } }.Normalized(T2018).Option("showFor"));
        Assert.Null(new WidgetSettings { Id = "winner", Options = new() { ["style"] = "banner", ["showFor"] = "12" } }.Normalized(T2018).Options);
        Assert.Null(new WidgetSettings { Id = "winner", Options = new() { ["style"] = "xyz" } }.Normalized(T2018).Options);
        Assert.Null(new WidgetSettings { Id = "winner", Options = new() { ["style"] = "both" } }.Normalized("f1-2004").Options);
        Assert.Equal("banner", new WidgetSettings { Id = "winner" }.OptionOr("style", "banner"));
    }

    [Fact]
    public void Pit_timer_2018_options_pit_time_position_and_tick()
    {
        var defs = WidgetCatalog.OptionsFor(T2018, "pittimer");
        Assert.Equal(["showPitTime", "showPosition", "showTick"], defs.Select(d => d.Id));
        Assert.All(defs, d => Assert.Equal((OptionKind.Toggle, "true"), (d.Kind, d.Default)));
        Assert.All(defs, d => Assert.False(string.IsNullOrWhiteSpace(d.Label)));
        Assert.Empty(WidgetCatalog.OptionsFor("f1-1998", "pittimer"));
        Assert.Empty(WidgetCatalog.OptionsFor("f1-2004", "pittimer"));
        Assert.Contains("always", WidgetCatalog.Find("pittimer")!.Columns.Select(c => c.Id));

        // Desligar grava "false"; o padrao (true) nao e gravado; nos outros temas nada e gravado.
        var off = new WidgetSettings { Id = "pittimer", Options = new() { ["ShowTick"] = "false", ["showPosition"] = "true" } }.Normalized(T2018);
        Assert.Equal(new Dictionary<string, string> { ["showTick"] = "false" }, off.Options);
        Assert.Null(new WidgetSettings { Id = "pittimer", Options = new() { ["showPitTime"] = "true" } }.Normalized(T2018).Options);
        Assert.Null(new WidgetSettings { Id = "pittimer", Options = new() { ["showPitTime"] = "false" } }.Normalized("f1-1998").Options);
        Assert.Equal("true", new WidgetSettings { Id = "pittimer" }.OptionOr("showPitTime", "true"));
    }

    [Fact]
    public void Live_speed_2018_options_units_name_and_always()
    {
        var defs = WidgetCatalog.OptionsFor(T2018, "livespeed");
        Assert.Equal(["units", "showName", "always"], defs.Select(d => d.Id));
        Assert.Equal((OptionKind.Choice, "both"), (defs[0].Kind, defs[0].Default));
        Assert.Equal(["both", "kph", "mph"], defs[0].Choices!.Select(c => c.Value));
        Assert.Equal((OptionKind.Toggle, "true"), (defs[1].Kind, defs[1].Default));
        Assert.Equal((OptionKind.Toggle, "false"), (defs[2].Kind, defs[2].Default));
        Assert.All(defs, d => Assert.False(string.IsNullOrWhiteSpace(d.Label)));
        Assert.All(defs[0].Choices!, c => Assert.False(string.IsNullOrWhiteSpace(c.Label)));
        Assert.Empty(WidgetCatalog.OptionsFor("f1-1998", "livespeed"));
        Assert.Empty(WidgetCatalog.OptionsFor("f1-2004", "livespeed"));

        // Valores canonicos; padroes (both / true / false) nao sao gravados.
        var s = new WidgetSettings { Id = "livespeed", Options = new() { ["Units"] = "MPH", ["showName"] = "False", ["always"] = "true" } }.Normalized(T2018);
        Assert.Equal(new Dictionary<string, string> { ["units"] = "mph", ["showName"] = "false", ["always"] = "true" }, s.Options);
        Assert.Null(new WidgetSettings { Id = "livespeed", Options = new() { ["units"] = "both", ["showName"] = "true", ["always"] = "false" } }.Normalized(T2018).Options);
        Assert.Null(new WidgetSettings { Id = "livespeed", Options = new() { ["units"] = "knots" } }.Normalized(T2018).Options);
        Assert.Equal("both", new WidgetSettings { Id = "livespeed" }.OptionOr("units", "both"));
    }

    [Fact]
    public void Live_speed_exists_only_in_the_2018_theme()
    {
        var def = WidgetCatalog.Find("livespeed")!;
        Assert.Equal("Live Speed", def.DisplayName);
        Assert.True(def.InTheme(T2018));
        Assert.True(def.InTheme("f1-2010s"));
        Assert.False(def.InTheme("f1-1998"));
        Assert.False(def.InTheme("f1-2004"));

        var p18 = ProfileFactory.CreateDefault("x", T2018);
        var ls = p18.Get("livespeed")!;
        Assert.True(ls.Visible);
        Assert.Null(ls.Options);
        foreach (var theme in new[] { "f1-1998", "f1-2004" })
        {
            Assert.Null(ProfileFactory.CreateDefault("x", theme).Get("livespeed"));
            Assert.DoesNotContain("livespeed", WidgetCatalog.ForTheme(theme).Select(d => d.Id));
            Assert.False(WidgetLayout.DesignSizes[theme].ContainsKey("livespeed"));
            // Perfil 1998/2004 que (por engano) traga o widget: a normalizacao o descarta.
            var p = ProfileFactory.CreateDefault("x", theme);
            var extra = p with { Widgets = [.. p.Widgets, new WidgetSettings { Id = "livespeed", Order = 99 }] };
            Assert.DoesNotContain(extra.Normalized().Widgets, w => w.Id == "livespeed");
        }
    }

    [Fact]
    public void Live_speed_2018_sits_on_the_right_clear_of_the_other_widgets()
    {
        var r = WidgetLayout.Rect(T2018, "livespeed")!.Value;
        Assert.Equal((1600.0, 400.0, 195.0, 128.0), r);
        foreach (var other in WidgetCatalog.ForTheme(T2018).Select(d => d.Id).Where(id => id != "livespeed"))
        {
            if (WidgetLayout.Rect(T2018, other) is not { } o) continue;
            bool overlap = r.X < o.X + o.W && o.X < r.X + r.W && r.Y < o.Y + o.H && o.Y < r.Y + r.H;
            Assert.False(overlap, $"livespeed sobrepoe {other} ({o.X},{o.Y} {o.W}x{o.H})");
        }
    }

    [Fact]
    public void Winner_2018_banner_sits_top_center_clear_of_the_tower()
    {
        var w = WidgetLayout.Rect(T2018, "winner")!.Value;
        var tower = WidgetLayout.Rect(T2018, "standings")!.Value;
        Assert.Equal((780.0, 90.0), (w.W, w.H));
        Assert.InRange(w.X + w.W / 2, 940, 980);   // centrado na tela de 1920
        Assert.True(w.Y < 100);
        Assert.True(w.X > tower.X + tower.W, $"banner x={w.X} encosta na torre (ate {tower.X + tower.W})");
    }

    [Fact]
    public void Race_start_2018_options_target_best_showFor_and_always()
    {
        var defs = WidgetCatalog.OptionsFor(T2018, "racestart");
        Assert.Equal(["target", "showBest", "showFor", "always"], defs.Select(d => d.Id));
        Assert.Equal((OptionKind.Choice, "200"), (defs[0].Kind, defs[0].Default));
        Assert.Equal(["100", "200"], defs[0].Choices!.Select(c => c.Value));
        Assert.Equal((OptionKind.Toggle, "true"), (defs[1].Kind, defs[1].Default));
        Assert.Equal((OptionKind.Number, "10", 5, 30), (defs[2].Kind, defs[2].Default, defs[2].Min, defs[2].Max));
        Assert.Equal((OptionKind.Toggle, "false"), (defs[3].Kind, defs[3].Default));
        Assert.All(defs, d => Assert.False(string.IsNullOrWhiteSpace(d.Label)));
        Assert.Empty(WidgetCatalog.OptionsFor("f1-1998", "racestart"));
        Assert.Empty(WidgetCatalog.OptionsFor("f1-2004", "racestart"));

        // Padroes (200 / true / 10 / false) nao sao gravados; invalidos sao descartados.
        Assert.Null(new WidgetSettings { Id = "racestart", Options = new() { ["target"] = "200", ["showBest"] = "true", ["showFor"] = "10", ["always"] = "false" } }.Normalized(T2018).Options);
        Assert.Null(new WidgetSettings { Id = "racestart", Options = new() { ["target"] = "300" } }.Normalized(T2018).Options);
        var s = new WidgetSettings { Id = "racestart", Options = new() { ["target"] = "100", ["showBest"] = "False", ["always"] = "TRUE" } }.Normalized(T2018);
        Assert.Equal(new Dictionary<string, string> { ["target"] = "100", ["showBest"] = "false", ["always"] = "true" }, s.Options);
        var n = new WidgetSettings { Id = "racestart", Options = new() { ["showFor"] = "20" } }.Normalized(T2018);
        Assert.Equal("20", n.OptionOr("showFor", "10"));
        Assert.Equal("200", new WidgetSettings { Id = "racestart" }.OptionOr("target", "200"));
    }

    [Fact]
    public void Race_start_exists_only_in_the_2018_theme()
    {
        var def = WidgetCatalog.Find("racestart")!;
        Assert.Equal("Race Start", def.DisplayName);
        Assert.True(def.InTheme(T2018));
        Assert.False(def.InTheme("f1-1998"));
        Assert.False(def.InTheme("f1-2004"));
        var rs = ProfileFactory.CreateDefault("x", T2018).Get("racestart")!;
        Assert.True(rs.Visible);
        Assert.Null(rs.Options);
        foreach (var theme in new[] { "f1-1998", "f1-2004" })
        {
            Assert.Null(ProfileFactory.CreateDefault("x", theme).Get("racestart"));
            Assert.False(WidgetLayout.DesignSizes[theme].ContainsKey("racestart"));
            var p = ProfileFactory.CreateDefault("x", theme);
            var extra = p with { Widgets = [.. p.Widgets, new WidgetSettings { Id = "racestart", Order = 99 }] };
            Assert.DoesNotContain(extra.Normalized().Widgets, w => w.Id == "racestart");
        }
    }

    [Fact]
    public void Race_start_2018_sits_on_the_right_clear_of_the_other_widgets()
    {
        var r = WidgetLayout.Rect(T2018, "racestart")!.Value;
        Assert.Equal((1600.0, 560.0, 195.0, 190.0), r);
        foreach (var other in WidgetCatalog.ForTheme(T2018).Select(d => d.Id).Where(id => id != "racestart"))
        {
            if (WidgetLayout.Rect(T2018, other) is not { } o) continue;
            bool overlap = r.X < o.X + o.W && o.X < r.X + r.W && r.Y < o.Y + o.H && o.Y < r.Y + r.H;
            Assert.False(overlap, $"racestart sobrepoe {other} ({o.X},{o.Y} {o.W}x{o.H})");
        }
        Assert.True(r.X + r.W <= WidgetLayout.RefWidth && r.Y + r.H <= WidgetLayout.RefHeight);
    }

    [Fact]
    public void Race_control_2018_options_flags_slow_stop_limit_and_showFor()
    {
        var defs = WidgetCatalog.OptionsFor(T2018, "racecontrol");
        Assert.Equal(["showFlags", "showSlowStop", "slowStopLimit", "showFor"], defs.Select(d => d.Id));
        Assert.Equal((OptionKind.Toggle, "true"), (defs[0].Kind, defs[0].Default));
        Assert.Equal((OptionKind.Toggle, "true"), (defs[1].Kind, defs[1].Default));
        Assert.Equal((OptionKind.Number, "5", 3, 30), (defs[2].Kind, defs[2].Default, defs[2].Min, defs[2].Max));
        Assert.Equal((OptionKind.Number, "8", 3, 15), (defs[3].Kind, defs[3].Default, defs[3].Min, defs[3].Max));
        Assert.All(defs, d => Assert.False(string.IsNullOrWhiteSpace(d.Label)));
        Assert.Empty(WidgetCatalog.OptionsFor("f1-1998", "racecontrol"));
        Assert.Empty(WidgetCatalog.OptionsFor("f1-2004", "racecontrol"));

        // Padroes (true / true / 5 / 8) nao sao gravados; invalidos sao descartados.
        Assert.Null(new WidgetSettings { Id = "racecontrol", Options = new() { ["showFlags"] = "true", ["showSlowStop"] = "true", ["slowStopLimit"] = "5", ["showFor"] = "8" } }.Normalized(T2018).Options);
        Assert.Null(new WidgetSettings { Id = "racecontrol", Options = new() { ["showFlags"] = "maybe", ["bogus"] = "1" } }.Normalized(T2018).Options);
        var s = new WidgetSettings { Id = "racecontrol", Options = new() { ["showFlags"] = "False", ["showSlowStop"] = "FALSE" } }.Normalized(T2018);
        Assert.Equal(new Dictionary<string, string> { ["showFlags"] = "false", ["showSlowStop"] = "false" }, s.Options);
        var n = new WidgetSettings { Id = "racecontrol", Options = new() { ["slowStopLimit"] = "12", ["showFor"] = "4" } }.Normalized(T2018);
        Assert.Equal("12", n.OptionOr("slowStopLimit", "5"));
        Assert.Equal("4", n.OptionOr("showFor", "8"));
        Assert.Equal("5", new WidgetSettings { Id = "racecontrol" }.OptionOr("slowStopLimit", "5"));
    }

    [Fact]
    public void Race_control_exists_only_in_the_2018_theme()
    {
        var def = WidgetCatalog.Find("racecontrol")!;
        Assert.Equal("Race Control", def.DisplayName);
        Assert.True(def.DefaultVisible);
        Assert.True(def.InTheme(T2018));
        Assert.False(def.InTheme("f1-1998"));
        Assert.False(def.InTheme("f1-2004"));
        var rc = ProfileFactory.CreateDefault("x", T2018).Get("racecontrol")!;
        Assert.True(rc.Visible);
        Assert.Null(rc.Options);
        foreach (var theme in new[] { "f1-1998", "f1-2004" })
        {
            Assert.Null(ProfileFactory.CreateDefault("x", theme).Get("racecontrol"));
            Assert.False(WidgetLayout.DesignSizes[theme].ContainsKey("racecontrol"));
            var p = ProfileFactory.CreateDefault("x", theme);
            var extra = p with { Widgets = [.. p.Widgets, new WidgetSettings { Id = "racecontrol", Order = 99 }] };
            Assert.DoesNotContain(extra.Normalized().Widgets, w => w.Id == "racecontrol");
        }
    }

    [Fact]
    public void Race_control_2018_sits_above_right_of_the_tower_clear_of_the_other_widgets()
    {
        var r = WidgetLayout.Rect(T2018, "racecontrol")!.Value;
        Assert.Equal((320.0, 40.0, 240.0, 149.0), r);
        var tower = WidgetLayout.Rect(T2018, "standings")!.Value;
        Assert.True(r.X > tower.X + tower.W, $"racecontrol x={r.X} encosta na torre (ate {tower.X + tower.W})");
        Assert.True(r.Y < 100);
        foreach (var other in WidgetCatalog.ForTheme(T2018).Select(d => d.Id).Where(id => id != "racecontrol"))
        {
            if (WidgetLayout.Rect(T2018, other) is not { } o) continue;
            bool overlap = r.X < o.X + o.W && o.X < r.X + r.W && r.Y < o.Y + o.H && o.Y < r.Y + r.H;
            Assert.False(overlap, $"racecontrol sobrepoe {other} ({o.X},{o.Y} {o.W}x{o.H})");
        }
    }

    [Fact]
    public void Quali_tower_2018_options_rows_near_elimination_mode_clock_and_at_risk()
    {
        var defs = WidgetCatalog.OptionsFor(T2018, "qualitower");
        Assert.Equal(["rows", "nearCount", "eliminationFrom", "mode", "showClock", "showAtRisk"], defs.Select(d => d.Id));
        Assert.Equal((OptionKind.Number, "10", 5.0, 20.0), (defs[0].Kind, defs[0].Default, defs[0].Min, defs[0].Max));
        Assert.Equal((OptionKind.Number, "3", 0.0, 10.0), (defs[1].Kind, defs[1].Default, defs[1].Min, defs[1].Max));
        Assert.Equal((OptionKind.Number, "0", 0.0, 30.0), (defs[2].Kind, defs[2].Default, defs[2].Min, defs[2].Max));
        Assert.Equal((OptionKind.Choice, "time"), (defs[3].Kind, defs[3].Default));
        Assert.Equal(["time", "fastesttyre"], defs[3].Choices!.Select(c => c.Value));
        Assert.Equal((OptionKind.Toggle, "true"), (defs[4].Kind, defs[4].Default));
        Assert.Equal((OptionKind.Toggle, "true"), (defs[5].Kind, defs[5].Default));
        Assert.All(defs, d => Assert.False(string.IsNullOrWhiteSpace(d.Label)));

        // Padroes nao sao gravados; numeros limitados; escolha e toggles canonicos; invalidos descartados.
        Assert.Null(new WidgetSettings { Id = "qualitower", Options = new() { ["rows"] = "10", ["nearCount"] = "3", ["eliminationFrom"] = "0", ["mode"] = "time", ["showClock"] = "true", ["showAtRisk"] = "true" } }.Normalized(T2018).Options);
        var s = new WidgetSettings { Id = "qualitower", Options = new() { ["rows"] = "2", ["nearCount"] = "50", ["eliminationFrom"] = "16", ["mode"] = "FastestTyre", ["showClock"] = "False", ["showAtRisk"] = "no", ["bogus"] = "1" } }.Normalized(T2018);
        Assert.Equal(new Dictionary<string, string> { ["rows"] = "5", ["nearCount"] = "10", ["eliminationFrom"] = "16", ["mode"] = "fastesttyre", ["showClock"] = "false" }, s.Options);
        Assert.Equal("30", new WidgetSettings { Id = "qualitower", Options = new() { ["eliminationFrom"] = "99" } }.Normalized(T2018).Option("eliminationFrom"));
        Assert.Null(new WidgetSettings { Id = "qualitower", Options = new() { ["mode"] = "gap" } }.Normalized(T2018).Options);
        // Opcoes so do 2018 (modo, cartao) sao descartadas nos outros temas.
        Assert.Null(new WidgetSettings { Id = "qualitower", Options = new() { ["mode"] = "fastesttyre", ["showAtRisk"] = "false" } }.Normalized("f1-1998").Options);
        Assert.Null(new WidgetSettings { Id = "qualitower", Options = new() { ["mode"] = "fastesttyre", ["columns"] = "1" } }.Normalized("f1-2004").Options);
    }

    [Fact]
    public void Quali_tower_2004_options_rows_near_elimination_and_clock()
    {
        var defs = WidgetCatalog.OptionsFor("f1-2004", "qualitower");
        Assert.Equal(["rows", "nearCount", "eliminationFrom", "showClock"], defs.Select(d => d.Id));
        Assert.Equal((OptionKind.Number, "5", 1.0, 20.0), (defs[0].Kind, defs[0].Default, defs[0].Min, defs[0].Max));
        Assert.Equal((OptionKind.Number, "3", 0.0, 10.0), (defs[1].Kind, defs[1].Default, defs[1].Min, defs[1].Max));
        Assert.Equal((OptionKind.Number, "0", 0.0, 30.0), (defs[2].Kind, defs[2].Default, defs[2].Min, defs[2].Max));
        Assert.Equal((OptionKind.Toggle, "true"), (defs[3].Kind, defs[3].Default));
        Assert.All(defs, d => Assert.False(string.IsNullOrWhiteSpace(d.Label)));
        Assert.Null(new WidgetSettings { Id = "qualitower", Options = new() { ["rows"] = "5", ["nearCount"] = "3", ["eliminationFrom"] = "0", ["showClock"] = "true" } }.Normalized("f1-2004").Options);
        var s = new WidgetSettings { Id = "qualitower", Options = new() { ["rows"] = "0", ["nearCount"] = "50", ["eliminationFrom"] = "16", ["showClock"] = "False" } }.Normalized("f1-2004");
        Assert.Equal(new Dictionary<string, string> { ["rows"] = "1", ["nearCount"] = "10", ["eliminationFrom"] = "16", ["showClock"] = "false" }, s.Options);
    }

    [Fact]
    public void Quali_tower_1998_options_rows_columns_elimination_and_clock()
    {
        var defs = WidgetCatalog.OptionsFor("f1-1998", "qualitower");
        Assert.Equal(["rows", "columns", "eliminationFrom", "showClock"], defs.Select(d => d.Id));
        Assert.Equal((OptionKind.Number, "6", 2.0, 10.0), (defs[0].Kind, defs[0].Default, defs[0].Min, defs[0].Max));
        Assert.Equal((OptionKind.Choice, "2"), (defs[1].Kind, defs[1].Default));
        Assert.Equal(["2", "1"], defs[1].Choices!.Select(c => c.Value));
        Assert.Equal((OptionKind.Number, "0", 0.0, 30.0), (defs[2].Kind, defs[2].Default, defs[2].Min, defs[2].Max));
        Assert.Equal((OptionKind.Toggle, "true"), (defs[3].Kind, defs[3].Default));
        Assert.All(defs, d => Assert.False(string.IsNullOrWhiteSpace(d.Label)));
        Assert.Null(new WidgetSettings { Id = "qualitower", Options = new() { ["rows"] = "6", ["columns"] = "2", ["eliminationFrom"] = "0", ["showClock"] = "true" } }.Normalized("f1-1998").Options);
        var s = new WidgetSettings { Id = "qualitower", Options = new() { ["rows"] = "40", ["columns"] = "1", ["eliminationFrom"] = "9", ["nearCount"] = "4" } }.Normalized("f1-1998");
        Assert.Equal(new Dictionary<string, string> { ["rows"] = "10", ["columns"] = "1", ["eliminationFrom"] = "9" }, s.Options);
        Assert.Null(new WidgetSettings { Id = "qualitower", Options = new() { ["columns"] = "3" } }.Normalized("f1-1998").Options);
    }

    [Theory]
    [InlineData("f1-2004", 400f, 314f)]
    [InlineData("f1-1998", 834f, 182f)]
    public void Quali_tower_04_98_take_the_tower_corner_and_clear_the_widgets_visible_in_qualifying(string theme, float w, float h)
    {
        var q = WidgetLayout.Rect(theme, "qualitower")!.Value;
        var tower = WidgetLayout.Rect(theme, "standings")!.Value;
        Assert.Equal((tower.X, tower.Y), (q.X, q.Y));
        Assert.Equal((w, h), WidgetLayout.DesignSizes[theme]["qualitower"]);
        var profile = ProfileFactory.CreateDefault("x", theme, WidgetLayout.RefWidth, WidgetLayout.RefHeight);
        foreach (var x in profile.Widgets.Where(x => x.Visible && x.Id is not ("qualitower" or "standings")))
        {
            if (!WidgetCatalog.Find(x.Id)!.Sessions.Contains(SessionIds.Qualify)) continue;
            if (WidgetLayout.Rect(theme, x.Id) is not { } o) continue;
            bool overlap = q.X < o.X + o.W && o.X < q.X + q.W && q.Y < o.Y + o.H && o.Y < q.Y + q.H;
            Assert.False(overlap, $"qualitower ({q.X},{q.Y} {q.W}x{q.H}) sobrepoe {x.Id} ({o.X},{o.Y} {o.W}x{o.H})");
        }
    }

    [Fact]
    public void Quali_tower_2018_takes_the_tower_corner_and_clears_the_widgets_visible_in_qualifying()
    {
        var q = WidgetLayout.Rect(T2018, "qualitower")!.Value;
        var tower = WidgetLayout.Rect(T2018, "standings")!.Value;
        Assert.Equal((tower.X, tower.Y), (q.X, q.Y));
        Assert.Equal((284f, 626f), WidgetLayout.DesignSizes[T2018]["qualitower"]);
        var profile = ProfileFactory.CreateDefault("x", T2018, WidgetLayout.RefWidth, WidgetLayout.RefHeight);
        foreach (var w in profile.Widgets.Where(w => w.Visible && w.Id is not ("qualitower" or "standings")))
        {
            if (!WidgetCatalog.Find(w.Id)!.Sessions.Contains(SessionIds.Qualify)) continue;
            if (WidgetLayout.Rect(T2018, w.Id) is not { } o) continue;
            bool overlap = q.X < o.X + o.W && o.X < q.X + q.W && q.Y < o.Y + o.H && o.Y < q.Y + q.H;
            Assert.False(overlap, $"qualitower ({q.X},{q.Y} {q.W}x{q.H}) sobrepoe {w.Id} ({o.X},{o.Y} {o.W}x{o.H})");
        }
    }
    [Theory]
    [InlineData("f1-2018", new[] { "compareTo", "showSectors", "showSectorPanel", "showFor", "always" })]
    [InlineData("f1-2004", new[] { "compareTo", "showSectors", "showFor", "always" })]
    [InlineData("f1-1998", new[] { "compareTo", "showSpeed", "showFor", "always" })]
    public void Quali_lap_options_per_theme(string theme, string[] ids)
    {
        var defs = WidgetCatalog.OptionsFor(theme, "qualilap");
        Assert.Equal(ids, defs.Select(d => d.Id));
        var cmp = defs.Single(d => d.Id == "compareTo");
        Assert.Equal((OptionKind.Choice, "leader"), (cmp.Kind, cmp.Default));
        Assert.Equal(["leader", "personal"], cmp.Choices!.Select(c => c.Value));
        var showFor = defs.Single(d => d.Id == "showFor");
        Assert.Equal((OptionKind.Number, "6", 3.0, 15.0), (showFor.Kind, showFor.Default, showFor.Min, showFor.Max));
        Assert.Equal((OptionKind.Toggle, "false"), (defs.Single(d => d.Id == "always").Kind, defs.Single(d => d.Id == "always").Default));
        foreach (var d in defs.Where(d => d.Id is "showSectors" or "showSectorPanel" or "showSpeed")) Assert.Equal((OptionKind.Toggle, "true"), (d.Kind, d.Default));
        Assert.All(defs, d => Assert.False(string.IsNullOrWhiteSpace(d.Label)));

        // Padroes nao sao gravados; escolha canonica, numero limitado, invalidos e opcoes de outros temas descartados.
        var defaults = defs.ToDictionary(d => d.Id, d => d.Default);
        Assert.Null(new WidgetSettings { Id = "qualilap", Options = defaults }.Normalized(theme).Options);
        var s = new WidgetSettings { Id = "qualilap", Options = new() { ["compareTo"] = "Personal", ["showFor"] = "99", ["always"] = "True", ["bogus"] = "1", ["eliminationFrom"] = "5" } }.Normalized(theme);
        Assert.Equal(new Dictionary<string, string> { ["compareTo"] = "personal", ["showFor"] = "15", ["always"] = "true" }, s.Options);
        Assert.Equal("3", new WidgetSettings { Id = "qualilap", Options = new() { ["showFor"] = "1" } }.Normalized(theme).Option("showFor"));
        Assert.Null(new WidgetSettings { Id = "qualilap", Options = new() { ["compareTo"] = "rival" } }.Normalized(theme).Options);
        if (theme != T2018) Assert.Null(new WidgetSettings { Id = "qualilap", Options = new() { ["showSectorPanel"] = "false" } }.Normalized(theme).Options);
        if (theme == "f1-1998") Assert.Null(new WidgetSettings { Id = "qualilap", Options = new() { ["showSectors"] = "false" } }.Normalized(theme).Options);
        else Assert.Null(new WidgetSettings { Id = "qualilap", Options = new() { ["showSpeed"] = "false" } }.Normalized(theme).Options);
    }

    [Theory]
    [InlineData("f1-2018", 664f, 152f)]
    [InlineData("f1-2004", 278f, 126f)]
    [InlineData("f1-1998", 640f, 124f)]
    public void Quali_lap_sits_bottom_center_and_clears_the_widgets_visible_in_qualifying(string theme, float w, float h)
    {
        Assert.Equal((w, h), WidgetLayout.DesignSizes[theme]["qualilap"]);
        var q = WidgetLayout.Rect(theme, "qualilap")!.Value;
        // Embaixo ao centro: metade de baixo da tela, centro horizontal perto do meio, dentro da tela.
        Assert.True(q.Y > WidgetLayout.RefHeight / 2 && q.Y + q.H <= WidgetLayout.RefHeight - 20, $"y={q.Y} h={q.H}");
        Assert.True(Math.Abs(q.X + q.W / 2 - WidgetLayout.RefWidth / 2) < 60, $"centro x={q.X + q.W / 2}");
        var profile = ProfileFactory.CreateDefault("x", theme, WidgetLayout.RefWidth, WidgetLayout.RefHeight);
        foreach (var x in profile.Widgets.Where(x => x.Visible && x.Id != "qualilap"))
        {
            if (WidgetLayout.ExclusiveGroups.Any(g => g.Contains("qualilap") && g.Contains(x.Id))) continue;
            if (!WidgetCatalog.Find(x.Id)!.Sessions.Contains(SessionIds.Qualify)) continue;
            if (WidgetLayout.Rect(theme, x.Id) is not { } o) continue;
            bool overlap = q.X < o.X + o.W && o.X < q.X + q.W && q.Y < o.Y + o.H && o.Y < q.Y + q.H;
            Assert.False(overlap, $"qualilap ({q.X},{q.Y} {q.W}x{q.H}) sobrepoe {x.Id} ({o.X},{o.Y} {o.W}x{o.H})");
        }
    }
}
