using System.Text.Json;
using Ams2.Shared.Profiles;

namespace Ams2.Shared.Tests;

/// <summary>Formatadores puros (nome, gap, tempo, unidades) e a personalizacao por widget no perfil (larguras, texto, formato, migracao v3).</summary>
public class DisplayFormatTests
{
    [Theory]
    [InlineData(NameStyle.Code3, "ALO")]
    [InlineData(NameStyle.Initials, "FA")]
    [InlineData(NameStyle.InitialLastName, "F. Alonso")]
    [InlineData(NameStyle.LastName, "Alonso")]
    [InlineData(NameStyle.FullName, "Fernando Alonso")]
    public void Name_styles(NameStyle style, string expected) => Assert.Equal(expected, DisplayFormat.Name("Fernando Alonso", style));

    [Fact]
    public void Unique_codes_keep_the_base_code_without_collision_and_disambiguate_repeats()
    {
        // Sem colisao: a sigla de sempre.
        Assert.Equal(["ALO", "SCH", "COS"], DisplayFormat.UniqueCodes(["Fernando Alonso", "Michael Schumacher", "Vitor COSTA"]));
        // Colisao: o 1o (na ordem recebida) fica com a base; os demais inicial do nome + 2 letras do sobrenome.
        Assert.Equal(["TRU", "PHI", "MTR"], DisplayFormat.UniqueCodes(["Marty Trundle", "Jonas Phillips", "Mark Trundle"]));
        Assert.Equal(["SCH", "RSC"], DisplayFormat.UniqueCodes(["Michael Schumacher", "Ralf Schumacher"]));
        // Ainda colide (mesma inicial): 2 letras do sobrenome + digito; nunca toma a sigla base de outro piloto.
        Assert.Equal(["TRU", "MTR", "TR2", "TR3"], DisplayFormat.UniqueCodes(["Marty Trundle", "Mark Trundle", "Mike Trundle", "Max Truman"]));
        Assert.Equal(["TRU", "MTR", "TR2"], DisplayFormat.UniqueCodes(["Marty Trundle", "Max Mtrovic", "Mark Trundle"]));
        // Nome de uma palavra e vazio.
        Assert.Equal(["SEN", "SSE", ""], DisplayFormat.UniqueCodes(["Senna", "Senna", " "]));
    }

    [Fact]
    public void Name_fixes_all_caps_surname_and_prefixes_the_car_number()
    {
        Assert.Equal("Costa", DisplayFormat.Name("Vitor COSTA", NameStyle.LastName));
        Assert.Equal("V. Costa", DisplayFormat.Name("Vitor COSTA", NameStyle.InitialLastName));
        Assert.Equal("Vitor Costa", DisplayFormat.Name("Vitor COSTA", NameStyle.FullName));
        Assert.Equal("#12 COS", DisplayFormat.Name("Vitor COSTA", NameStyle.Code3, 12));
        Assert.Equal("VDS", DisplayFormat.Name("Vitor de Souza", NameStyle.Initials));
        Assert.Equal("de Souza", DisplayFormat.Name("Vitor de Souza", NameStyle.LastName));
        Assert.Equal("SEN", DisplayFormat.Name("Senna", NameStyle.Code3));
        Assert.Equal("Senna", DisplayFormat.Name("Senna", NameStyle.InitialLastName));
        Assert.Equal("", DisplayFormat.Name("  ", NameStyle.FullName));
    }

    [Theory]
    [InlineData(1.2346, 3, true, false, "+1.235")]
    [InlineData(1.25, 1, true, false, "+1.3")]       // meio para cima
    [InlineData(1.2346, 1, true, false, "+1.2")]
    [InlineData(1.6, 0, true, false, "+2")]
    [InlineData(-0.5, 2, true, false, "-0.50")]
    [InlineData(-0.5, 2, false, false, "0.50")]
    [InlineData(0.0004, 3, true, false, "+0.000")]
    [InlineData(-0.0004, 3, true, false, "+0.000")]   // -0 arredondado nao vira "-0.000"
    [InlineData(12.3, 2, true, true, "+12.30s")]
    [InlineData(62.3456, 3, true, true, "+1:02.346")] // a partir de 60 s, m:ss (sem sufixo)
    [InlineData(59.9996, 3, true, false, "+1:00.000")]
    [InlineData(125, 0, false, false, "2:05")]
    public void Gap_format(double s, int dec, bool sign, bool suffix, string expected) => Assert.Equal(expected, DisplayFormat.Gap(s, dec, sign, suffix));

    [Fact]
    public void Laps_and_placeholders()
    {
        Assert.Equal("+2L", DisplayFormat.Laps(2));
        Assert.Equal("-1L", DisplayFormat.Laps(-1));
        Assert.Equal("2L", DisplayFormat.Laps(2, sign: false));
        Assert.Equal("--.---", DisplayFormat.NoTime(3));
        Assert.Equal("--.-", DisplayFormat.NoTime(1));
        Assert.Equal("--", DisplayFormat.NoTime(0));
    }

    [Theory]
    [InlineData(83.4567, LapTimeStyle.MinSec, 3, "1:23.457")]
    [InlineData(83.4567, LapTimeStyle.MinSec, 1, "1:23.5")]
    [InlineData(83.4567, LapTimeStyle.MinSec, 0, "1:23")]
    [InlineData(83.4567, LapTimeStyle.Seconds, 3, "83.457")]
    [InlineData(59.9996, LapTimeStyle.MinSec, 3, "1:00.000")]
    [InlineData(45.1, LapTimeStyle.MinSec, 3, "0:45.100")]
    [InlineData(3723.5, LapTimeStyle.MinSec, 1, "1:02:03.5")]
    [InlineData(0, LapTimeStyle.MinSec, 3, "-:--.---")]
    [InlineData(-1, LapTimeStyle.Seconds, 2, "--.--")]
    public void Lap_time_format(double s, LapTimeStyle style, int dec, string expected) => Assert.Equal(expected, DisplayFormat.LapTime(s, style, dec));

    [Fact]
    public void Units()
    {
        Assert.Equal(360, DisplayFormat.Speed(100, SpeedUnit.Kph), 6);
        Assert.Equal(223.69, DisplayFormat.Speed(100, SpeedUnit.Mph), 2);
        Assert.Equal(100, DisplayFormat.SpeedFromKph(160.9344, SpeedUnit.Mph), 6);
        Assert.Equal(212, DisplayFormat.Temp(100, TempUnit.Fahrenheit), 6);
        Assert.Equal(32, DisplayFormat.Temp(0, TempUnit.Fahrenheit), 6);
        Assert.Equal(1, DisplayFormat.Fuel(3.785411784, FuelUnit.Gallons), 9);
        Assert.Equal(("KPH", "MPH", "°F", "GAL", "mi"), (DisplayFormat.SpeedLabel(SpeedUnit.Kph), DisplayFormat.SpeedLabel(SpeedUnit.Mph), DisplayFormat.TempLabel(TempUnit.Fahrenheit), DisplayFormat.FuelLabel(FuelUnit.Gallons), DisplayFormat.DistanceLabel(SpeedUnit.Mph)));
    }

    [Fact]
    public void Display_options_fall_back_to_the_widget_default()
    {
        var empty = DisplayOptions.Empty;
        Assert.Equal("ALO", empty.FormatName("Fernando Alonso", 3, NameStyle.Code3));
        Assert.Equal("Alonso", empty.FormatName("Fernando Alonso", 3, NameStyle.LastName));
        Assert.Equal("+1.234", empty.FormatGap(1.234));
        Assert.Equal("1.234", empty.FormatGap(1.234, defaultSign: false)); // 1998: lista sem "+"
        var o = new DisplayOptions { Name = NameStyle.FullName, CarNumber = true, GapDecimals = 1, GapSign = true, GapSuffix = true, LapTime = LapTimeStyle.Seconds, LapDecimals = 2 };
        Assert.Equal("#3 Fernando Alonso", o.FormatName("Fernando Alonso", 3, NameStyle.Code3));
        Assert.Equal("+1.2s", o.FormatGap(1.234, defaultSign: false));
        Assert.Equal("--.-", o.NoGap);
        Assert.Equal("83.46", o.FormatLapTime(83.456));
        Assert.Equal("+1L", o.FormatLaps(1, defaultSign: false));
    }

    [Fact]
    public void Display_options_normalize_and_roundtrip_as_readable_json()
    {
        Assert.Null(new DisplayOptions().Normalized());
        Assert.Equal(3, new DisplayOptions { GapDecimals = 9 }.Normalized()!.GapDecimals);
        Assert.Equal(0, new DisplayOptions { LapDecimals = -2 }.Normalized()!.LapDecimals);
        var o = new DisplayOptions { Name = NameStyle.InitialLastName, Speed = SpeedUnit.Mph, Temp = TempUnit.Fahrenheit, Fuel = FuelUnit.Gallons };
        string json = JsonSerializer.Serialize(o, ProfileStore.Json);
        Assert.Contains("\"InitialLastName\"", json);
        Assert.Contains("\"Mph\"", json);
        Assert.Equal(o, JsonSerializer.Deserialize<DisplayOptions>(json, ProfileStore.Json));
    }
}

public class CustomizationProfileTests
{
    [Fact]
    public void Normalized_clamps_widths_text_and_colors_and_drops_unknown_or_default_values()
    {
        var s = new WidgetSettings
        {
            Id = "standings",
            ColumnWidths = new() { ["name"] = 400, ["gap"] = 100, ["bogus"] = 150, ["pos"] = 10 },
            TextScale = 5f, FontWeight = 640, TextColor = "#ff0", LabelColor = "zzz", ValueColor = " 00ccff ",
            Display = new DisplayOptions(),
        }.Normalized();
        Assert.Equal(new Dictionary<string, int> { ["name"] = WidgetCatalog.MaxWidthPct, ["pos"] = WidgetCatalog.MinWidthPct }, s.ColumnWidths);
        Assert.Equal(WidgetCatalog.MaxTextScale, s.TextScale);
        Assert.Equal(600, s.FontWeight);
        Assert.Equal("#FFFF00", s.TextColor);
        Assert.Null(s.LabelColor);
        Assert.Equal("#00CCFF", s.ValueColor);
        Assert.Null(s.Display);
        Assert.Equal(2.5f, s.WidthFactor("name"));
        Assert.Equal(1f, s.WidthFactor("gap"));
        Assert.Equal(52f, s.Width("pos", 104));
    }

    [Fact]
    public void Text_scale_multiplies_the_render_scale_so_the_window_grows_with_the_font()
    {
        var s = new WidgetSettings { Id = "relative", Scale = 0.8f, TextScale = 1.5f }.Normalized();
        Assert.Equal(1.2f, s.RenderScale, 4);
        Assert.Equal(0.8f, (s with { TextScale = null }).RenderScale);
        Assert.Null(new WidgetSettings { Id = "relative", TextScale = 1f }.Normalized().TextScale);
    }

    [Fact]
    public void Patch_sets_and_clears_the_new_fields()
    {
        var s = new WidgetSettings { Id = "relative" }.Normalized();
        s = new WidgetPatch
        {
            ColumnWidths = new() { ["name"] = 140 }, TextScale = 1.25f, FontWeight = 800, TextColor = "#112233",
            Display = new DisplayOptions { GapDecimals = 1 },
        }.ApplyTo(s);
        Assert.Equal(140, s.ColumnWidths!["name"]);
        Assert.Equal((1.25f, 800, "#112233", 1), (s.TextScale!.Value, s.FontWeight!.Value, s.TextColor, s.Display!.GapDecimals!.Value));
        s = new WidgetPatch { ColumnWidths = [], FontWeight = 0, TextColor = "", Display = new DisplayOptions(), TextScale = 1f }.ApplyTo(s);
        Assert.Null(s.ColumnWidths); Assert.Null(s.FontWeight); Assert.Null(s.TextColor); Assert.Null(s.Display); Assert.Null(s.TextScale);
    }

    [Fact]
    public void Merge_keeps_the_latest_value_of_each_field()
    {
        var a = new WidgetPatch { TextScale = 1.2f, TextColor = "#111111", Display = new DisplayOptions { GapDecimals = 1 } };
        var b = new WidgetPatch { TextColor = "", ColumnWidths = new() { ["gap"] = 80 } };
        var m = WidgetPatch.Merge(a, b);
        Assert.Equal(1.2f, m.TextScale);
        Assert.Equal("", m.TextColor);
        Assert.Equal(1, m.Display!.GapDecimals);
        Assert.Equal(80, m.ColumnWidths!["gap"]);
    }

    [Fact]
    public void Customization_roundtrips_through_the_store()
    {
        using var t = new TempStore();
        var p = ProfileFactory.CreateDefault("Custom", "f1-2018");
        var s = p.Get("board")! with
        {
            ColumnWidths = new() { ["name"] = 130, ["time"] = 90 }, TextScale = 1.1f, FontWeight = 700, ValueColor = "#AABBCC",
            Display = new DisplayOptions { Name = NameStyle.FullName, LapTime = LapTimeStyle.Seconds, LapDecimals = 1, GapSign = false },
        };
        t.Store.Save(p.WithWidget(s));
        var r = t.Store.Load("f1-2018", "Custom")!.Get("board")!;
        Assert.Equal(130, r.ColumnWidths!["name"]);
        Assert.Equal(90, r.ColumnWidths!["time"]);
        Assert.Equal((1.1f, 700, "#AABBCC"), (r.TextScale!.Value, r.FontWeight!.Value, r.ValueColor));
        Assert.Equal(s.Display, r.Display);
    }

    [Fact]
    public void Old_profiles_load_unchanged_and_saved_column_lists_get_the_new_columns()
    {
        using var t = new TempStore();
        // Perfil v2 do f1-2004 como era gravado: Inputs sem grafico, Driver Caption/Winner so com a lista vazia ("always" desligado).
        string json = """
        {"schemaVersion":2,"name":"Velho","themeId":"f1-2004","widgets":[
          {"id":"inputs","visible":true,"x":1700,"y":770,"scale":0.6,"opacity":1,"order":0,"columns":["bars","gear"]},
          {"id":"drivercaption","visible":true,"x":1,"y":2,"scale":1,"opacity":1,"order":1,"columns":[]},
          {"id":"weather","visible":true,"x":1,"y":2,"scale":1,"opacity":1,"order":2},
          {"id":"standings","visible":true,"x":1,"y":2,"scale":1,"opacity":1,"order":3,"columns":["pos","name"],"topCount":5,"nearCount":3}
        ]}
        """;
        var dir = Path.Combine(t.Dir, "profiles", "f1-2004");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Velho.json"), json);
        var p = t.Store.Load("f1-2004", "Velho")!;
        Assert.Equal(Profile.CurrentSchemaVersion, p.SchemaVersion);
        Assert.Equal(["bars", "gear", "speedo", "graph"], p.Get("inputs")!.Columns);
        Assert.Equal(["team", "tyre"], p.Get("drivercaption")!.Columns);   // continua so em eventos, campos visiveis
        Assert.Null(p.Get("weather")!.Columns);
        Assert.Equal(["pos", "name"], p.Get("standings")!.Columns);
        Assert.All(p.Widgets, w => { Assert.Null(w.Display); Assert.Null(w.ColumnWidths); Assert.Null(w.TextScale); Assert.Equal(w.Scale, w.RenderScale); });
        // Salvo de novo ja como v3: carregar outra vez nao duplica nada.
        t.Store.Save(p);
        Assert.Equal(["bars", "gear", "speedo", "graph"], t.Store.Load("f1-2004", "Velho")!.Get("inputs")!.Columns);
    }

    [Fact]
    public void New_defaults_show_the_2004_input_graph_and_keep_event_widgets_on_events()
    {
        var p = ProfileFactory.CreateDefault("A", "f1-2004");
        Assert.True(p.Get("inputs")!.ColumnVisible("graph"));
        Assert.True(p.Get("inputs")!.ColumnVisible("speedo"));
        foreach (var id in new[] { "drivercaption", "pitstops", "pittimer", "winner", "radar" })
        {
            Assert.False(p.Get(id)!.ColumnVisible("always"), id);
            Assert.False(p.Get(id)!.ColumnVisible("native"), id);
        }
        Assert.True(p.Get("winner")!.ColumnVisible("stats"));
    }

    [Fact]
    public void Catalog_width_columns_and_caps_cover_the_priority_widgets()
    {
        foreach (var id in new[] { "standings", "relative", "board", "inputs" })
            Assert.NotEmpty(WidgetCatalog.Find(id)!.Widths);
        Assert.True(WidgetCatalog.Find("standings")!.Caps.HasFlag(DisplayCaps.Name | DisplayCaps.Gap));
        Assert.True(WidgetCatalog.Find("board")!.Caps.HasFlag(DisplayCaps.LapTime));
        Assert.True(WidgetCatalog.Find("inputs")!.Caps.HasFlag(DisplayCaps.Speed));
        Assert.True(WidgetCatalog.Find("weather")!.Caps.HasFlag(DisplayCaps.Temp));
        Assert.True(WidgetCatalog.Find("fuel")!.Caps.HasFlag(DisplayCaps.Fuel));
    }
}
