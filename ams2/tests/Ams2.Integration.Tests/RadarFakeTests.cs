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
    public void Fake_radar_native_style_is_the_default_and_only_sees_the_two_cars_alongside_at_3_s()
    {
        // t = 3 s: carros a direita (lateral 3,2 m -> gap 1,2 m) e a esquerda (4,4 m -> gap 2,4 m), os dois ao lado; os outros a +/-20 m nao entram (alcance minimo).
        string o = Run("--widget radar --sim 3", out var png);
        try
        {
            Assert.Contains("[RADAR] valido=True alcance=10m carros=2 alertaE=True alertaD=True aoLadoE=True aoLadoD=True distE=2.4 distD=1.2", o);
            Assert.True(new FileInfo(png).Length > 500);
        }
        finally { File.Delete(png); }
    }

    [Fact]
    public void Fake_radar_at_6_s_shows_a_yellow_and_a_green_gap()
    {
        string o = Run("--widget radar --sim 6", out var png);
        try { Assert.Contains("aoLadoE=True aoLadoD=True distE=3.5 distD=5.5", o); }
        finally { File.Delete(png); }
    }

    [Fact]
    public void Fake_radar_panel_style_keeps_the_old_range_and_alerts()
    {
        string o = Run("--widget radar --sim 3 --cols panel", out var png);
        try
        {
            Assert.Contains("[RADAR] valido=True alcance=15m carros=2 alertaE=True alertaD=True", o);
            Assert.Contains("Right Alert", o); Assert.Contains("Left Alert", o);
            Assert.True(new FileInfo(png).Length > 500);
        }
        finally { File.Delete(png); }
    }

    [Fact]
    public void Fake_radar_range_option_brings_the_far_cars_in_on_the_panel_style()
    {
        string o = Run("--widget radar --sim 3 --cols panel --radar-range 25", out var png);
        try { Assert.Contains("alcance=25m carros=4", o); }
        finally { File.Delete(png); }
    }
}
