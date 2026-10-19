using System.Globalization;
using System.Speech.Synthesis;

namespace IracingLiveCoach.OverlayHost.Coach;

/// <summary>SAPI (System.Speech) coach voice with a small priority queue. Never throws.</summary>
public sealed class SapiCoachVoice : ICoachVoice
{
    private readonly object _lock = new();
    private readonly List<(string Text, int Priority, long Seq)> _queue = new();
    private SpeechSynthesizer? _synth;
    private string? _current;
    private long _seq;
    private int _volume = 100;
    private int _rate;

    public SapiCoachVoice()
    {
        try
        {
            _synth = new SpeechSynthesizer();
            _synth.SpeakStarted += (_, e) => Raise(SpeakStarted, e.Prompt?.ToString() ?? _current ?? "");
            _synth.SpeakCompleted += OnCompleted;
            _synth.SetOutputToDefaultAudioDevice();
            _synth.Volume = _volume;
        }
        catch { _synth?.Dispose(); _synth = null; }
    }

    public bool IsAvailable => _synth != null;
    public event Action<string>? SpeakStarted;
    public event Action<string>? SpeakCompleted;

    public int Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 100); try { if (_synth != null) _synth.Volume = _volume; } catch { } }
    }

    public int Rate
    {
        get => _rate;
        set { _rate = Math.Clamp(value, -10, 10); try { if (_synth != null) _synth.Rate = _rate; } catch { } }
    }

    public IReadOnlyList<string> InstalledVoices
    {
        get
        {
            try { return _synth?.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToList() ?? new List<string>(); }
            catch { return new List<string>(); }
        }
    }

    public string? SelectVoiceForCulture(string cultureTag)
    {
        if (_synth == null || string.IsNullOrWhiteSpace(cultureTag)) return null;
        try
        {
            var wanted = cultureTag.Trim();
            var lang = wanted.Split('-', '_')[0].ToLowerInvariant();
            if (lang is not ("en" or "pt" or "es" or "de" or "fr")) return null;
            var voices = _synth.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo).ToList();
            var pick = voices.FirstOrDefault(v => string.Equals(v.Culture.Name, wanted, StringComparison.OrdinalIgnoreCase))
                       ?? voices.FirstOrDefault(v => v.Culture.TwoLetterISOLanguageName == lang);
            if (pick == null) return null;
            lock (_lock) _synth.SelectVoice(pick.Name);
            return pick.Name;
        }
        catch { return null; }
    }

    /// <summary>Selects an installed voice by exact name. Returns false when missing.</summary>
    public bool SelectVoiceByName(string name)
    {
        if (_synth == null || string.IsNullOrWhiteSpace(name)) return false;
        try
        {
            if (!InstalledVoices.Contains(name)) return false;
            lock (_lock) _synth.SelectVoice(name);
            return true;
        }
        catch { return false; }
    }

    public void Speak(string text, int priority = 0, bool interrupt = false)
    {
        if (_synth == null || string.IsNullOrWhiteSpace(text)) return;
        try
        {
            lock (_lock)
            {
                if (interrupt) { _queue.Clear(); _synth.SpeakAsyncCancelAll(); }
                _queue.Add((text, priority, _seq++));
                if (_current == null) StartNext();
            }
        }
        catch { }
    }

    public void Stop()
    {
        try { lock (_lock) { _queue.Clear(); _synth?.SpeakAsyncCancelAll(); } } catch { }
    }

    private void StartNext()
    {
        if (_synth == null || _queue.Count == 0) { _current = null; return; }
        var next = _queue.OrderByDescending(q => q.Priority).ThenBy(q => q.Seq).First();
        _queue.Remove(next);
        _current = next.Text;
        try { _synth.SpeakAsync(next.Text); }
        catch { _current = null; }
    }

    private void OnCompleted(object? sender, SpeakCompletedEventArgs e)
    {
        string? done;
        lock (_lock) { done = _current; _current = null; StartNext(); }
        Raise(SpeakCompleted, done ?? "");
    }

    private static void Raise(Action<string>? h, string s)
    {
        try { h?.Invoke(s); } catch { }
    }

    public void Dispose()
    {
        try { Stop(); _synth?.Dispose(); } catch { }
        _synth = null;
    }
}
