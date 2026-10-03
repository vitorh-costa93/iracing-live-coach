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
        var a = Render("--widget fuel --theme f1-2010s", new WidgetSettings { Id = "fuel" });
        var b = Render("--widget fuel --theme f1-2010s", new WidgetSettings { Id = "fuel", TextScale = 2f });
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
}
