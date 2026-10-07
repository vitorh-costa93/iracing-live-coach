using System.Diagnostics;
using Ams2.Shared.Ipc;
using Ams2.Shared.PlayerNames;

namespace Ams2.Integration.Tests;

/// <summary>Nome de exibicao do jogador: comandos IPC (get/set/clear/aplicar sugeridos), persistencia e --png --player-name. Tudo com o escritor falso.</summary>
[Collection("host")]
public sealed class PlayerNamesIpcTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ams2-pn-it-" + Guid.NewGuid().ToString("N"));
    readonly string _pipe = "ams2-pn-it-" + Guid.NewGuid().ToString("N");
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
    public async Task Ipc_get_set_clear_and_apply_suggested_names_live_and_persisted()
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

        // O host detecta o carro do jogador do escritor falso: modelo sem sufixo, nome do jogo e sugestao (primeiro outro carro do modelo).
        PlayerNameEntry? entry = null;
        await WaitFor(() =>
        {
            var pn = client.SendAsync(IpcCommands.GetPlayerNames).Result!.PlayerNames;
            entry = pn.Entries.FirstOrDefault(e => e.Model == pn.CurrentModel);
            return entry is not null;
        }, 8000, "o host nao detectou o carro do jogador");
        Assert.Equal("Formula Classic Gen2", entry!.Model);
        Assert.Equal("Player", entry.OriginalName);
        Assert.Equal("Michael Schumacher", entry.Suggested);
        Assert.Equal("", entry.Name);

        // set: aplica ao vivo (resposta ja traz o estado) e persiste em disco na hora.
        var st = (await client.SendAsync(IpcCommands.SetPlayerName, m => m with { Model = "formula classic gen2 (M)", Name = "Miko Hanninen" }))!;
        Assert.Equal("Miko Hanninen", st.PlayerNames.Entries.Single(e => e.Model == "Formula Classic Gen2").Name);
        var file = Path.Combine(_dir, PlayerNameStore.FileName);
        Assert.Equal("Miko Hanninen", new PlayerNameStore(_dir).Get("Formula Classic Gen2"));

        // clear
        st = (await client.SendAsync(IpcCommands.ClearPlayerName, m => m with { Model = "Formula Classic Gen2" }))!;
        Assert.Equal("", st.PlayerNames.Entries.Single().Name);
        Assert.Null(new PlayerNameStore(_dir).Get("Formula Classic Gen2"));

        // aplicar sugeridos aos que nao tem nome
        st = (await client.SendAsync(IpcCommands.ApplySuggestedNames))!;
        Assert.Equal("Michael Schumacher", st.PlayerNames.Entries.Single().Name);

        // edicao externa do arquivo chega ao Control Center por evento (o host releia a cada ~1 s)
        lock (events) events.Clear();
        File.WriteAllText(file, "{\"version\":1,\"entries\":[{\"model\":\"Formula Classic Gen2\",\"originalName\":\"Player\",\"suggested\":\"Michael Schumacher\",\"name\":\"Editado\"}]}");
        await WaitFor(() => { lock (events) return events.Any(e => e.Event == IpcEvents.StateChanged && e.State?.PlayerNames.Entries.Any(x => x.Name == "Editado") == true); }, 6000, "evento de estado nao chegou");

        // erros viram resposta
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(IpcCommands.SetPlayerName, m => m with { Name = "Sem modelo" }));
    }

    [Fact]
    public void Png_with_player_name_shows_the_display_name_and_does_not_write_the_user_file()
    {
        if (IracingRunning) return;
        var exe = FindHostExe();
        Assert.NotNull(exe);
        string png = Path.Combine(_dir, "r.png");
        Directory.CreateDirectory(_dir);
        var psi = new ProcessStartInfo(exe!, $"--png \"{png}\" --widget relative --player-name \"Miko Hanninen\" --sim 5") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(20000));
        Assert.Equal(0, p.ExitCode);
        Assert.Contains("HAN (voce)", output);   // sigla derivada do nome novo (o jogo diz "Player")
        Assert.DoesNotContain("PLA (voce)", output);
        Assert.True(File.Exists(png));
    }

    public void Dispose()
    {
        try { if (_host is { HasExited: false }) { _host.Kill(); _host.WaitForExit(2000); } } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }
}
