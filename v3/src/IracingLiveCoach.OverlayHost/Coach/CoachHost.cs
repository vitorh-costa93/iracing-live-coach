using IracingLiveCoach.Core.Coach;
using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.OverlayHost.Coach;

/// <summary>Owns the CoachEngine and its voice/tone/command I/O. Subscribes to the reader, reloads
/// coach-settings.json when it changes (2 s mtime poll) and never throws into the overlay.
/// Threading: all engine calls go through <c>_gate</c> (the engine is not thread-safe), and nothing slow
/// runs under it or on the telemetry thread: file writes go to a single-writer background queue, and
/// settings load/voice selection/microphone/audio device changes run on background threads serialized
/// by <c>_applyGate</c>; only the swap of engine fields happens under <c>_gate</c>.</summary>
public sealed class CoachHost : IDisposable
{
    /// <summary>Messages with at least this priority interrupt the current speech.</summary>
    public const int InterruptPriority = CoachMessages.PriorityTip;

    /// <summary>LiveTrace copies for the widgets are refreshed at most this often (~15 Hz).</summary>
    private const long TraceRefreshMs = 66;

    private readonly object _gate = new();
    private readonly object _applyGate = new();
    private readonly TelemetryReader _reader;
    private readonly string _settingsPath;
    private readonly CoachWriteQueue _writer = new();
    private readonly EngineerLog _engineerLog;
    private readonly CoachEngine _engine;
    private readonly SapiCoachVoice _voice = new();
    private readonly BrakeTonePlayer _tones = new();
    private readonly VoiceCommandListener _listener = new();
    private readonly System.Threading.Timer _poll;
    private volatile CoachSettings _settings = new();
    private DateTime _settingsStamp = DateTime.MinValue;   // _applyGate
    private string? _appliedVoiceKey;                      // _applyGate
    private CoachContext? _lastCtx;                        // _gate
    private volatile bool _started, _disposed;

    public CoachHost(TelemetryReader reader, string? dataDir = null)
    {
        _reader = reader;
        dataDir ??= CoachSettings.DefaultDirectory;
        _settingsPath = Path.Combine(dataDir, "coach-settings.json");
        _engine = new CoachEngine(dataDir, null, _writer);
        _engineerLog = new EngineerLog(Path.Combine(dataDir, "coach-engineer.log"));
        _engine.EngineerLogLine += _engineerLog.Append;
        _engine.MessageReady += OnMessage;
        _engine.BrakeToneCue += OnCue;
        _listener.IntentRecognized += OnIntent;
        _poll = new System.Threading.Timer(_ => PollSettings(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Read-only access for widgets (LiveTrace, LastReport, SessionScore, ...). Engine state is
    /// updated on the telemetry thread; widgets should only read snapshots.</summary>
    public CoachEngine Engine => _engine;
    public CoachSettings Settings => _settings;
    public event Action<CoachSettings>? SettingsApplied;

    private string? _focusCache;
    private long _focusCacheTick = long.MinValue;
    private IReadOnlyList<TracePoint> _traceCache = Array.Empty<TracePoint>();
    private long _traceCacheTick = long.MinValue;

    /// <summary>Copy of everything the coach widgets draw, taken under the engine lock so the render
    /// thread never reads engine state while the telemetry thread mutates it. The live trace copy is
    /// cached and refreshed at ~15 Hz. Never throws.</summary>
    public CoachSnapshot Snapshot(bool includeTrace)
    {
        try
        {
            lock (_gate)
            {
                long now = Environment.TickCount64;
                if (now - _focusCacheTick > 2000)
                {
                    _focusCacheTick = now;
                    try { _focusCache = _engine.WeeklyFocus; } catch { _focusCache = null; }
                }
                if (includeTrace && now - _traceCacheTick >= TraceRefreshMs)
                {
                    _traceCacheTick = now;
                    _traceCache = _engine.LiveTrace;
                }
                return new CoachSnapshot(
                    _settings,
                    includeTrace ? _traceCache : Array.Empty<TracePoint>(),
                    _engine.Reference is not null,
                    _engine.LastReport, _engine.LastBreakdown, _engine.SessionScore,
                    _engine.LastMessage, _focusCache);
            }
        }
        catch { return CoachSnapshot.Empty; }
    }

    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        _reader.CoachSampleUpdated += OnSample;
        _reader.CoachContextChanged += OnContext;
        _reader.PitLossLog += _engineerLog.Append;
        // First load + the context the reader already knows (if the first SessionInfo came before we
        // subscribed), off the calling thread.
        RunApply(() =>
        {
            ReloadSettings(force: true);
            var ctx = _reader.CurrentCoachContext;
            if (ctx != null) OnContext(ctx);
        });
        _poll.Change(2000, 2000);
    }

    private void OnSample(CoachSample s)
    {
        try
        {
            lock (_gate)
            {
                if (_disposed || !_settings.Enabled) return;
                _engine.Feed(s);
            }
        }
        catch { }
    }

    private void OnContext(CoachContext c)
    {
        try
        {
            lock (_gate)
            {
                _lastCtx = c;
                if (_disposed || !_settings.Enabled) return;
                if (Equals(_engine.Context, c)) return; // same context delivered twice (Start + event)
                _engine.SetContext(c);
            }
        }
        catch { }
    }

    private void OnMessage(CoachMessage m)
    {
        try
        {
            var s = _settings;
            if (s.Enabled && s.VoiceEnabled)
                _voice.Speak(m.Text, m.Priority, ShouldInterrupt(m));
        }
        catch { }
    }

    /// <summary>Corner tips and answers cut the current speech; engineer calls only when flagged (flags/damage).</summary>
    internal static bool ShouldInterrupt(CoachMessage m) =>
        m.Kind == CoachMessageKind.Engineer ? m.Interrupt : m.Priority >= InterruptPriority;

    private void OnCue(BrakeToneCue cue)
    {
        try { var s = _settings; if (s.Enabled && s.BrakeTones) _tones.Play(cue.Beat); } catch { }
    }

    private void OnIntent(string intent)
    {
        try
        {
            string text;
            lock (_gate)
            {
                if (_disposed || !_settings.Enabled) return;
                text = _engine.Answer(intent);
            }
            if (_settings.VoiceEnabled && !string.IsNullOrWhiteSpace(text))
                _voice.Speak(text, CoachMessages.PriorityAnswer, true);
        }
        catch { }
    }

    /// <summary>Runs work on the thread pool, serialized with every other settings apply.</summary>
    private void RunApply(Action work)
    {
        Task.Run(() =>
        {
            try { lock (_applyGate) { if (!_disposed) work(); } } catch { }
        });
    }

    // Timer thread (never the telemetry thread). Skips the tick if an apply is still running.
    private void PollSettings()
    {
        if (!Monitor.TryEnter(_applyGate)) return;
        try { if (!_disposed) ReloadSettings(force: false); }
        catch { }
        finally { Monitor.Exit(_applyGate); }
    }

    // Caller holds _applyGate.
    private void ReloadSettings(bool force)
    {
        DateTime stamp;
        try { stamp = File.Exists(_settingsPath) ? File.GetLastWriteTimeUtc(_settingsPath) : DateTime.MinValue; }
        catch { return; }
        if (_disposed || (!force && stamp == _settingsStamp)) return;
        var s = CoachSettings.TryLoad(_settingsPath);
        if (s == null) return; // locked mid-write / invalid JSON: keep the current config, retry next poll
        _settingsStamp = stamp;
        Apply(s);
        try { SettingsApplied?.Invoke(s); } catch { }
    }

    // Caller holds _applyGate (background thread). Only the engine field swap takes _gate; every
    // device/SAPI call runs outside it, each guarded so one failure does not skip the others.
    private void Apply(CoachSettings s)
    {
        try
        {
            lock (_gate)
            {
                bool wasEnabled = _settings.Enabled && _started;
                _settings = s;
                _engine.Skill = s.Skill;
                _engine.Language = s.Language;
                _engine.CornerTipsEnabled = s.CornerTips;
                _engine.LapSummaryEnabled = s.LapSummary;
                _engine.BrakeTonesEnabled = s.BrakeTones;
                _engine.EngineerEnabled = s.EngineerEnabled;
                _engine.FuelCallsEnabled = s.FuelCalls;
                _engine.FlagCallsEnabled = s.FlagCalls;
                _engine.GapCallsEnabled = s.GapCalls;
                _engine.PitCallsEnabled = s.PitCalls;
                _engine.RejoinCallsEnabled = s.RejoinCalls;
                // Re-enabled after being off: the context event may have been dropped meanwhile.
                if (s.Enabled && !wasEnabled && _lastCtx != null)
                    _engine.SetContext(_lastCtx);
            }
        }
        catch { }

        try { _voice.Volume = s.VoiceVolume; _voice.Rate = s.VoiceRate; } catch { }
        try { _tones.Volume = s.ToneVolume; _tones.SetActive(s.Enabled && s.BrakeTones); } catch { }

        try
        {
            var voiceKey = $"{s.Language}|{s.VoiceName}";
            if (voiceKey != _appliedVoiceKey)
            {
                _appliedVoiceKey = voiceKey;
                if (string.IsNullOrWhiteSpace(s.VoiceName) || !_voice.SelectVoiceByName(s.VoiceName!))
                    _voice.SelectVoiceForCulture(s.Language);
            }
        }
        catch { }

        try
        {
            if (!s.Enabled)
            {
                _voice.Stop();
                _listener.Stop();
            }
            else if (s.VoiceCommands) _listener.Start();
            else _listener.Stop();
        }
        catch { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        try { _reader.CoachSampleUpdated -= OnSample; _reader.CoachContextChanged -= OnContext; _reader.PitLossLog -= _engineerLog.Append; } catch { }
        try { _poll.Dispose(); } catch { }
        // Wait for an in-flight settings apply (it checks _disposed before starting), then release I/O.
        bool applyIdle = Monitor.TryEnter(_applyGate, TimeSpan.FromSeconds(3));
        try
        {
            try { _listener.Dispose(); } catch { }
            try { _voice.Dispose(); } catch { }
            try { _tones.Dispose(); } catch { }
        }
        finally { if (applyIdle) Monitor.Exit(_applyGate); }
        // Flush pending reference/history writes (drains the queue, bounded wait).
        try { _writer.Dispose(); } catch { }
        try { _engineerLog.Dispose(); } catch { }
    }
}
