using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace IracingLiveCoach.OverlayHost.Coach;

/// <summary>
/// Plays short in-memory sine beeps for the braking countdown. Play(beat): beat 0..2 = low tones rising
/// slightly, beat 3 (or more) = high tone ("brake now").
/// One persistent WaveOutEvent plays a MixingSampleProvider (silence when idle); Play() only adds a mixer
/// input, so it never opens a device nor blocks (safe on the telemetry thread). The device is opened and
/// closed by SetActive, which may block and must be called from a background thread. Never throws.
/// </summary>
public sealed class BrakeTonePlayer : IDisposable
{
    private const int SampleRate = 44100;
    private const int ReopenDelayMs = 3000;
    private static readonly double[] Freqs = { 600, 680, 760, 1400 };
    private readonly object _lock = new();   // SetActive/Dispose/reopen only; never taken by Play
    private readonly float[][] _tones;
    private readonly MixingSampleProvider _mixer =
        new(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1)) { ReadFully = true };
    private WaveOutEvent? _output;
    private volatile bool _ready;
    private bool _wantActive, _disposed, _reopenPending;
    private volatile int _volume = 80;

    public BrakeTonePlayer()
    {
        _tones = Freqs.Select(f => Make(f, f >= 1000 ? 0.28 : 0.12)).ToArray();
    }

    /// <summary>0..100.</summary>
    public int Volume { get => _volume; set => _volume = Math.Clamp(value, 0, 100); }

    /// <summary>True while the output device is open and beeps can be heard.</summary>
    public bool IsReady => _ready;

    /// <summary>Non-blocking: queues the beep on the open output (dropped when inactive).</summary>
    public void Play(int beat)
    {
        if (!_ready) return;
        try
        {
            int vol = _volume;
            if (vol <= 0) return;
            var data = _tones[Math.Clamp(beat, 0, _tones.Length - 1)];
            _mixer.AddMixerInput(new VolumeSampleProvider(new BufferProvider(data)) { Volume = vol / 100f });
        }
        catch { }
    }

    /// <summary>Opens (true) or closes (false) the persistent output. May block on the audio driver:
    /// call it from a background thread, never from the telemetry or render thread.</summary>
    public void SetActive(bool active)
    {
        WaveOutEvent? old = null;
        lock (_lock)
        {
            if (_disposed) return;
            _wantActive = active;
            if (active && _output == null) Open();
            else if (!active && _output != null)
            {
                old = _output;
                _output = null;
                _ready = false;
            }
        }
        Release(old);
        if (!active) { try { _mixer.RemoveAllMixerInputs(); } catch { } }
    }

    private void Open()
    {
        WaveOutEvent? output = null;
        try
        {
            output = new WaveOutEvent { DesiredLatency = 60 };
            output.Init(_mixer);
            var o = output;
            output.PlaybackStopped += (_, _) => OnStopped(o);
            output.Play();
            _output = output;
            _ready = true;
        }
        catch
        {
            _ready = false;
            Release(output);
            ScheduleReopen();
        }
    }

    // Raised on the playback thread (device lost/unplugged): never dispose or reopen here.
    private void OnStopped(WaveOutEvent o)
    {
        _ready = false;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            bool reopen;
            lock (_lock)
            {
                if (!ReferenceEquals(_output, o)) return; // already replaced/closed by SetActive/Dispose
                _output = null;
                reopen = _wantActive && !_disposed;
            }
            Release(o);
            if (reopen) lock (_lock) ScheduleReopen();
        });
    }

    // Caller holds _lock.
    private void ScheduleReopen()
    {
        if (_reopenPending || _disposed) return;
        _reopenPending = true;
        Task.Delay(ReopenDelayMs).ContinueWith(_ =>
        {
            lock (_lock)
            {
                _reopenPending = false;
                if (!_disposed && _wantActive && _output == null) Open();
            }
        }, TaskScheduler.Default);
    }

    private static float[] Make(double freq, double seconds)
    {
        int n = (int)(SampleRate * seconds);
        int fade = SampleRate / 200;
        var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            double env = Math.Min(1.0, Math.Min(i, n - 1 - i) / (double)fade);
            d[i] = (float)(Math.Sin(2 * Math.PI * freq * i / SampleRate) * 0.8 * env);
        }
        return d;
    }

    private static void Release(WaveOutEvent? o)
    {
        if (o == null) return;
        try { o.Stop(); } catch { }
        try { o.Dispose(); } catch { }
    }

    public void Dispose()
    {
        WaveOutEvent? old;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _ready = false;
            old = _output;
            _output = null;
        }
        Release(old);
    }

    private sealed class BufferProvider : ISampleProvider
    {
        private readonly float[] _data;
        private int _pos;
        public BufferProvider(float[] data) { _data = data; WaveFormat = Format; }
        private static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);
        public WaveFormat WaveFormat { get; }
        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, _data.Length - _pos);
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
    }
}
