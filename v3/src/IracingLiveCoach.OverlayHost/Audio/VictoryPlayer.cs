using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace IracingLiveCoach.OverlayHost.Audio;

/// <summary>
/// Plays the user's victory theme (mp3/wav/m4a/wma) through NAudio + Windows Media Foundation on the
/// default audio device. One track at a time: starting a new one (or <see cref="Stop"/>) stops and
/// releases the previous. Never throws -- a missing file, an unsupported codec or a busy audio
/// device just returns false. (Windows' old MCI layer rejected the user's mp3, hence Media Foundation.)
/// </summary>
public sealed class VictoryPlayer : IDisposable
{
    private readonly object _lock = new();
    private WaveOutEvent? _output;
    private MediaFoundationReader? _reader;

    /// <summary>Starts the track from the beginning at <paramref name="volumePct"/> (0..100).</summary>
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
                // Release only if this is still the current track (a newer Play may have replaced it).
                output.PlaybackStopped += (_, _) => { lock (_lock) { if (ReferenceEquals(_output, output)) Release(); } };
                output.Init(volume);
                output.Play();
                return true;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or IOException or NAudio.MmException)
            {
                Release();
                return false;
            }
        }
    }

    public void Stop()
    {
        lock (_lock) Release();
    }

    private void Release()
    {
        var output = _output;
        var reader = _reader;
        _output = null;
        _reader = null;
        try { output?.Stop(); } catch { /* already stopped */ }
        output?.Dispose();
        reader?.Dispose();
    }

    public void Dispose() => Stop();
}
