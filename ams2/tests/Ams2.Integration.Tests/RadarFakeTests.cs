using System.Diagnostics;

namespace Ams2.Integration.Tests;

/// <summary>--png com AMS2_FAKE_RADAR=1: carros orbitando o jogador chegam ao radar (posicao de mundo -> tracker) e o widget desenha algo.</summary>
public sealed class RadarFakeTests
{
    static string Exe()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Ams2.OverlayHost"))) dir = dir.Parent;
        Assert.NotNull(dir);
        string cfg = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";
        return Path.Combine(dir!.FullName, "src", "Ams2.OverlayHost", "bin", cfg, "net9.0-windows", "Ams2.OverlayHost.exe");
    }

    static string Run(string args, out string png)
    {
        png = Path.Combine(Path.GetTempPath(), $"ams2-radar-test-{Guid.NewGuid():N}.png");
        var psi = new ProcessStartInfo(Exe(), $"--png \"{png}\" {args}") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        psi.Environment["AMS2_FAKE_RADAR"] = "1";
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(30000));
        return output;
    }

    [Fact]
    public void Fake_radar_puts_one_car_on_each_side_at_3_s_and_renders_a_png()
    {
        // t = 3 s: o carro 1 esta a direita e o carro 3 a esquerda, os dois ao lado (frente ~0); os outros dois ficam a +/-20 m, fora do alcance de 15 m.
        string o = Run("--widget radar --sim 3", out var png);
        try
        {
            Assert.Contains("[RADAR] valido=True alcance=15m carros=2 alertaE=True alertaD=True", o);
            Assert.Contains("Right Alert", o); Assert.Contains("Left Alert", o);
            Assert.True(new FileInfo(png).Length > 500);
        }
        finally { File.Delete(png); }
    }

    [Fact]
    public void Fake_radar_range_option_brings_the_far_cars_in()
    {
        string o = Run("--widget radar --sim 3 --radar-range 25", out var png);
        try { Assert.Contains("alcance=25m carros=4", o); }
        finally { File.Delete(png); }
    }
}
