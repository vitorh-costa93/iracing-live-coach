namespace Ams2.Core.Calc.GpxSat;

/// <summary>Como o grupo (linha ou coluna) entra depois do wipe branco.</summary>
public enum GpxAnimKind
{
    /// <summary>Linha a linha, flip vertical (T += tri*FH/100, B -= tri*FH/100). Telas comuns do D2004.</summary>
    Rows,
    /// <summary>Coluna a coluna, altura crescendo de cima para baixo e alfa = ROT%. Standings do D2004.</summary>
    Columns,
    /// <summary>Sem animacao: tudo assentado no primeiro quadro (Digital, Classic).</summary>
    Instant,
}

/// <summary>Fase do logo opcional (Siemens) que fecha a animacao.</summary>
public enum GpxLogoPhase { Hidden, Flip, Shown }

/// <summary>
/// Animador do GPxSAT (spec_digital2004.md secao 2, ANIM = this+0x39d), em funcao do relogio (deterministico: so depende de
/// <c>now</c>). Passo = <see cref="StepMs"/> (GPx: FadingSpeed ms; aqui o padrao e 20 ms = 2x o tempo real pedido).
/// Fase 1: wipe branco da esquerda para a direita em 10 passos (FADE% 10..100). Fase 2: um grupo por vez, 10 passos cada
/// (ROT% 0..90; fora do grupo atual ROT% = 100). Caixa nao assentada = branca e sem texto. Depois do ultimo grupo, o logo (se
/// armado) gira em branco por 10 passos e fica <see cref="LogoMs"/> na tela.
/// </summary>
public sealed class GpxAnimator
{
    public const int Steps = 10;
    public double StepMs { get; set; } = 20;
    public double LogoMs { get; set; } = 6000;

    string? _keyA;
    int _keyB;
    double _start;
    bool _logoArmed;

    public string? Key => _keyA;
    public int Page => _keyB;
    public int Groups { get; private set; }
    public GpxAnimKind Kind { get; private set; } = GpxAnimKind.Rows;
    public bool LogoArmed => _logoArmed;
    public double StartedAt => _start;

    /// <summary>Esquece a tela atual (a proxima Update reinicia e arma o logo).</summary>
    public void Reset() { _keyA = null; _keyB = 0; Groups = 0; _logoArmed = false; }

    /// <summary>
    /// Atualiza a tela exibida. Devolve 1 se <paramref name="keyA"/> mudou (reinicia e arma o logo), 2 se so a pagina
    /// <paramref name="keyB"/> mudou (reinicia sem o logo), 0 se nada mudou. O numero de grupos pode mudar sem reiniciar.
    /// </summary>
    public int Update(string keyA, int keyB, int groups, GpxAnimKind kind, double nowSeconds)
    {
        Groups = Math.Max(0, groups); Kind = kind;
        if (!string.Equals(_keyA, keyA, StringComparison.Ordinal))
        {
            _keyA = keyA; _keyB = keyB; _start = nowSeconds; _logoArmed = true;
            return 1;
        }
        if (_keyB != keyB)
        {
            _keyB = keyB; _start = nowSeconds; _logoArmed = false;
            return 2;
        }
        return 0;
    }

    /// <summary>Move o inicio da animacao atual (previa: quadro em um instante escolhido da animacao).</summary>
    public void StartAt(double startSeconds) => _start = startSeconds;

    /// <summary>Duracao (s) ate tudo assentar, incluindo o giro do logo.</summary>
    public double SettleSeconds => (GroupStart(Groups) + Steps) * StepMs / 1000;

    /// <summary>Passos inteiros decorridos desde o inicio (relogio voltando = 0).</summary>
    public int StepAt(double nowSeconds) => (int)Math.Floor(Math.Max(0, (nowSeconds - _start) * 1000) / Math.Max(1, StepMs) + 1e-9);

    bool Instant => Kind == GpxAnimKind.Instant;

    /// <summary>FADE% da fase 1 (10..100; 100 = wipe concluido).</summary>
    public int FadePct(double now) => Instant ? 100 : Math.Min(100, (StepAt(now) + 1) * 10);

    public bool FadeDone(double now) => Instant || StepAt(now) >= Steps;

    int GroupStart(int g) => Steps + g * Steps;

    /// <summary>ROT% do grupo: 0..90 enquanto anima, 100 antes de comecar (caixa cheia em branco) e depois de assentar.</summary>
    public int RotPct(int group, double now)
    {
        if (Instant) return 100;
        int s = StepAt(now) - GroupStart(group);
        return s is >= 0 and < Steps ? s * 10 : 100;
    }

    /// <summary>Encolhimento do flip: r &gt; 50 ? 100 - r : r (0..50).</summary>
    public static int Tri(int rot) => rot > 50 ? 100 - rot : rot;

    /// <summary>FADEDONE && SETTLED(grupo): cor final e texto.</summary>
    public bool Settled(int group, double now) => Instant || StepAt(now) >= GroupStart(group) + Steps;

    /// <summary>Todos os grupos assentados.</summary>
    public bool AllSettled(double now) => Instant || StepAt(now) >= GroupStart(Groups);

    /// <summary>Grupo animando agora (-1 = nenhum).</summary>
    public int ActiveGroup(double now)
    {
        if (Instant) return -1;
        int s = StepAt(now) - Steps;
        return s >= 0 && s / Steps < Groups ? s / Steps : -1;
    }

    /// <summary>Fase do logo: com <paramref name="enabled"/> e armado (keyA mudou), gira em branco depois do ultimo grupo e fica LogoMs.</summary>
    public (GpxLogoPhase Phase, int Rot) Logo(double now, bool enabled)
    {
        if (!enabled || !_logoArmed || _keyA is null) return (GpxLogoPhase.Hidden, 0);
        if (Instant)
            return (now - _start) * 1000 < LogoMs ? (GpxLogoPhase.Shown, 100) : (GpxLogoPhase.Hidden, 0);
        int s = StepAt(now) - GroupStart(Groups);
        if (s < 0) return (GpxLogoPhase.Hidden, 0);
        if (s < Steps) return (GpxLogoPhase.Flip, s * 10);
        double shownMs = (now - _start) * 1000 - (GroupStart(Groups) + Steps) * StepMs;
        return shownMs < LogoMs ? (GpxLogoPhase.Shown, 100) : (GpxLogoPhase.Hidden, 0);
    }
}
