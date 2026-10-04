using System.Diagnostics;
using System.Globalization;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

/// <summary>A tabela de tamanhos de projeto em WidgetLayout (usada pelo teste de sobreposicao) tem de bater com os widgets reais.</summary>
public sealed class DesignSizeTests
{
    [Fact]
    public void WidgetLayout_design_sizes_match_the_real_widgets()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Ams2.OverlayHost"))) dir = dir.Parent;
        Assert.NotNull(dir);
        string cfg = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";
        var exe = Path.Combine(dir!.FullName, "src", "Ams2.OverlayHost", "bin", cfg, "net9.0-windows", "Ams2.OverlayHost.exe");
        Assert.True(File.Exists(exe), exe);

        // --dump-sizes so instancia os widgets (sem janela, sem GPU, sem jogo).
        using var p = Process.Start(new ProcessStartInfo(exe, "--dump-sizes") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
        string output = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(20000));

        int seen = 0;
        foreach (var line in output.Split((char)10, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var f = line.Split(' ');
            if (f.Length != 4) continue;
            var (w, h) = WidgetLayout.DesignSizes[f[0]][f[1]];
            Assert.Equal((w, h), (float.Parse(f[2], CultureInfo.InvariantCulture), float.Parse(f[3], CultureInfo.InvariantCulture)));
            seen++;
        }
        Assert.Equal(ThemeCatalog.All.Sum(t => WidgetCatalog.ForTheme(t.Id).Count()), seen);
    }
}
