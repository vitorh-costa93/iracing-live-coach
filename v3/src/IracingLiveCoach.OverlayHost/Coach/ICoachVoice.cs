namespace IracingLiveCoach.OverlayHost.Coach;

/// <summary>Asynchronous spoken output for the coach. Implementations never throw.</summary>
public interface ICoachVoice : IDisposable
{
    /// <summary>0..100.</summary>
    int Volume { get; set; }
    /// <summary>-10..10.</summary>
    int Rate { get; set; }
    bool IsAvailable { get; }
    IReadOnlyList<string> InstalledVoices { get; }
    event Action<string>? SpeakStarted;
    event Action<string>? SpeakCompleted;
    /// <summary>Queues a phrase (higher priority first). interrupt = cancel current speech and clear the queue.</summary>
    void Speak(string text, int priority = 0, bool interrupt = false);
    void Stop();
    /// <summary>Picks an installed voice matching en / pt-BR / es / de / fr; falls back to the default. Returns the voice name or null.</summary>
    string? SelectVoiceForCulture(string cultureTag);
}
