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
        Assert.Equal(WidgetCatalog.All.Select(w => w.Id), p.Ordered.Select(w => w.Id));
        Assert.All(p.Widgets, w => { Assert.True(w.Visible); Assert.Equal(WidgetCatalog.Find(w.Id)!.DefaultScale, w.Scale); });
        // layout do usuario no iRacing V3 (v3-layout.json): relative embaixo à direita, standings no canto superior esquerdo
        Assert.Equal((1430, 925), (p.Get("relative")!.X, p.Get("relative")!.Y));
        Assert.Equal((0, 0), (p.Get("standings")!.X, p.Get("standings")!.Y));
        Assert.Equal(8, p.Get("standings")!.Rows);
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
        var s = p.Get("standings")! with { Visible = false, X = 12, Y = 34, Scale = 1.25f, Opacity = 0.7f, Font = "Segoe UI", Rows = 12, Columns = ["pos", "gap"], Order = 5 };
        t.Store.Save(p.WithWidget(s));
        var back = t.Store.Load(Theme, "Corrida")!;
        var r = back.Get("standings")!;
        Assert.False(r.Visible);
        Assert.Equal((12, 34), (r.X, r.Y));
        Assert.Equal(1.25f, r.Scale); Assert.Equal(0.7f, r.Opacity);
        Assert.Equal("Segoe UI", r.Font); Assert.Equal(12, r.Rows);
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
    }

    [Fact]
    public void New_widgets_exist_and_f1_2004_standings_defaults_to_mini_tower_columns()
    {
        Assert.NotNull(WidgetCatalog.Find("lapcounter"));
        Assert.Contains(WidgetCatalog.Find("standings")!.Columns, c => c.Id == "flag");
        Assert.Contains(WidgetCatalog.Find("relative")!.Columns, c => c.Id == "bar");
        Assert.Equal(["pos", "name", "flag"], ProfileFactory.CreateDefault("x", "f1-2004").Get("standings")!.Columns);
        Assert.Null(ProfileFactory.CreateDefault("x", "f1-1998").Get("standings")!.Columns);
        Assert.Equal(["pos", "name", "flag"], ProfileFactory.CreateDefault("x", "f1-2004").Get("standings")!.Normalized().Columns);
    }

    [Fact]
    public void Broadcast_widgets_are_in_the_catalog_and_default_to_event_only_display()
    {
        foreach (var id in new[] { "drivercaption", "pitstops", "pittimer", "winner" })
        {
            var d = WidgetCatalog.Find(id);
            Assert.NotNull(d);
            Assert.Contains(d!.Columns, c => c.Id == "always");
            foreach (var theme in new[] { "f1-1998", "f1-2004", "f1-2010s" })
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
        Assert.Equal(WidgetCatalog.All.Count, p.Widgets.Count);
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
        Assert.Equal(Enumerable.Range(0, WidgetCatalog.All.Count), p.Ordered.Select(w => w.Order));
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
}
