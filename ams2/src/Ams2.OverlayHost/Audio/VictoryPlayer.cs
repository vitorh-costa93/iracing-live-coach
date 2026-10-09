using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ams2.OverlayHost.Audio;

/// <summary>
/// Toca o tema da vitoria (mp3/wav/m4a/wma) com NAudio + Media Foundation no dispositivo padrao. Uma faixa por vez: iniciar outra
/// (ou <see cref="Stop"/>) para e libera a anterior. Nunca lanca: arquivo ausente, codec nao suportado ou dispositivo ocupado = false.
/// A abertura do arquivo roda em thread propria (<see cref="PlayAsync"/>) para nunca travar o laco de render.
/// </summary>
public sealed class VictoryPlayer : IDisposable
{
    readonly object _lock = new();
    WaveOutEvent? _output;
    MediaFoundationReader? _reader;

    /// <summary>Dispara a faixa em uma thread do pool; erros sao engolidos.</summary>
    public void PlayAsync(string path, int volumePct)
        => Task.Run(() => { try { Play(path, volumePct); } catch (Exception ex) { Console.Error.WriteLine($"[Victory] {ex.GetType().Name}: {ex.Message}"); } });

    public bool Play(string path, int volumePct)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        lock (_lock)
        {
            Release();
            try
            {
                _reader = new MediaFoundationReader(path);
                var volume = new VolumeSampleProvider(_reader.ToSampleProvider()) { Volume = Math.Clamp(volumePct, 0, 100) / 100f };
                var output = new WaveOutEvent();
                _output = output;
                output.PlaybackStopped += (_, _) => { lock (_lock) { if (ReferenceEquals(_output, output)) Release(); } };
                output.Init(volume);
                output.Play();
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Victory] falha ao tocar: {ex.GetType().Name}: {ex.Message}");
                Release();
                return false;
            }
        }
    }

    public void Stop()
    {
        lock (_lock) Release();
    }

    void Release()
    {
        var output = _output;
        var reader = _reader;
        _output = null;
        _reader = null;
        try { output?.Stop(); } catch { /* ja parado */ }
        try { output?.Dispose(); } catch { }
        try { reader?.Dispose(); } catch { }
    }

    public void Dispose() => Stop();
}
