using System.Diagnostics;
using Ams2.Shared.Ipc;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

/// <summary>
/// Filtro de visibilidade por tipo de sessao no host real (--fake): o escritor falso padrao e uma corrida, o AMS2_FAKE_QUALI=1 uma
/// classificacao. O host informa o grupo da sessao e os widgets escondidos pelo filtro no estado do IPC. Nunca roda com o iRacing aberto.
/// </summary>
[Collection("host")]
public sealed class SessionFilterTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ams2-it-" + Guid.NewGuid().ToString("N"));
    readonly string _pipe = "ams2-it-" + Guid.NewGuid().ToString("N");
    Process? _host;

    static bool IracingRunning => Process.GetProcessesByName("iRacingSim64DX11").Length + Process.GetProcessesByName("iRacingSim64").Length > 0;

    static string HostExe()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Ams2.OverlayHost"))) dir = dir.Parent;
        Assert.NotNull(dir);
        string cfg = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";
        var exe = Path.Combine(dir!.FullName, "src", "Ams2.OverlayHost", "bin", cfg, "net9.0-windows", "Ams2.OverlayHost.exe");
        Assert.True(File.Exists(exe), exe);
        return exe;
    }

    static async Task WaitFor(Func<Task<bool>> cond, int ms, string what)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (!await cond() && DateTime.UtcNow < until) await Task.Delay(50);
        Assert.True(await cond(), what);
    }

    async Task<IpcClient> StartHost(bool quali)
    {
        var psi = new ProcessStartInfo(HostExe(), $"--fake --seconds 20 --pipe {_pipe} --profiles-dir \"{_dir}\"") { UseShellExecute = false, CreateNoWindow = true };
        psi.Environment["AMS2_FAKE_QUALI"] = quali ? "1" : "0";
        _host = Process.Start(psi)!;
        var client = new IpcClient(_pipe);
        client.Start();
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!client.Connected && DateTime.UtcNow < until) await Task.Delay(25);
        Assert.True(client.Connected, "cliente nao conectou ao host");
        return client;
    }

    [Fact]
    public async Task Race_session_hides_quali_widgets_and_honours_the_widget_sessions()
    {
        if (IracingRunning) return;
        using var client = await StartHost(quali: false);
        await WaitFor(async () => (await client.SendAsync(IpcCommands.GetState))!.Session == SessionIds.Race, 8000, "host nao viu a sessao de corrida");

        var st = (await client.SendAsync(IpcCommands.GetState))!;
        Assert.Equal(["qualilap", "qualiresult", "qualitower"], st.HiddenBySession.Order());   // existentes: todas as sessoes por padrao

        st = (await client.SendAsync(IpcCommands.SetWidget, m => m with { Widget = "fuel", Patch = new WidgetPatch { Sessions = ["qualify"] } }))!;
        Assert.Equal(["qualify"], st.Widgets.Single(w => w.Id == "fuel").Sessions);
        Assert.Contains("fuel", st.HiddenBySession);

        st = (await client.SendAsync(IpcCommands.SetWidget, m => m with { Widget = "qualitower", Patch = new WidgetPatch { Sessions = ["qualify", "race"] } }))!;
        Assert.DoesNotContain("qualitower", st.HiddenBySession);

        st = (await client.SendAsync(IpcCommands.SetWidget, m => m with { Widget = "fuel", Patch = new WidgetPatch { Sessions = [] } }))!;   // restaurar padrao
        Assert.Null(st.Widgets.Single(w => w.Id == "fuel").Sessions);
        Assert.DoesNotContain("fuel", st.HiddenBySession);
    }

    [Fact]
    public async Task Quali_session_shows_quali_widgets()
    {
        if (IracingRunning) return;
        using var client = await StartHost(quali: true);
        await WaitFor(async () => (await client.SendAsync(IpcCommands.GetState))!.Session == SessionIds.Qualify, 8000, "host nao viu a sessao de classificacao");
        var st = (await client.SendAsync(IpcCommands.GetState))!;
        Assert.Empty(st.HiddenBySession);
        st = (await client.SendAsync(IpcCommands.SetWidget, m => m with { Widget = "standings", Patch = new WidgetPatch { Sessions = ["race"] } }))!;
        Assert.Equal(["standings"], st.HiddenBySession);
    }

    [Fact]
    public void Png_fake_quali_prints_the_quali_table_and_the_player_lap()
    {
        if (IracingRunning) return;
        var png = Path.Combine(_dir, "q.png");
        Directory.CreateDirectory(_dir);
        var psi = new ProcessStartInfo(HostExe(), $"--png \"{png}\" --widget qualitower --sim 45") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        psi.Environment["AMS2_FAKE_QUALI"] = "1";
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(20000));
        var lines = output.Split('\n', StringSplitOptions.TrimEntries);
        Assert.Contains(lines, l => l.StartsWith("[QUALI] sessao=qualify") && l.Contains("linhas=20"));
        Assert.Contains("[QUALI] 1 #0 Michael Schumacher TimeSet 30.102 0.000", lines);
        Assert.Contains(lines, l => l.StartsWith("[QUALI] 5 #5 Player TimeSet 30.480 +0.378"));
        Assert.Contains(lines, l => l.StartsWith("[QUALI] 17 #17") && l.Contains("InPit"));
        Assert.Contains(lines, l => l.StartsWith("[QUALI] 20 #19") && l.Contains("NoTime"));
        Assert.Contains(lines, l => l.StartsWith("[QUALILAP] resultado") && l.Contains("pos=5") && l.Contains("melhorou=True"));
        Assert.True(File.Exists(png));
    }

    public void Dispose()
    {
        try { if (_host is { HasExited: false }) { _host.Kill(); _host.WaitForExit(2000); } } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }
}
