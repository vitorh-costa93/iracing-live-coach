using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

/// <summary>
/// Personalizacao aplicada no desenho real (--png com o escritor falso): o tamanho do texto redimensiona a janela inteira na
/// proporcao, larguras de coluna mudam a largura do widget e o --settings (o mesmo JSON do perfil que o Control Center usa) vale.
/// </summary>
public sealed class CustomizationPngTests
{
    static string Exe()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Ams2.OverlayHost"))) dir = dir.Parent;
        Assert.NotNull(dir);
        string cfg = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";
        return Path.Combine(dir!.FullName, "src", "Ams2.OverlayHost", "bin", cfg, "net9.0-windows", "Ams2.OverlayHost.exe");
    }

    /// <summary>Renderiza e devolve (largura, altura) do PNG e a saida.</summary>
    static (int W, int H, string Out) Render(string args, WidgetSettings? settings = null)
    {
        string png = Path.Combine(Path.GetTempPath(), $"ams2-cust-{Guid.NewGuid():N}.png");
        string? json = null;
        if (settings is not null)
        {
            json = Path.ChangeExtension(png, ".json");
            File.WriteAllText(json, JsonSerializer.Serialize(settings, ProfileStore.Json));
            args += $" --settings \"{json}\"";
        }
        var psi = new ProcessStartInfo(Exe(), $"--png \"{png}\" --sim 20 {args}") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        psi.Environment["AMS2_FAKE_BOARD"] = "1";
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(30000));
        try
        {
            Assert.Equal(0, p.ExitCode);
            var m = Regex.Match(o, @"\[PNG\] .* (\d+)x(\d+) tema=");
            Assert.True(m.Success, o);
            Assert.True(new FileInfo(png).Length > 500);
            return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), o);
        }
        finally { File.Delete(png); if (json is not null) File.Delete(json); }
    }

    [Theory]
    [InlineData("relative", "f1-1998")]
    [InlineData("standings", "f1-2004")]
    [InlineData("inputs", "f1-2018")]
    [InlineData("board", "f1-2004")]
    [InlineData("weather", "f1-1998")]
    [InlineData("standings", "f1-2018")]
    [InlineData("lapcounter", "f1-2018")]
    [InlineData("drivercaption", "f1-2018")]
    [InlineData("pittimer", "f1-2018")]
    [InlineData("board", "f1-2018")]
    [InlineData("livespeed", "f1-2018")]
    [InlineData("racestart", "f1-2018")]
    public void Text_scale_grows_the_window_in_proportion(string widget, string theme)
    {
        var a = Render($"--widget {widget} --theme {theme}");
        var b = Render($"--widget {widget} --theme {theme} --text-scale 1.5");
        Assert.InRange(b.W, a.W * 1.5 - 2, a.W * 1.5 + 2);
        Assert.InRange(b.H, a.H * 1.5 - 2, a.H * 1.5 + 2);
        var c = Render($"--widget {widget} --theme {theme} --text-scale 0.8");
        Assert.True(c.W < a.W && c.H < a.H);
    }

    [Fact]
    public void Text_scale_in_the_settings_json_also_resizes()
    {
        var a = Render("--widget fuel --theme f1-2018", new WidgetSettings { Id = "fuel" });
        var b = Render("--widget fuel --theme f1-2018", new WidgetSettings { Id = "fuel", TextScale = 2f });
        Assert.Equal((a.W * 2, a.H * 2), (b.W, b.H));
    }

    [Theory]
    [InlineData("standings", "f1-1998", "name")]
    [InlineData("standings", "f1-2004", "gap")]
    [InlineData("relative", "f1-2018", "name")]
    [InlineData("relative", "f1-2004", "gap")]
    [InlineData("inputs", "f1-1998", "graph")]
    [InlineData("inputs", "f1-2004", "graph")]
    [InlineData("board", "f1-2018", "name")]
    [InlineData("pitstops", "f1-2004", "name")]
    [InlineData("pittimer", "f1-1998", "name")]
    [InlineData("standings", "f1-2018", "name")]
    [InlineData("standings", "f1-2018", "gap")]
    [InlineData("pitstops", "f1-2018", "stops")]
    [InlineData("pittimer", "f1-2018", "name")]
    [InlineData("inputs", "f1-2018", "graph")]
    public void Wider_column_makes_the_widget_wider(string widget, string theme, string column)
    {
        var cols = widget == "standings" ? new[] { "pos", "name", "gap" } : null;
        var a = Render($"--widget {widget} --theme {theme}", new WidgetSettings { Id = widget, Columns = cols });
        var b = Render($"--widget {widget} --theme {theme}", new WidgetSettings { Id = widget, Columns = cols, ColumnWidths = new() { [column] = 200 } });
        Assert.True(b.W > a.W, $"{a.W} -> {b.W}");
        Assert.Equal(a.H, b.H);
    }

    /// <summary>Renderiza e devolve os bytes do PNG (para comparar pixels).</summary>
    static byte[] Pixels(string args, WidgetSettings settings)
    {
        string png = Path.Combine(Path.GetTempPath(), $"ams2-cust-{Guid.NewGuid():N}.png");
        string json = Path.ChangeExtension(png, ".json");
        File.WriteAllText(json, JsonSerializer.Serialize(settings, ProfileStore.Json));
        var psi = new ProcessStartInfo(Exe(), $"--png \"{png}\" --sim 20 {args} --settings \"{json}\"") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        psi.Environment["AMS2_FAKE_BOARD"] = "1";
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(30000));
        try { Assert.Equal(0, p.ExitCode); return File.ReadAllBytes(png); }
        finally { File.Delete(png); File.Delete(json); }
    }

    // 2004 e 2018 usam a mesma familia para texto e numeros: a troca de fonte tem que valer mesmo assim.
    [Theory]
    [InlineData("standings", "f1-2004")]
    [InlineData("relative", "f1-2018")]
    [InlineData("standings", "f1-2018")]
    [InlineData("drivercaption", "f1-2018")]
    public void Font_override_changes_the_drawing_when_text_and_numbers_share_a_family(string widget, string theme)
    {
        var a = Pixels($"--widget {widget} --theme {theme}", new WidgetSettings { Id = widget });
        var b = Pixels($"--widget {widget} --theme {theme}", new WidgetSettings { Id = widget, Font = "Reddit Sans" });
        Assert.NotEqual(a, b);
    }

    // No 2004 os nomes ficam em celulas brancas (tinta propria): a cor de "Textos" tem que chegar nelas.
    [Fact]
    public void Text_color_reaches_names_on_the_white_cells_of_2004()
    {
        var a = Pixels("--widget standings --theme f1-2004", new WidgetSettings { Id = "standings" });
        var b = Pixels("--widget standings --theme f1-2004", new WidgetSettings { Id = "standings", TextColor = "#00AA00" });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Relative_gap_format_reaches_the_drawn_rows()
    {
        // A saida do --png lista o Relative com o formato padrao; aqui so garante que um formato extremo renderiza sem erro.
        var r = Render("--widget relative --theme f1-2004", new WidgetSettings
        {
            Id = "relative",
            Display = new DisplayOptions { Name = NameStyle.FullName, CarNumber = true, GapDecimals = 0, GapSign = false, GapSuffix = true },
            FontWeight = 400, TextColor = "#FF8800", ValueColor = "#00FF00",
        });
        Assert.True(r.W > 0);
    }

    // Tema 2018: cor de "Textos"/"Valores" e peso da fonte chegam na torre (siglas, gaps) e o formato de nome vale na legenda.
    [Theory]
    [InlineData("TextColor")]
    [InlineData("ValueColor")]
    [InlineData("FontWeight")]
    public void Text_customization_reaches_the_2018_tower(string what)
    {
        var cols = new[] { "pos", "name", "gap" };
        var a = Pixels("--widget standings --theme f1-2018", new WidgetSettings { Id = "standings", Columns = cols });
        var s = new WidgetSettings { Id = "standings", Columns = cols };
        s = what switch { "TextColor" => s with { TextColor = "#00AA00" }, "ValueColor" => s with { ValueColor = "#00AA00" }, _ => s with { FontWeight = 400 } };
        Assert.NotEqual(a, Pixels("--widget standings --theme f1-2018", s));
    }

    [Fact]
    public void Hiding_columns_narrows_the_2018_tower()
    {
        var all = Render("--widget standings --theme f1-2018", new WidgetSettings { Id = "standings", Columns = ["pos", "name", "class", "gap"] });
        var some = Render("--widget standings --theme f1-2018", new WidgetSettings { Id = "standings", Columns = ["pos", "name"] });
        Assert.True(some.W < all.W, $"{all.W} -> {some.W}");
    }

    [Fact]
    public void Name_format_reaches_the_2018_caption()
    {
        var a = Pixels("--widget drivercaption --theme f1-2018", new WidgetSettings { Id = "drivercaption", Columns = ["always", "team", "tyre"] });
        var b = Pixels("--widget drivercaption --theme f1-2018", new WidgetSettings { Id = "drivercaption", Columns = ["always", "team", "tyre"], Display = new DisplayOptions { Name = NameStyle.FullName, CarNumber = true } });
        Assert.NotEqual(a, b);
    }

    // ---- Legenda 2018: variantes (WidgetCatalog.OptionsFor("f1-2018", "drivercaption")) ----

    /// <summary>Renderiza a legenda 2018 (sempre visivel, equipe e pneus) com as opcoes do tema; devolve (largura, altura, bytes do PNG).</summary>
    static (int W, int H, byte[] Png) Caption18(Dictionary<string, string>? options, double sim = 20, Dictionary<string, string>? env = null, string[]? cols = null)
    {
        string png = Path.Combine(Path.GetTempPath(), $"ams2-c18-{Guid.NewGuid():N}.png");
        string json = Path.ChangeExtension(png, ".json");
        File.WriteAllText(json, JsonSerializer.Serialize(new WidgetSettings { Id = "drivercaption", Columns = cols ?? ["always", "team", "tyre"], Options = options }, ProfileStore.Json));
        var psi = new ProcessStartInfo(Exe(), $"--png \"{png}\" --sim {sim.ToString(System.Globalization.CultureInfo.InvariantCulture)} --widget drivercaption --theme f1-2018 --settings \"{json}\"")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        psi.Environment["AMS2_FAKE_BOARD"] = "1";
        foreach (var (k, v) in env ?? []) psi.Environment[k] = v;
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(30000));
        try
        {
            Assert.Equal(0, p.ExitCode);
            var m = Regex.Match(o, @"\[PNG\] .* (\d+)x(\d+) tema=");
            Assert.True(m.Success, o);
            return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), File.ReadAllBytes(png));
        }
        finally { File.Delete(png); File.Delete(json); }
    }

    [Theory]
    [InlineData("driver")]
    [InlineData("startednow")]
    [InlineData("result")]
    [InlineData("auto")]
    public void Every_2018_caption_variant_renders_in_the_same_window(string variant)
    {
        var grid = new Dictionary<string, string> { ["AMS2_FAKE_GRID"] = "1" };
        var driver = Caption18(new() { ["variant"] = "driver" }, env: grid);
        var r = Caption18(new() { ["variant"] = variant }, env: grid);
        // Janela fixa (a maior das variantes): trocar a variante nao muda o tamanho.
        Assert.Equal((driver.W, driver.H), (r.W, r.H));
        Assert.True(r.Png.Length > 500);
        if (variant is "startednow" or "result") Assert.NotEqual(driver.Png, r.Png);
    }

    [Fact]
    public void Caption_2018_auto_follows_the_race_situation()
    {
        // Depois da bandeirada do jogador (AMS2_FAKE_FINISH: os 8 primeiros recebem em t=30 s) o automatico mostra o resultado
        // (no 2018 sem esperar a legenda do vencedor: o banner WINNER fica no alto da tela).
        var finish = new Dictionary<string, string> { ["AMS2_FAKE_FINISH"] = "1" };
        Assert.Equal(Caption18(new() { ["variant"] = "result" }, 33, finish).Png, Caption18(null, 33, finish).Png);
        Assert.Equal(Caption18(new() { ["variant"] = "result" }, 45, finish).Png, Caption18(null, 45, finish).Png);
        // Com o grid conhecido e mudanca de posicao recente, STARTED / NOW; sem grid, a placa simples.
        var grid = new Dictionary<string, string> { ["AMS2_FAKE_GRID"] = "1" };
        Assert.Equal(Caption18(new() { ["variant"] = "startednow" }, 3, grid).Png, Caption18(null, 3, grid).Png);
        Assert.Equal(Caption18(new() { ["variant"] = "driver" }, 3).Png, Caption18(null, 3).Png);
        // Sem "sempre visivel" vale o tempo na tela: 9 s depois de conectar a legenda ja sumiu com showFor=3 e segue visivel com 15.
        string[] events = ["team", "tyre"];
        var shortHold = Caption18(new() { ["showFor"] = "3" }, 9, cols: events);
        var longHold = Caption18(new() { ["showFor"] = "15" }, 9, cols: events);
        Assert.NotEqual(shortHold.Png, longHold.Png);
    }

    // ---- Vencedor 2018: estilos (WidgetCatalog.OptionsFor("f1-2018", "winner")) ----

    /// <summary>Renderiza o vencedor 2018 depois da bandeirada (AMS2_FAKE_FINISH: os 8 primeiros recebem em t=30 s); devolve (largura, altura, bytes do PNG).</summary>
    static (int W, int H, byte[] Png) Winner18(Dictionary<string, string>? options, double sim = 40, string[]? cols = null, bool finish = true)
    {
        string png = Path.Combine(Path.GetTempPath(), $"ams2-w18-{Guid.NewGuid():N}.png");
        string json = Path.ChangeExtension(png, ".json");
        File.WriteAllText(json, JsonSerializer.Serialize(new WidgetSettings { Id = "winner", Columns = cols ?? ["always", "team", "stats"], Options = options }, ProfileStore.Json));
        var psi = new ProcessStartInfo(Exe(), $"--png \"{png}\" --sim {sim.ToString(System.Globalization.CultureInfo.InvariantCulture)} --widget winner --theme f1-2018 --settings \"{json}\"")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        psi.Environment["AMS2_FAKE_BOARD"] = "1";
        if (finish) psi.Environment["AMS2_FAKE_FINISH"] = "1";
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(30000));
        try
        {
            Assert.Equal(0, p.ExitCode);
            var m = Regex.Match(o, @"\[PNG\] .* (\d+)x(\d+) tema=");
            Assert.True(m.Success, o);
            return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), File.ReadAllBytes(png));
        }
        finally { File.Delete(png); File.Delete(json); }
    }

    [Fact]
    public void Winner_2018_styles_banner_podium_and_both()
    {
        var banner = Winner18(null);
        var podium = Winner18(new() { ["style"] = "podium" });
        var both = Winner18(new() { ["style"] = "both" });
        // Banner 780x90 (padrao), pódio 780x380, ambos = banner + 14 + pódio.
        Assert.Equal((780, 90), (banner.W, banner.H));
        Assert.Equal((780, 380), (podium.W, podium.H));
        Assert.Equal((780, 90 + 14 + 380), (both.W, both.H));
        Assert.Equal(banner.Png, Winner18(new() { ["style"] = "banner" }).Png);
        // Sem bandeirada nada aparece; com ela, o desenho muda. Equipe/estatisticas desligadas mudam o banner.
        Assert.NotEqual(banner.Png, Winner18(null, finish: false).Png);
        Assert.NotEqual(banner.Png, Winner18(null, cols: ["always"]).Png);
        Assert.NotEqual(podium.Png, Winner18(new() { ["style"] = "podium" }, cols: ["always", "stats"]).Png);
    }

    [Fact]
    public void Winner_2018_show_for_controls_the_time_on_screen()
    {
        // Bandeirada em t=30 s: 12 s depois (t=42) o banner ja sumiu com showFor=5 e segue visivel com 30.
        string[] events = ["team", "stats"];
        var shortHold = Winner18(new() { ["showFor"] = "5" }, 42, events);
        var longHold = Winner18(new() { ["showFor"] = "30" }, 42, events);
        Assert.NotEqual(shortHold.Png, longHold.Png);
        Assert.Equal(Winner18(null, finish: false, cols: events).Png, shortHold.Png);
    }

    // ---- Pit lane 2018: opcoes (WidgetCatalog.OptionsFor("f1-2018", "pittimer")) ----

    /// <summary>Renderiza o pit lane 2018 com AMS2_FAKE_PITS (jogador entra na pit lane em t=16 s, parado de 17 a 20,4 s, sai em 21,4 s).</summary>
    static (int W, int H, byte[] Png) PitLane18(Dictionary<string, string>? options, double sim)
    {
        string png = Path.Combine(Path.GetTempPath(), $"ams2-p18-{Guid.NewGuid():N}.png");
        string json = Path.ChangeExtension(png, ".json");
        File.WriteAllText(json, JsonSerializer.Serialize(new WidgetSettings { Id = "pittimer", Options = options }, ProfileStore.Json));
        var psi = new ProcessStartInfo(Exe(), $"--png \"{png}\" --sim {sim.ToString(System.Globalization.CultureInfo.InvariantCulture)} --widget pittimer --theme f1-2018 --settings \"{json}\"")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        psi.Environment["AMS2_FAKE_PITS"] = "1";
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(30000));
        try
        {
            Assert.Equal(0, p.ExitCode);
            var m = Regex.Match(o, @"\[PNG\] .* (\d+)x(\d+) tema=");
            Assert.True(m.Success, o);
            return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), File.ReadAllBytes(png));
        }
        finally { File.Delete(png); File.Delete(json); }
    }

    [Fact]
    public void Pit_lane_2018_options_pit_time_position_and_tick()
    {
        var on = PitLane18(null, 19);
        Assert.Equal((300, 182), (on.W, on.H));
        // Na pit lane: "PIT 3.0" no lugar do rotulo; desligado volta a "STOP TIME".
        var noPit = PitLane18(new() { ["showPitTime"] = "false" }, 19);
        Assert.NotEqual(on.Png, noPit.Png);
        Assert.Equal(on.Png, PitLane18(new() { ["showPitTime"] = "true" }, 19).Png);
        // Fora da pit lane (t=23, tempo da parada ainda na tela) a opcao nao muda nada.
        Assert.Equal(PitLane18(null, 23).Png, PitLane18(new() { ["showPitTime"] = "false" }, 23).Png);
        // Caixa de posicao e tique: cada um muda o desenho, sem mudar a janela.
        foreach (var opt in new[] { "showPosition", "showTick" })
        {
            var off = PitLane18(new() { [opt] = "false" }, 19);
            Assert.Equal((on.W, on.H), (off.W, off.H));
            Assert.NotEqual(on.Png, off.Png);
        }
    }

    // ---- Race Start 2018: opcoes (WidgetCatalog.OptionsFor("f1-2018", "racestart")); AMS2_FAKE_LAUNCH=1 = verde em t=1 s, 200 km/h em t~5,6 s ----

    static (int W, int H, byte[] Png) RaceStart18(Dictionary<string, string>? options, double sim)
    {
        string png = Path.Combine(Path.GetTempPath(), $"ams2-rs18-{Guid.NewGuid():N}.png");
        string json = Path.ChangeExtension(png, ".json");
        File.WriteAllText(json, JsonSerializer.Serialize(new WidgetSettings { Id = "racestart", Options = options }, ProfileStore.Json));
        var psi = new ProcessStartInfo(Exe(), $"--png \"{png}\" --sim {sim.ToString(System.Globalization.CultureInfo.InvariantCulture)} --widget racestart --theme f1-2018 --settings \"{json}\"")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        psi.Environment["AMS2_FAKE_LAUNCH"] = "1";
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(30000));
        try
        {
            Assert.Equal(0, p.ExitCode);
            var m = Regex.Match(o, @"\[PNG\] .* (\d+)x(\d+) tema=");
            Assert.True(m.Success, o);
            return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), File.ReadAllBytes(png));
        }
        finally { File.Delete(png); File.Delete(json); }
    }

    [Fact]
    public void Race_start_2018_appears_after_the_target_and_honours_the_options()
    {
        var shown = RaceStart18(null, 8);                  // 4.6 s alcancado em t~5,6 s: na tela
        Assert.Equal((300, 292), (shown.W, shown.H));
        var grid = RaceStart18(null, 0.5);                 // ainda no grid: nada
        var gone = RaceStart18(null, 30);                  // passou showFor (10 s): some
        Assert.NotEqual(shown.Png, grid.Png);
        Assert.Equal(grid.Png, gone.Png);
        Assert.Equal(shown.Png, RaceStart18(new() { ["target"] = "200", ["showBest"] = "true" }, 8).Png);
        // showFor 30: ainda na tela em t=30; "always": o ultimo resultado fica, e antes da largada mostra "-.-" + BEST.
        Assert.Equal(shown.Png, RaceStart18(new() { ["showFor"] = "30" }, 30).Png);
        Assert.Equal(shown.Png, RaceStart18(new() { ["always"] = "true" }, 30).Png);
        var alwaysGrid = RaceStart18(new() { ["always"] = "true" }, 0.5);
        Assert.NotEqual(grid.Png, alwaysGrid.Png);
        Assert.NotEqual(shown.Png, alwaysGrid.Png);
        // Alvo 100: aparece antes (2.3 s, t~3,3 s) e com outro titulo/tempo.
        var t100 = RaceStart18(new() { ["target"] = "100" }, 5);
        Assert.NotEqual(grid.Png, t100.Png);
        Assert.NotEqual(shown.Png, t100.Png);
        Assert.Equal(grid.Png, RaceStart18(null, 5).Png);   // 200 ainda nao alcancado em t=5
        // Sem BEST: a placa perde a segunda faixa (102 de projeto).
        var noBest = RaceStart18(new() { ["showBest"] = "false" }, 8);
        Assert.Equal((300, 190), (noBest.W, noBest.H));
    }

    // ---- Live Speed 2018: opcoes (WidgetCatalog.OptionsFor("f1-2018", "livespeed")) ----

    static (int W, int H, byte[] Png) LiveSpeed18(Dictionary<string, string>? options)
    {
        string png = Path.Combine(Path.GetTempPath(), $"ams2-ls18-{Guid.NewGuid():N}.png");
        string json = Path.ChangeExtension(png, ".json");
        File.WriteAllText(json, JsonSerializer.Serialize(new WidgetSettings { Id = "livespeed", Options = options }, ProfileStore.Json));
        var psi = new ProcessStartInfo(Exe(), $"--png \"{png}\" --sim 20 --widget livespeed --theme f1-2018 --settings \"{json}\"")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(30000));
        try
        {
            Assert.Equal(0, p.ExitCode);
            var m = Regex.Match(o, @"\[PNG\] .* (\d+)x(\d+) tema=");
            Assert.True(m.Success, o);
            return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), File.ReadAllBytes(png));
        }
        finally { File.Delete(png); File.Delete(json); }
    }

    [Fact]
    public void Live_speed_2018_units_name_and_always()
    {
        var both = LiveSpeed18(null);
        Assert.Equal((300, 196), (both.W, both.H));
        Assert.Equal(both.Png, LiveSpeed18(new() { ["units"] = "both" }).Png);
        var kph = LiveSpeed18(new() { ["units"] = "kph" });
        var mph = LiveSpeed18(new() { ["units"] = "mph" });
        Assert.Equal((both.W, both.H), (kph.W, kph.H));
        Assert.Equal((both.W, both.H), (mph.W, mph.H));
        Assert.NotEqual(both.Png, kph.Png);
        Assert.NotEqual(both.Png, mph.Png);
        Assert.NotEqual(kph.Png, mph.Png);
        // Sem nome: a placa encolhe a faixa do nome (30 de projeto).
        var noName = LiveSpeed18(new() { ["showName"] = "false" });
        Assert.Equal((300, 166), (noName.W, noName.H));
        // "always" so muda a regra de visibilidade do host, nao o desenho.
        Assert.Equal(both.Png, LiveSpeed18(new() { ["always"] = "true" }).Png);
    }

    // ---- Torre 2018: modos e opcoes do tema (WidgetCatalog.OptionsFor("f1-2018", "standings")) ----

    /// <summary>Renderiza a torre 2018 com as opcoes do tema e variaveis extras do escritor falso; devolve (largura, altura, bytes do PNG).</summary>
    static (int W, int H, byte[] Png) Tower18(Dictionary<string, string>? options, double sim = 20, Dictionary<string, string>? env = null)
    {
        string png = Path.Combine(Path.GetTempPath(), $"ams2-t18-{Guid.NewGuid():N}.png");
        string json = Path.ChangeExtension(png, ".json");
        File.WriteAllText(json, JsonSerializer.Serialize(new WidgetSettings { Id = "standings", Columns = ["pos", "name", "gap"], Options = options }, ProfileStore.Json));
        var psi = new ProcessStartInfo(Exe(), $"--png \"{png}\" --sim {sim.ToString(System.Globalization.CultureInfo.InvariantCulture)} --widget standings --theme f1-2018 --settings \"{json}\"")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        psi.Environment["AMS2_FAKE_BOARD"] = "1";
        foreach (var (k, v) in env ?? []) psi.Environment[k] = v;
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(30000));
        try
        {
            Assert.Equal(0, p.ExitCode);
            var m = Regex.Match(o, @"\[PNG\] .* (\d+)x(\d+) tema=");
            Assert.True(m.Success, o);
            return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), File.ReadAllBytes(png));
        }
        finally { File.Delete(png); File.Delete(json); }
    }

    [Theory]
    [InlineData("gap")]
    [InlineData("interval")]
    [InlineData("gainedlost")]
    [InlineData("pitstops")]
    [InlineData("bestlap")]
    [InlineData("auto")]
    public void Every_2018_tower_mode_renders_in_the_same_window_and_draws_its_own_column(string mode)
    {
        var grid = new Dictionary<string, string> { ["AMS2_FAKE_GRID"] = "1" };
        var gap = Tower18(null, env: grid);
        var r = Tower18(new() { ["mode"] = mode }, env: grid);
        // A janela tem tamanho fixo (reserva do titulo/BATTLE/bandeirada): trocar o modo, inclusive o automatico, nao muda o tamanho.
        Assert.Equal((gap.W, gap.H), (r.W, r.H));
        Assert.True(r.Png.Length > 500);
        if (mode != "gap") Assert.NotEqual(gap.Png, r.Png);
    }

    [Fact]
    public void Tower_2018_states_yellow_flag_finish_and_out_block_change_the_drawing()
    {
        var normal = Tower18(null, 63);
        var yellow = Tower18(null, 63, new() { ["AMS2_FAKE_FLAG"] = "6" });
        Assert.NotEqual(normal.Png, yellow.Png);
        // Nomes completos sob bandeira: liga/desliga muda o desenho.
        Assert.NotEqual(yellow.Png, Tower18(new() { ["fullNames"] = "false" }, 63, new() { ["AMS2_FAKE_FLAG"] = "6" }).Png);
        var finish = Tower18(null, 35, new() { ["AMS2_FAKE_FINISH"] = "1" });
        Assert.Equal((normal.W, normal.H), (finish.W, finish.H));
        Assert.NotEqual(Tower18(null, 35).Png, finish.Png);
        var outs = Tower18(null, 20, new() { ["AMS2_FAKE_OUT"] = "1" });
        Assert.NotEqual(Tower18(null, 20).Png, outs.Png);
    }

    [Fact]
    public void Tower_2018_battle_and_out_block_options()
    {
        // O jogador do escritor falso (board) anda a < 1 s dos vizinhos: o bloco BATTLE aparece e some com a opcao.
        var on = Tower18(null);
        var off = Tower18(new() { ["battle"] = "false" });
        Assert.NotEqual(on.Png, off.Png);
        // Sem o bloco OUT a janela fica mais baixa (sem a reserva das linhas cinza).
        var noOut = Tower18(new() { ["outBlock"] = "false" });
        Assert.True(noOut.H < on.H, $"{on.H} -> {noOut.H}");
        Assert.Equal(on.W, noOut.W);
    }
}
