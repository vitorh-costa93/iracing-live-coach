using System.Diagnostics;
using Ams2.Shared.Ipc;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

/// <summary>
/// Integracao real: o OverlayHost em --fake (escritor falso em processo, sem o jogo) e o IpcClient. As janelas aparecem por poucos
/// segundos. Regra do projeto: nunca abrir o overlay com o iRacing aberto, entao o teste nao faz nada nesse caso.
/// </summary>
public sealed class HostIpcTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ams2-it-" + Guid.NewGuid().ToString("N"));
    readonly string _pipe = "ams2-it-" + Guid.NewGuid().ToString("N");
    Process? _host;

    static bool IracingRunning => Process.GetProcessesByName("iRacingSim64DX11").Length + Process.GetProcessesByName("iRacingSim64").Length > 0;

    static string? FindHostExe()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Ams2.OverlayHost"))) dir = dir.Parent;
        if (dir is null) return null;
        string cfg = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";
        var exe = Path.Combine(dir.FullName, "src", "Ams2.OverlayHost", "bin", cfg, "net9.0-windows", "Ams2.OverlayHost.exe");
        return File.Exists(exe) ? exe : null;
    }

    static async Task WaitFor(Func<bool> cond, int ms, string what)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (!cond() && DateTime.UtcNow < until) await Task.Delay(25);
        Assert.True(cond(), what);
    }

    [Fact]
    public async Task Host_in_fake_mode_applies_ipc_commands_live_and_persists_them()
    {
        if (IracingRunning) return;
        var exe = FindHostExe();
        Assert.NotNull(exe);

        _host = Process.Start(new ProcessStartInfo(exe!, $"--fake --seconds 25 --pipe {_pipe} --profiles-dir \"{_dir}\"") { UseShellExecute = false, CreateNoWindow = true })!;
        using var client = new IpcClient(_pipe);
        var events = new List<IpcMessage>();
        client.EventReceived += e => { lock (events) events.Add(e); };
        client.Start();
        await WaitFor(() => client.Connected, 10000, "cliente nao conectou ao host");

        // Estado inicial: 6 widgets, tema 1998, perfil padrao, dados do escritor falso chegando.
        var st = (await client.SendAsync(IpcCommands.GetState))!;
        Assert.True(st.Fake);
        Assert.Equal("f1-1998", st.Theme);
        Assert.Equal("Padrão", st.ActiveProfile);
        Assert.Equal(WidgetCatalog.All.Select(w => w.Id), st.Widgets.Select(w => w.Id));
        Assert.All(new[] { "f1-1998", "f1-2004", "f1-2010s" }, id => Assert.Contains(st.Themes, t => t.Id == id && t.Available));
        await WaitFor(() => client.SendAsync(IpcCommands.GetState).Result!.GameConnected, 8000, "escritor falso nao conectou");

        // Aplicar um perfil inteiro (o Control Center envia Data) e depois alterar um widget ao vivo.
        var profile = ProfileFactory.CreateDefault("Corrida", "f1-1998", 1920, 1080);
        st = (await client.SendAsync(IpcCommands.ApplyProfile, m => m with { Data = profile }))!;
        Assert.Equal("Corrida", st.ActiveProfile);

        st = (await client.SendAsync(IpcCommands.SetWidget, m => m with
        {
            Widget = "fuel",
            Patch = new WidgetPatch { Visible = false, Scale = 1.5f, X = 100, Y = 120, Opacity = 0.6f, Font = "Segoe UI", Columns = ["laps"] },
        }))!;
        var fuel = st.Widgets.Single(w => w.Id == "fuel");
        Assert.False(fuel.Visible);
        Assert.Equal(1.5f, fuel.Scale);
        Assert.Equal(0.6f, fuel.Opacity);
        Assert.Equal("Segoe UI", fuel.Font);
        Assert.Equal(["laps"], fuel.Columns);

        st = (await client.SendAsync(IpcCommands.SetWidget, m => m with { Widget = "standings", Patch = new WidgetPatch { Rows = 12 } }))!;
        Assert.Equal(12, st.Widgets.Single(w => w.Id == "standings").Rows);
        st = (await client.SendAsync(IpcCommands.SetWidget, m => m with { Widget = "relative", Patch = new WidgetPatch { Order = 0 } }))!;
        Assert.Equal("relative", st.Widgets[0].Id);

        // Modo de edicao liga e desliga.
        Assert.True((await client.SendAsync(IpcCommands.SetEditMode, m => m with { Edit = true }))!.EditMode);
        Assert.False((await client.SendAsync(IpcCommands.SetEditMode, m => m with { Edit = false }))!.EditMode);

        // Erros viram resposta, nao derrubam o host.
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(IpcCommands.SetTheme, m => m with { Theme = "f1-2099" }));
        // Cada tema tem seus perfis: trocar para 2004 e 2010s carrega o perfil padrao do tema, sem tocar no de 1998.
        foreach (var th in new[] { "f1-2004", "f1-2010s" })
        {
            st = (await client.SendAsync(IpcCommands.SetTheme, m => m with { Theme = th }))!;
            Assert.Equal(th, st.Theme);
            Assert.Equal("Padrão", st.ActiveProfile);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(IpcCommands.SetWidget, m => m with { Widget = "nope", Patch = new WidgetPatch() }));
        st = (await client.SendAsync(IpcCommands.SetTheme, m => m with { Theme = "f1-1998" }))!;
        Assert.Equal("f1-1998", st.Theme);

        // Perfil e alteracoes foram gravados em disco (salvamento com atraso curto).
        var store = new ProfileStore(_dir);
        await WaitFor(() => store.Load("f1-1998", "Corrida")?.Get("fuel")?.X == 100, 4000, "alteracao nao foi salva no perfil");
        var saved = store.Load("f1-1998", "Corrida")!;
        Assert.Equal(1.5f, saved.Get("fuel")!.Scale);
        Assert.Equal(12, saved.Get("standings")!.Rows);
        Assert.Equal("Corrida", store.GetActiveProfile("f1-1998"));

        // Reconexao: um segundo cliente novo ve o mesmo estado.
        using var client2 = new IpcClient(_pipe);
        client2.Start();
        await WaitFor(() => client2.Connected, 5000, "segundo cliente nao conectou");
        Assert.Equal("Corrida", (await client2.SendAsync(IpcCommands.GetState))!.ActiveProfile);
    }

    public void Dispose()
    {
        try { if (_host is { HasExited: false }) { _host.Kill(); _host.WaitForExit(2000); } } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }
}
