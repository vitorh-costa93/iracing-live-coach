# Coach (equivalente ao Trophi.ai "Mansell AI") — especificação

Objetivo: o overlay V3 passa a incluir um coach de pilotagem completo, aberto junto com o OverlayHost
(sem processo extra). 100% local (sem nuvem, sem login). Referência de produto: trophi.ai.

## Funcionalidades do Trophi → equivalente aqui
| Trophi | Aqui |
|---|---|
| Voz em tempo real (Mansell AI), fala sozinho, analisa a volta anterior e orienta na próxima | `CoachEngine` + `ICoachVoice` (SAPI/System.Speech). Fala só quando pode mudar o resultado, 1 prioridade por vez |
| Prioriza o erro que mais custa tempo | `CornerAnalyzer` ordena por tempo perdido (s) vs volta de referência |
| Live Telemetry Overlay (freio, acelerador, velocidade, marcha, volante; traço do piloto vs referência) | `TraceWidget` (OverlayHost) |
| Post-Lap Analysis / AI Coach Report (curva a curva) | `LapReport` + `CoachReportWidget` |
| Pontuação de técnica na sessão | `TechniqueScorer` (0-100: frenagem, trail-brake, aceleração, consistência) |
| Brake tones (bipes assistivos de ponto de frenagem) | `BrakeToneScheduler` (Core) + `BrakeTonePlayer` (NAudio) |
| Adapta ao nível do piloto, 59 idiomas | `SkillLevel` (Beginner/Intermediate/Advanced) + frases en/pt-BR/es/de/fr; voz SAPI por cultura |
| Perguntas por voz ao coach | `VoiceCommandListener` (System.Speech.Recognition) → `CoachQuery.Answer` |
| Weekly Series prep | `WeeklyFocusStore`: histórico por pista/carro/semana, devolve foco (pior categoria) |
| Off-Track Practice Mode | `CornerDrill`: no box, fala resumo curva a curva da última volta/stint sob demanda |
| Pro Telemetry / compare com especialista | referência = melhor volta válida do piloto salva em disco (sem dados de especialista externos) |

## Camadas
- `v3/src/IracingLiveCoach.Core/Coach/` — lógica pura, testável, sem UI/áudio. Namespace `IracingLiveCoach.Core.Coach`.
- `v3/src/IracingLiveCoach.OverlayHost/Coach/` — voz, bipes, reconhecimento, ligação ao `TelemetryReader`.
- `v3/src/IracingLiveCoach.OverlayHost/Widgets/` — `TraceWidget`, `CoachReportWidget`, `CoachStatusWidget`.
- `v3/src/IracingLiveCoach.ControlCenter/` — aba "Coach" (config).
- Testes: `v3/tests/IracingLiveCoach.Core.Tests/Coach/`.
- Persistência: `%APPDATA%\iracing-live-coach\coach\` (`refs\<track>_<car>.json`, `history.json`, `coach-settings.json`).

## Contrato do Core (nomes fixos, outros agentes dependem deles)
```csharp
public sealed record CoachSample(double SessionTime, int Lap, double LapDistPct /*0..1*/, double SpeedMs,
    double Brake, double Throttle, double SteeringRad, int Gear, double Rpm, bool OnPitRoad, bool IsOnTrack);
public sealed record CoachContext(string TrackId, string CarId, double TrackLengthM, string SessionType);
public enum SkillLevel { Beginner, Intermediate, Advanced }
public enum CoachMessageKind { Briefing, CornerTip, LapSummary, Praise, Answer }
public sealed record CoachMessage(CoachMessageKind Kind, string Key, IReadOnlyDictionary<string,string> Args, int Priority, string Text);
public sealed record CornerReport(int Index, double StartPct, double ApexPct, double EndPct,
    double BrakePointDeltaM, double PeakBrakeDelta, double TrailBrakeDeltaM, double ThrottleOnDeltaM,
    double MinSpeedDeltaKmh, double TimeLostSec, string MainIssue /*enum name*/);
public sealed record LapReport(int Lap, double LapTimeSec, bool Valid, double DeltaToRefSec,
    IReadOnlyList<CornerReport> Corners, int TechniqueScore);

public sealed class CoachEngine {
  public CoachEngine(string dataDir, Func<DateTime> clock = null);
  public SkillLevel Skill { get; set; }  public string Language { get; set; } // "en","pt-BR","es","de","fr"
  public bool CornerTipsEnabled, LapSummaryEnabled, BrakeTonesEnabled;
  public void SetContext(CoachContext ctx);               // carrega referência salva
  public void Feed(CoachSample s);                        // chamado a ~60 Hz
  public event Action<CoachMessage> MessageReady;         // fila já priorizada/espaçada
  public event Action<LapReport> LapCompleted;
  public event Action<BrakeToneCue> BrakeToneCue;         // record BrakeToneCue(double MetersToBrakePoint, int Beat)
  public IReadOnlyList<TracePoint> LiveTrace { get; }     // últimos ~N s para o widget
  public ReferenceTrace? Reference { get; }               // por bin de distância (1000 bins)
  public LapReport? LastReport { get; }  public int SessionScore { get; }
  public string Answer(string intent);                    // "lastlap","worstcorner","score","focus","repeat","summary"
}
```
Regras de análise: volta válida = completa, sem pit, sem tempo anômalo (>130% da referência); nova melhor
volta válida substitui a referência e persiste. Curvas detectadas na referência (mínimos locais de
velocidade com queda >= 8 km/h ou frenagem > 0.3). Comparações por distância (bins), nunca por tempo.
Tempo perdido por curva = integral(dx/v_piloto − dx/v_ref) no trecho aproximação→saída.
Mensagens: 1 por curva no máximo, na aproximação da curva da PRÓXIMA volta (~250 m antes, escalado por
velocidade); nunca falar com SpeedMs < 8 ou OnPitRoad; espaçamento mínimo 4 s; no nível Beginner, no
máx. 1 dica a cada 2 curvas; Praise quando a curva melhora vs volta anterior.
