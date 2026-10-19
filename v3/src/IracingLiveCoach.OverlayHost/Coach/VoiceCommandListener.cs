using System.Speech.Recognition;

namespace IracingLiveCoach.OverlayHost.Coach;

/// <summary>
/// Fixed multilingual (en + pt-BR) command grammar. Intents: lastlap, worstcorner, score, focus, repeat,
/// summary, fuel, gapahead, gapbehind, position, laps, pit (answers come in the coach language).
/// Tolerant to a missing microphone or recognizer (IsAvailable = false, Start returns false).
/// </summary>
public sealed class VoiceCommandListener : IDisposable
{
    private static readonly (string Intent, string[] Phrases)[] Commands =
    {
        ("lastlap", new[] { "how was my last lap", "last lap", "como foi minha última volta", "última volta" }),
        ("worstcorner", new[] { "worst corner", "where am I losing time", "pior curva" }),
        ("score", new[] { "my score", "what is my score", "minha nota", "qual minha nota" }),
        ("focus", new[] { "what should I focus on", "focus", "foco", "no que devo focar" }),
        ("repeat", new[] { "repeat", "say that again", "repetir", "repita" }),
        ("summary", new[] { "lap summary", "summary", "give me a summary", "resumo", "resumo da volta" }),
        ("fuel", new[] { "how much fuel", "fuel", "fuel level", "quanto combustível", "quanto combustivel", "combustível" }),
        ("gapahead", new[] { "gap ahead", "gap to the car ahead", "gap à frente", "diferença para frente" }),
        ("gapbehind", new[] { "gap behind", "gap to the car behind", "gap atrás", "diferença para trás" }),
        ("position", new[] { "position", "what position am I", "posição", "qual minha posição" }),
        ("laps", new[] { "laps remaining", "how many laps left", "quantas voltas faltam", "voltas restantes" }),
        ("pit", new[] { "pit this lap", "box", "do I need to pit", "box nesta volta", "preciso parar" }),
    };

    private SpeechRecognitionEngine? _engine;
    private bool _running;
    private readonly object _lock = new();

    public event Action<string>? IntentRecognized;
    public bool IsAvailable { get; private set; }
    public bool IsRunning => _running;
    /// <summary>Minimum recognizer confidence (0..1).</summary>
    public float MinConfidence { get; set; } = 0.6f;

    public VoiceCommandListener()
    {
        try
        {
            var infos = SpeechRecognitionEngine.InstalledRecognizers();
            var info = infos.FirstOrDefault(r => r.Culture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                       ?? infos.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en")
                       ?? infos.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "pt")
                       ?? infos.FirstOrDefault();
            if (info == null) return;
            var engine = new SpeechRecognitionEngine(info);
            foreach (var (intent, phrases) in Commands)
            {
                var gb = new GrammarBuilder { Culture = info.Culture };
                gb.Append(new Choices(phrases));
                engine.LoadGrammar(new Grammar(gb) { Name = intent });
            }
            engine.SpeechRecognized += OnRecognized;
            _engine = engine;
            IsAvailable = true;
        }
        catch { _engine?.Dispose(); _engine = null; IsAvailable = false; }
    }

    public bool Start()
    {
        lock (_lock)
        {
            if (_engine == null || _running) return _running;
            try
            {
                _engine.SetInputToDefaultAudioDevice();
                _engine.RecognizeAsync(RecognizeMode.Multiple);
                _running = true;
            }
            catch { IsAvailable = false; _running = false; }
            return _running;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_engine == null || !_running) return;
            try { _engine.RecognizeAsyncCancel(); } catch { }
            _running = false;
        }
    }

    private void OnRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        try
        {
            if (e.Result.Confidence < MinConfidence) return;
            var intent = e.Result.Grammar?.Name;
            if (!string.IsNullOrEmpty(intent)) IntentRecognized?.Invoke(intent);
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        try { _engine?.Dispose(); } catch { }
        _engine = null;
        IsAvailable = false;
    }
}
