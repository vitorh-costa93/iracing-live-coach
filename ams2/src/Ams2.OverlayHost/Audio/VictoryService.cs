using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.Shared.Victory;

namespace Ams2.OverlayHost.Audio;

/// <summary>
/// Liga detector, escolha do tema pelo nome do piloto, config (victory.json) e player. <see cref="OnModel"/> roda na thread principal
/// do host a cada volta do laco; qualquer falha de audio ou de arquivo e engolida (o overlay nunca cai por causa do tema da vitoria).
/// </summary>
internal sealed class VictoryService : IDisposable
{
    readonly VictoryStore _store;
    readonly VictoryPlayer _player = new();
    readonly VictoryDetector _detector = new();
    readonly bool _autoPlay;
    long _lastFrame = -1;
    double _refreshAt;

    /// <param name="autoPlay">false (--fake) = nunca toca sozinho; so os comandos de teste tocam.</param>
    public VictoryService(VictoryStore store, bool autoPlay) { _store = store; _autoPlay = autoPlay; }

    public VictoryStore Store => _store;

    public void OnModel(OverlayModel model)
    {
        try
        {
            if (model.Frame == _lastFrame) return;
            _lastFrame = model.Frame;
            if (model.Now >= _refreshAt || model.Now < _refreshAt - 5) { _refreshAt = model.Now + 1; _store.Refresh(); }
            if (!model.Connected || model.Session is not { } s) { return; }
            // O detector roda sempre (mantem o estado coerente mesmo com o tema desligado); so o disparo depende da config.
            if (!_detector.Update(s) || !_autoPlay) return;
            var cfg = _store.Current;
            if (!cfg.Enabled) return;
            var theme = VictoryThemeSelector.Select(s.PlayerCar?.Name);
            var path = cfg.Resolve(theme);
            Console.WriteLine($"[Victory] vitoria! tema={theme} arquivo={(path ?? "(nenhum)")}");
            if (path is not null) _player.PlayAsync(path, cfg.VolumePct);
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Victory] {ex.GetType().Name}: {ex.Message}"); }
    }

    public void SetConfig(VictoryConfig config) => _store.Save(config);

    /// <summary>Teste manual: toca o tema pedido (ou o do nome atual do piloto). Devolve a mensagem de erro, ou null se tocou.</summary>
    public string? Test(string? themeName, string? currentPlayerName)
    {
        VictoryTheme theme;
        if (string.IsNullOrWhiteSpace(themeName)) theme = VictoryThemeSelector.Select(currentPlayerName);
        else if (!Enum.TryParse(themeName, ignoreCase: true, out theme) || !Enum.IsDefined(theme)) return $"Tema da vitoria desconhecido: {themeName}.";
        _store.Refresh();
        var cfg = _store.Current;
        var path = cfg.Resolve(theme);
        if (path is null) return theme == VictoryTheme.Default ? "Sem arquivo de audio para o tema Padrão." : $"Sem arquivo de audio para o tema {theme} nem para o Padrão.";
        _player.PlayAsync(path, cfg.VolumePct);
        return null;
    }

    public void Stop() { try { _player.Stop(); } catch { } }

    public void Dispose() => _player.Dispose();
}
