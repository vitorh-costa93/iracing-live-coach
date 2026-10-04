using Ams2.Shared.Profiles;

namespace Ams2.Shared.Tests;

public sealed class TempStore : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "ams2-tests-" + Guid.NewGuid().ToString("N"));
    public ProfileStore Store { get; }
    public TempStore() => Store = new ProfileStore(Dir);
    public void Dispose() { try { Directory.Delete(Dir, true); } catch { } }
}

public class ProfileTests
{
    const string Theme = "f1-1998";

    [Fact]
    public void Default_profile_has_every_catalog_widget_in_order()
    {
        var p = ProfileFactory.CreateDefault("A", Theme);
        Assert.Equal(WidgetCatalog.ForTheme(Theme).Select(w => w.Id), p.Ordered.Select(w => w.Id));
        Assert.All(p.Widgets, w => Assert.Equal(WidgetLayout.Get(Theme, w.Id).Scale, w.Scale));
        // o board substitui Relative e Driver Caption no layout padrao (continuam no catalogo, desligados)
        Assert.All(p.Widgets, w => Assert.Equal(w.Id is not ("relative" or "drivercaption"), w.Visible));
        // composicao da TV: standings no canto superior esquerdo, board embaixo ao centro
        Assert.Equal((32, 24), (p.Get("standings")!.X, p.Get("standings")!.Y));
        Assert.Equal(872, p.Get("board")!.Y);
        Assert.InRange(p.Get("board")!.X, 600, 700);
        Assert.Equal((5, 3), (p.Get("standings")!.TopCount, p.Get("standings")!.NearCount));
        Assert.Null(p.Get("standings")!.Rows);
        Assert.Equal(3, p.Get("relative")!.Rows);
        Assert.Null(p.Get("fuel")!.Rows);
    }

    [Fact]
    public void Default_positions_scale_with_the_screen()
    {
        var a = ProfileFactory.CreateDefault("A", Theme, 1920, 1080).Get("relative")!;
        var b = ProfileFactory.CreateDefault("A", Theme, 3840, 2160).Get("relative")!;
        Assert.Equal(a.X * 2, b.X);
        Assert.Equal(a.Y * 2, b.Y);
    }

    [Fact]
    public void Roundtrip_keeps_all_fields()
    {
        using var t = new TempStore();
        var p = ProfileFactory.CreateDefault("Corrida", Theme);
        var s = p.Get("standings")! with { Visible = false, X = 12, Y = 34, Scale = 1.25f, Opacity = 0.7f, Font = "Segoe UI", TopCount = 7, NearCount = 4, Columns = ["pos", "gap"], Order = 5 };
        t.Store.Save(p.WithWidget(s));
        var back = t.Store.Load(Theme, "Corrida")!;
        var r = back.Get("standings")!;
        Assert.False(r.Visible);
        Assert.Equal((12, 34), (r.X, r.Y));
        Assert.Equal(1.25f, r.Scale); Assert.Equal(0.7f, r.Opacity);
        Assert.Equal("Segoe UI", r.Font); Assert.Equal((7, 4), (r.TopCount, r.NearCount));
        Assert.Equal(["pos", "gap"], r.Columns);
        Assert.Equal(Profile.CurrentSchemaVersion, back.SchemaVersion);
    }

    [Fact]
    public void Normalization_clamps_values_and_drops_unknown_columns()
    {
        var s = new WidgetSettings { Id = "relative", Scale = 99, Opacity = 0, Rows = 50, Columns = ["gap", "bogus", "gap"] }.Normalized();
        Assert.Equal(WidgetCatalog.MaxScale, s.Scale);
        Assert.Equal(WidgetCatalog.MinOpacity, s.Opacity);
        Assert.Equal(4, s.Rows);
        Assert.Equal(["gap"], s.Columns);
        Assert.Null(new WidgetSettings { Id = "fuel", Rows = 5 }.Normalized().Rows);
        // Standings: topo/janela limitados; o jogador nunca fica sem linha
        var st = new WidgetSettings { Id = "standings", TopCount = 99, NearCount = -4 }.Normalized();
        Assert.Equal((WidgetCatalog.MaxTopCount, 0), (st.TopCount, st.NearCount));
        var zero = new WidgetSettings { Id = "standings", TopCount = 0, NearCount = 0 }.Normalized();
        Assert.Equal((0, 1), (zero.TopCount, zero.NearCount));
        Assert.Null(new WidgetSettings { Id = "fuel", TopCount = 4 }.Normalized().TopCount);
    }

    [Fact]
    public void Old_schema_profiles_load_with_default_standings_selection_and_the_new_board_widget()
    {
        using var t = new TempStore();
        var dir = Path.Combine(t.Dir, "profiles", Theme);
        Directory.CreateDirectory(dir);
        // perfil v1: Standings com Rows=8, sem TopCount/NearCount e sem o widget "board"
        File.WriteAllText(Path.Combine(dir, "antigo.json"),
            "{\"schemaVersion\":1,\"name\":\"Antigo\",\"themeId\":\"f1-1998\",\"widgets\":[{\"id\":\"standings\",\"visible\":true,\"x\":10,\"y\":20,\"scale\":1,\"opacity\":1,\"order\":0,\"rows\":12,\"columns\":[\"pos\",\"name\",\"gap\"]}]}");
        var p = t.Store.Load(Theme, "Antigo")!;
        var s = p.Get("standings")!;
        Assert.Equal((10, 20), (s.X, s.Y));
        Assert.Equal(["pos", "name", "gap"], s.Columns);                      // escolhas antigas preservadas
        Assert.Equal((5, 3), (s.TopCount, s.NearCount));                      // padroes novos
        Assert.NotNull(p.Get("board"));
        Assert.Equal(Profile.CurrentSchemaVersion, p.SchemaVersion);
    }

    [Fact]
    public void Patch_changes_standings_top_and_near_live()
    {
        var s = new WidgetSettings { Id = "standings" }.Normalized();
        var r = new WidgetPatch { TopCount = 3 }.ApplyTo(s);
        Assert.Equal((3, 3), (r.TopCount, r.NearCount));
        r = new WidgetPatch { NearCount = 5 }.ApplyTo(r);
        Assert.Equal((3, 5), (r.TopCount, r.NearCount));
    }

    [Fact]
    public void Board_defaults_keep_only_tyre_and_page_in_f1_1998()
    {
        Assert.Equal(["tyre", "page"], ProfileFactory.CreateDefault("x", "f1-1998").Get("board")!.Columns);
        Assert.Null(ProfileFactory.CreateDefault("x", "f1-2004").Get("board")!.Columns);   // todas visiveis
        // 2018: a TV nao tem o Board e a torre traz o "LAP n / N": Board e Lap Counter desligados (continuam no perfil); Relative tambem.
        var p18 = ProfileFactory.CreateDefault("x", "f1-2018");
        Assert.False(p18.Get("board")!.Visible);
        Assert.False(p18.Get("lapcounter")!.Visible);
        Assert.False(p18.Get("relative")!.Visible);
        Assert.True(p18.Get("standings")!.Visible);
        Assert.True(ProfileFactory.CreateDefault("x", "f1-2004").Get("board")!.Visible);
        Assert.True(ProfileFactory.CreateDefault("x", "f1-1998").Get("lapcounter")!.Visible);
    }

    [Fact]
    public void New_widgets_exist_and_standings_defaults_to_pos_and_name()
    {
        Assert.NotNull(WidgetCatalog.Find("lapcounter"));
        Assert.DoesNotContain(WidgetCatalog.Find("standings")!.Columns, c => c.Id == "flag");
        Assert.Contains(WidgetCatalog.Find("relative")!.Columns, c => c.Id == "bar");
        Assert.Equal(["pos", "name"], ProfileFactory.CreateDefault("x", "f1-2004").Get("standings")!.Columns);
        Assert.Equal(["pos", "name"], ProfileFactory.CreateDefault("x", "f1-1998").Get("standings")!.Columns);   // so posicao + sigla, sem gap/classe
        Assert.Equal(["pos", "name", "gap"], ProfileFactory.CreateDefault("x", "f1-2018").Get("standings")!.Columns);   // 2018: coluna clara (gap/modos) como na TV
        Assert.Equal(["pos", "name", "gap"], ProfileFactory.CreateDefault("x", "f1-1998").Get("relative")!.Columns);
        Assert.Contains(WidgetCatalog.Find("standings")!.Columns, c => c.Id == "table");
        // perfis antigos com a coluna "flag" carregam sem erro: a coluna desconhecida e ignorada
        var old = ProfileFactory.CreateDefault("x", "f1-2004").Get("standings")! with { Columns = ["pos", "name", "flag"] };
        Assert.Equal(["pos", "name"], old.Normalized().Columns);
    }

    [Fact]
    public void Broadcast_widgets_are_in_the_catalog_and_default_to_event_only_display()
    {
        foreach (var id in new[] { "drivercaption", "pitstops", "pittimer", "winner" })
        {
            var d = WidgetCatalog.Find(id);
            Assert.NotNull(d);
            Assert.Contains(d!.Columns, c => c.Id == "always");
            foreach (var theme in new[] { "f1-1998", "f1-2004", "f1-2018" })
            {
                var w = ProfileFactory.CreateDefault("x", theme).Get(id)!.Normalized();
                Assert.False(w.ColumnVisible("always"));      // padrao: so aparece nos eventos
                Assert.True(w with { Columns = null } is { } all && all.ColumnVisible("always"));
            }
        }
        Assert.Equal(4, WidgetCatalog.Find("pitstops")!.MaxRows);
        Assert.Equal(WidgetCatalog.All.Count, WidgetCatalog.All.Select(w => w.Id).Distinct().Count());
    }

    [Fact]
    public void Missing_widgets_are_filled_in_on_load()
    {
        using var t = new TempStore();
        t.Store.Save(new Profile { Name = "Parcial", ThemeId = Theme, Widgets = [new WidgetSettings { Id = "fuel", X = 5, Y = 6 }] });
        var p = t.Store.Load(Theme, "Parcial")!;
        Assert.Equal(WidgetCatalog.ForTheme(Theme).Count(), p.Widgets.Count);
        Assert.Equal(5, p.Get("fuel")!.X);
    }

    [Fact]
    public void Corrupted_or_newer_schema_files_are_ignored()
    {
        using var t = new TempStore();
        t.Store.EnsureDefault(Theme);
        var dir = Path.Combine(t.Dir, "profiles", Theme);
        File.WriteAllText(Path.Combine(dir, "ruim.json"), "{ not json");
        File.WriteAllText(Path.Combine(dir, "futuro.json"), "{\"schemaVersion\":99,\"name\":\"Futuro\",\"themeId\":\"f1-1998\",\"widgets\":[]}");
        Assert.Equal(["Padrão"], t.Store.List(Theme));
        Assert.Null(t.Store.Load(Theme, "futuro"));
    }

    [Fact]
    public void Duplicate_rename_delete_and_active_profile()
    {
        using var t = new TempStore();
        var active = t.Store.GetActiveProfile(Theme);
        Assert.Equal("Padrão", active);
        t.Store.Duplicate(Theme, "Padrão", "Chuva");
        Assert.Equal(["Chuva", "Padrão"], t.Store.List(Theme));
        Assert.Throws<InvalidOperationException>(() => t.Store.Duplicate(Theme, "Padrão", "Chuva"));

        t.Store.SetActiveProfile(Theme, "Chuva");
        t.Store.Rename(Theme, "Chuva", "Noite");
        Assert.Equal("Noite", t.Store.GetActiveProfile(Theme));
        Assert.False(t.Store.Exists(Theme, "Chuva"));

        t.Store.Delete(Theme, "Noite");
        Assert.Equal("Padrão", t.Store.GetActiveProfile(Theme)); // o ativo sumiu: cai no padrao
    }

    [Fact]
    public void Profiles_are_separated_per_theme_and_theme_is_remembered()
    {
        using var t = new TempStore();
        t.Store.Save(ProfileFactory.CreateDefault("X", "f1-1998"));
        Assert.Empty(t.Store.List("f1-2004"));
        t.Store.SetActiveTheme("f1-2004");
        Assert.Equal("f1-2004", new ProfileStore(t.Dir).GetActiveTheme());
    }

    [Fact]
    public void Patch_applies_only_present_fields()
    {
        var s = new WidgetSettings { Id = "standings", X = 1, Y = 2, Scale = 1, Font = "Arial", Columns = ["gap"] };
        var r = new WidgetPatch { Scale = 2f, Visible = false }.ApplyTo(s);
        Assert.Equal((1, 2, 2f, false, "Arial"), (r.X, r.Y, r.Scale, r.Visible, r.Font));
        var r2 = new WidgetPatch { ClearFont = true, AllColumns = true }.ApplyTo(r);
        Assert.Null(r2.Font); Assert.Null(r2.Columns);
    }

    [Fact]
    public void MoveTo_reorders_and_renumbers()
    {
        var p = ProfileFactory.CreateDefault("A", Theme).MoveTo("weather", 0);
        Assert.Equal("weather", p.Ordered.First().Id);
        Assert.Equal(Enumerable.Range(0, WidgetCatalog.ForTheme(Theme).Count()), p.Ordered.Select(w => w.Order));
        p = p.MoveTo("weather", 99);
        Assert.Equal("weather", p.Ordered.Last().Id);
    }

    [Fact]
    public void Names_with_invalid_characters_still_roundtrip()
    {
        using var t = new TempStore();
        t.Store.Save(ProfileFactory.CreateDefault("a/b:c?", Theme));
        Assert.Contains("a/b:c?", t.Store.List(Theme));
        Assert.NotNull(t.Store.Load(Theme, "a/b:c?"));
    }

    // ---- Migracao do tema substituido f1-2010s -> f1-2018 (pasta temporaria; os arquivos antigos ficam intactos) ----

    static void WriteLegacy(string dir, string name, int standingsX)
    {
        var p = ProfileFactory.CreateDefault(name, "f1-2010s");
        p = p.WithWidget(p.Get("standings")! with { X = standingsX });
        Directory.CreateDirectory(Path.Combine(dir, "profiles", "f1-2010s"));
        File.WriteAllText(Path.Combine(dir, "profiles", "f1-2010s", name + ".json"), System.Text.Json.JsonSerializer.Serialize(p, ProfileStore.Json));
    }

    [Fact]
    public void Legacy_2010s_profiles_and_state_are_read_as_2018_without_deleting_the_old_files()
    {
        using var t = new TempStore();
        WriteLegacy(t.Dir, "Meu", 123);
        File.WriteAllText(Path.Combine(t.Dir, "state.json"), "{\"activeTheme\":\"f1-2010s\",\"activeProfiles\":{\"f1-2010s\":\"Meu\",\"f1-1998\":\"Padrão\"}}");
        var store = new ProfileStore(t.Dir);

        Assert.Equal("f1-2018", store.GetActiveTheme());
        Assert.Equal("Meu", store.GetActiveProfile("f1-2018"));
        Assert.Contains("Meu", store.List("f1-2018"));
        var p = store.Load("f1-2018", "Meu")!;
        Assert.Equal("f1-2018", p.ThemeId);
        Assert.Equal(123, p.Get("standings")!.X);
        // o id antigo continua funcionando (Control Center/host antigos) e aponta para a mesma pasta
        Assert.Equal(123, store.Load("f1-2010s", "Meu")!.Get("standings")!.X);
        // nada do usuario foi apagado: a pasta antiga continua la
        Assert.True(File.Exists(Path.Combine(t.Dir, "profiles", "f1-2010s", "Meu.json")));
        Assert.True(File.Exists(Path.Combine(t.Dir, "profiles", "f1-2018", "Meu.json")));
        var state = File.ReadAllText(Path.Combine(t.Dir, "state.json"));
        Assert.DoesNotContain("f1-2010s", state);
        Assert.Contains("f1-1998", state);
    }

    [Fact]
    public void Legacy_migration_never_overwrites_an_existing_2018_profile()
    {
        using var t = new TempStore();
        WriteLegacy(t.Dir, "Meu", 123);
        var current = ProfileFactory.CreateDefault("Meu", "f1-2018");
        Directory.CreateDirectory(Path.Combine(t.Dir, "profiles", "f1-2018"));
        File.WriteAllText(Path.Combine(t.Dir, "profiles", "f1-2018", "Meu.json"),
            System.Text.Json.JsonSerializer.Serialize(current.WithWidget(current.Get("standings")! with { X = 456 }), ProfileStore.Json));
        var store = new ProfileStore(t.Dir);
        Assert.Equal(456, store.Load("f1-2018", "Meu")!.Get("standings")!.X);
    }

    [Fact]
    public void Legacy_theme_ids_map_to_the_new_theme()
    {
        Assert.Equal("f1-2018", ThemeCatalog.Canonical("f1-2010s"));
        Assert.Equal("f1-2004", ThemeCatalog.Canonical("f1-2004"));
        Assert.Equal("f1-2018", ThemeCatalog.Find("F1-2010S")!.Id);
        Assert.DoesNotContain(ThemeCatalog.All, d => d.Id == "f1-2010s");
        Assert.Equal("F1 2018", ThemeCatalog.Find("f1-2018")!.DisplayName);
        Assert.Equal(WidgetLayout.Get("f1-2018", "standings"), WidgetLayout.Get("f1-2010s", "standings"));
    }}
