using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ams2.Integration.Tests;

/// <summary>
/// Medicao automatizada (so --fake, poucos segundos, nunca com o iRacing aberto): o widget Inputs deve ser desenhado perto da taxa do
/// monitor e as entradas amostradas bem acima de 120 Hz, desacopladas do passo de 60 Hz do provider.
/// </summary>
[Collection("host")] // serializa com HostIpcTests: janelas simultaneas disputam GPU e distorcem o fps
public sealed class FpsMeasureTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ams2-fps-" + Guid.NewGuid().ToString("N"));

    static bool IracingRunning => Process.GetProcessesByName("iRacingSim64DX11").Length + Process.GetProcessesByName("iRacingSim64").Length > 0;

    static string? FindHostExe()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Ams2.OverlayHost"))) dir = dir.Parent;
        if (dir is null) return null;
        string cfg = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";
        // Prefere o exe Release (o que o usuario publica): o Debug desenha ~3x mais devagar e nao representa o fps real.
        var release = Path.Combine(dir.FullName, "src", "Ams2.OverlayHost", "bin", "Release", "net9.0-windows", "Ams2.OverlayHost.exe");
        if (File.Exists(release)) return release;
        var exe = Path.Combine(dir.FullName, "src", "Ams2.OverlayHost", "bin", cfg, "net9.0-windows", "Ams2.OverlayHost.exe");
        return File.Exists(exe) ? exe : null;
    }

    static double Rate(string output, string label)
    {
        var m = Regex.Match(output, @"\[FPS\] " + Regex.Escape(label) + @"\s+([\d.,]+)/s");
        Assert.True(m.Success, $"linha '{label}' ausente na saida:\n{output}");
        return double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
    }

    [Theory]
    [InlineData("inputs", "f1-1998")]
    [InlineData("inputgraph", "f1-2004")]
    [InlineData("inputs", "f1-2004")]
    [InlineData("livespeed", "f1-2018")]
    [InlineData("radar", "f1-2004")]
    public void Fast_widgets_render_near_monitor_rate_and_sample_above_120hz(string widget, string theme)
    {
        if (IracingRunning || Process.GetProcessesByName("AMS2AVX").Length > 0) return;
        var exe = FindHostExe();
        Assert.NotNull(exe);
        var psi = new ProcessStartInfo(exe!, $"--fake --theme {theme} --widget {widget} --seconds 5 --measure --pipe ams2-fps-{Guid.NewGuid():N} --profiles-dir \"{_dir}\" --x 40 --y 40")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        psi.Environment["AMS2_FAKE_RADAR"] = "1";
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(20000), "host nao terminou");
        Assert.Equal(0, p.ExitCode);

        double hz = Regex.Match(output, @"monitor=(\d+) Hz") is { Success: true } mm ? double.Parse(mm.Groups[1].Value, CultureInfo.InvariantCulture) : 60;
        double fps = Rate(output, "render " + widget);
        double sampling = Rate(output, "inputs.amostragem");
        double ticks = Rate(output, "provider.tick");

        Assert.True(sampling >= 120, $"amostragem das entradas {sampling:F1}/s < 120\n{output}");
        // Sem simuladores disputando a GPU: detecta regressao para um teto de 60 Hz em monitores de alta taxa.
        // O teste nao mede desempenho em corrida real nem exige saturar monitores acima de 120 Hz.
        double minFps = Math.Min(hz, 120) * 0.7;
        Assert.True(fps >= minFps, $"fps do Inputs {fps:F1} abaixo do minimo {minFps:F0} (monitor {hz} Hz)\n{output}");
        Assert.InRange(ticks, 40, 80); // o provider segue em ~60 Hz: amostragem e render nao dependem dele
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
}
