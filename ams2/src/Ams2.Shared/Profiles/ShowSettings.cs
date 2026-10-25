using System.Globalization;
using System.Text.Json.Serialization;

namespace Ams2.Shared.Profiles;

/// <summary>Gatilho da "Exibição" de um widget/peça: quando ele aparece.</summary>
public static class ShowTriggers
{
    /// <summary>Padrão do widget (o comportamento que existia antes da Exibição; gravado como null).</summary>
    public const string Native = "native";
    /// <summary>Sempre na tela (equivale ao antigo "Sempre visível").</summary>
    public const string Always = "always";
    /// <summary>Aparece por <see cref="ShowSettings.DurationSeconds"/> s a cada evento escolhido em <see cref="ShowSettings.Events"/>.</summary>
    public const string Events = "events";
    /// <summary>Aparece por <see cref="ShowSettings.DurationSeconds"/> s a cada <see cref="ShowSettings.PeriodSeconds"/> s.</summary>
    public const string Periodic = "periodic";

    public static readonly IReadOnlyList<string> All = [Native, Always, Events, Periodic];

    public static string Label(string id) => id switch
    {
        Native => "Padrão do widget", Always => "Sempre", Events => "Por evento", Periodic => "Periódico (a cada N s)", _ => id,
    };

    /// <summary>Id conhecido no case canonico; desconhecido ou vazio = null.</summary>
    public static string? Canonical(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(t => string.Equals(t, id.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Eventos que podem disparar um widget (<see cref="ShowTriggers.Events"/>). Instantes calculados no host (Ams2.Core.Calc.ShowEventTracker).</summary>
public static class ShowEvents
{
    public const string LeaderLap = "leaderLap", PlayerLap = "playerLap", BestLap = "bestLap", Position = "position", Pit = "pit",
        PlayerPit = "playerPit", Flag = "flag", Start = "start", Sector = "sector", Finish = "finish", SessionEnd = "sessionEnd";

    public static readonly IReadOnlyList<string> All = [LeaderLap, PlayerLap, BestLap, Position, Pit, PlayerPit, Flag, Start, Sector, Finish, SessionEnd];

    public static string Label(string id) => id switch
    {
        LeaderLap => "Líder fecha a volta", PlayerLap => "Jogador fecha a volta", BestLap => "Nova volta mais rápida",
        Position => "Jogador muda de posição", Pit => "Carro entra nos boxes", PlayerPit => "Jogador para nos boxes",
        Flag => "Bandeira (amarela, azul, vermelha, xadrez)", Start => "Largada (sinal verde)", Sector => "Jogador fecha um setor",
        Finish => "Bandeirada do vencedor", SessionEnd => "Fim da classificação", _ => id,
    };

    /// <summary>Ids conhecidos, sem repeticao, na ordem canonica; nenhum conhecido = null.</summary>
    public static string[]? Canonical(IEnumerable<string>? ids)
    {
        if (ids is null) return null;
        var set = new HashSet<string>(ids.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
        var list = All.Where(set.Contains).ToArray();
        return list.Length == 0 ? null : list;
    }
}

/// <summary>Forma de uma lista: estática (o topo, posicionado como nos prints) ou dinâmica (janela que acompanha o jogador).</summary>
public static class ShowForms
{
    public const string Static = "static", Dynamic = "dynamic";
    public static readonly IReadOnlyList<string> All = [Static, Dynamic];
    public static string Label(string id) => id switch { Static => "Estática (topo da lista)", Dynamic => "Dinâmica (ao redor do jogador)", _ => id };
    public static string? Canonical(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(t => string.Equals(t, id.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// "Exibição" de um widget (ou de uma peça dele, em <see cref="Pieces"/>): quando aparece, por quanto tempo, fades e forma.
/// Todo campo nulo = padrão do widget no tema (<see cref="ShowCatalog"/>), então perfis sem este bloco desenham exatamente como antes.
/// </summary>
public sealed record ShowSettings
{
    public const double MinPeriod = 5, MaxPeriod = 600, MaxFade = 5, MinDuration = 0.5, MaxDuration = 600;
    public const int MaxAround = 10;

    /// <summary>Peças: liga/desliga a peça dentro do widget (null = padrão da peça).</summary>
    public bool? Enabled { get; init; }
    /// <summary><see cref="ShowTriggers"/>; null = padrão do widget.</summary>
    public string? Trigger { get; init; }
    /// <summary>Eventos do gatilho "events" (<see cref="ShowEvents"/>); null = os sugeridos pelo widget.</summary>
    public string[]? Events { get; init; }
    /// <summary>Gatilho periódico: intervalo entre aparições (s).</summary>
    public double? PeriodSeconds { get; init; }
    /// <summary>Tempo na tela (s) por evento/ciclo; no gatilho padrão substitui o tempo próprio do widget (p.ex. "showFor").</summary>
    public double? DurationSeconds { get; init; }
    public double? FadeInSeconds { get; init; }
    public double? FadeOutSeconds { get; init; }
    /// <summary><see cref="ShowForms"/> (listas).</summary>
    public string? Form { get; init; }
    /// <summary>Listas: linhas visíveis.</summary>
    public int? Rows { get; init; }
    /// <summary>Listas dinâmicas: pilotos ao redor do jogador.</summary>
    public int? Around { get; init; }
    /// <summary>Durações secundárias do widget (<see cref="ShowDef.Timings"/>): id -> s.</summary>
    public Dictionary<string, double>? Timings { get; init; }
    /// <summary>Exibição de cada peça (<see cref="ShowDef.Pieces"/>): id -> configuração.</summary>
    public Dictionary<string, ShowSettings>? Pieces { get; init; }

    [JsonIgnore]
    public bool IsEmpty => Enabled is null && Trigger is null && Events is null && PeriodSeconds is null && DurationSeconds is null && FadeInSeconds is null
        && FadeOutSeconds is null && Form is null && Rows is null && Around is null && Timings is null && Pieces is null;

    /// <summary>
    /// Limites e limpeza: valores iguais ao padrão e campos que o widget não usa saem; tudo padrão = null (sem bloco no JSON).
    /// Sem <paramref name="def"/> (widget desconhecido) só limites genéricos.
    /// </summary>
    public ShowSettings? Normalized(ShowDef? def, WidgetSettings? owner = null)
    {
        def ??= ShowDef.Generic;
        string? trigger = def.Triggers ? ShowTriggers.Canonical(Trigger) : null;
        if (trigger == ShowTriggers.Native) trigger = null;
        string[]? events = ShowEvents.Canonical(Events);
        if (events is not null && SameSet(events, def.DefaultEvents)) events = null;
        double? period = Clamp(PeriodSeconds, MinPeriod, MaxPeriod);
        if (period == def.DefaultPeriod) period = null;
        var dur = def.Duration ?? ShowDef.GenericDuration;
        double? duration = Clamp(DurationSeconds, dur.Min, dur.Max);
        // O padrão do widget também pode vir de uma opção antiga ("showFor"): só some quando igual ao efetivo sem a Exibição.
        if (duration is { } d && d == ShowPlan.LegacyOrDefault(dur, owner)) duration = null;
        double? fadeIn = Clamp(FadeInSeconds, 0, MaxFade), fadeOut = Clamp(FadeOutSeconds, 0, MaxFade);
        if (fadeIn == def.FadeIn) fadeIn = null;
        if (fadeOut == def.FadeOut) fadeOut = null;
        string? form = null; int? rows = null, around = null;
        if (def.List is { } list)
        {
            form = ShowForms.Canonical(Form);
            if (form == list.DefaultForm) form = null;
            if (Rows is { } r) rows = Math.Clamp(r, list.MinRows, list.MaxRows);
            if (rows is { } rr && rr == ShowPlan.LegacyOrDefault(list, owner)) rows = null;
            if (Around is { } a) around = Math.Clamp(a, 0, MaxAround);
            if (around == list.DefaultAround) around = null;
        }
        Dictionary<string, double>? timings = null;
        if (Timings is not null)
            foreach (var (k, v) in Timings)
            {
                var t = def.Timings.FirstOrDefault(x => string.Equals(x.Id, k, StringComparison.OrdinalIgnoreCase));
                if (t is null || !double.IsFinite(v)) continue;
                double c = Round(Math.Clamp(v, t.Min, t.Max));
                if (c == ShowPlan.LegacyOrDefault(t, owner)) continue;
                (timings ??= new(StringComparer.OrdinalIgnoreCase))[t.Id] = c;
            }
        Dictionary<string, ShowSettings>? pieces = null;
        if (Pieces is not null)
            foreach (var (k, v) in Pieces)
            {
                var p = def.Pieces.FirstOrDefault(x => string.Equals(x.Id, k, StringComparison.OrdinalIgnoreCase));
                if (p is null || v is null) continue;
                var n = v.NormalizedPiece(p, def, owner);
                if (n is not null) (pieces ??= new(StringComparer.OrdinalIgnoreCase))[p.Id] = n;
            }
        var result = new ShowSettings
        {
            Trigger = trigger, Events = events, PeriodSeconds = period, DurationSeconds = duration, FadeInSeconds = fadeIn, FadeOutSeconds = fadeOut,
            Form = form, Rows = rows, Around = around, Timings = timings, Pieces = pieces,
        };
        return result.IsEmpty ? null : result;
    }

    ShowSettings? NormalizedPiece(PieceDef p, ShowDef parent, WidgetSettings? owner)
    {
        bool? enabled = Enabled;
        if (enabled is { } e && e == ShowPlan.PieceDefaultEnabled(p, owner)) enabled = null;
        var dur = p.Duration ?? ShowDef.GenericDuration;
        string? trigger = ShowTriggers.Canonical(Trigger);
        // Peca: "sempre" = o padrao (a peca ja aparece enquanto tem dados); so evento/periodico restringem.
        if (trigger is ShowTriggers.Native or ShowTriggers.Always) trigger = null;
        bool generic = trigger is not null;
        double? duration = p.Duration is not null || generic ? Clamp(DurationSeconds, dur.Min, dur.Max) : null;
        if (duration is { } d && d == ShowPlan.LegacyOrDefault(dur, owner)) duration = null;
        string[]? events = trigger == ShowTriggers.Events ? ShowEvents.Canonical(Events) : null;
        if (events is not null && SameSet(events, parent.DefaultEvents)) events = null;
        double? period = trigger == ShowTriggers.Periodic ? Clamp(PeriodSeconds, MinPeriod, MaxPeriod) : null;
        if (period == parent.DefaultPeriod) period = null;
        double? fadeIn = generic ? Clamp(FadeInSeconds, 0, MaxFade) : null, fadeOut = generic ? Clamp(FadeOutSeconds, 0, MaxFade) : null;
        if (fadeIn == parent.FadeIn) fadeIn = null;
        if (fadeOut == parent.FadeOut) fadeOut = null;
        var r = new ShowSettings { Enabled = enabled, DurationSeconds = duration, Trigger = trigger, Events = events, PeriodSeconds = period, FadeInSeconds = fadeIn, FadeOutSeconds = fadeOut };
        return r.IsEmpty ? null : r;
    }

    static double? Clamp(double? v, double min, double max) => v is { } x && double.IsFinite(x) ? Round(Math.Clamp(x, min, max)) : null;
    static double Round(double v) => Math.Round(v, 2);
    static bool SameSet(IEnumerable<string> a, IEnumerable<string> b) => new HashSet<string>(a, StringComparer.OrdinalIgnoreCase).SetEquals(b);
}

/// <summary>Duração ajustável (s): padrão, limites e a opção antiga do tema que guardava o mesmo valor (lida quando a Exibição não define).</summary>
public sealed record TimingDef(string Id, string Label, double Default, double Min, double Max, string? LegacyOption = null);

/// <summary>Lista com forma estática/dinâmica: linhas (opção antiga em <see cref="RowsOption"/>) e pilotos ao redor do jogador.</summary>
public sealed record ListDef(string DefaultForm, int DefaultRows, int MinRows, int MaxRows, int DefaultAround, string? RowsOption = null, string? AroundOption = null);

/// <summary>Peça de um widget composto (Race Board / Qualy Board): liga/desliga, duração própria e gatilho próprio (padrão / por evento / periódico).</summary>
public sealed record PieceDef(string Id, string Label, TimingDef? Duration = null, string? LegacyToggle = null, bool DefaultEnabled = true);

/// <summary>Padrões da Exibição de um widget num tema (os antigos valores fixos do código).</summary>
public sealed record ShowDef
{
    public static readonly TimingDef GenericDuration = new("duration", "Tempo na tela (s)", 8, ShowSettings.MinDuration, 120);
    public static readonly ShowDef Generic = new();

    /// <summary>Descrição do gatilho padrão do widget (Control Center).</summary>
    public string NativeLabel { get; init; } = "Sempre (enquanto houver dados)";
    /// <summary>Aceita os gatilhos genéricos (sempre/evento/periódico)? A camada GPxSAT de tela inteira não.</summary>
    public bool Triggers { get; init; } = true;
    /// <summary>Duração própria do gatilho padrão (null = o widget fica enquanto houver dados; os gatilhos genéricos usam <see cref="GenericDuration"/>).</summary>
    public TimingDef? Duration { get; init; }
    public double FadeIn { get; init; } = 0.3;
    public double FadeOut { get; init; } = 0.6;
    /// <summary>O gatilho padrão usa os fades (senão a animação é a do tema, p.ex. o reveal do 2018, e os fades valem só nos gatilhos genéricos).</summary>
    public bool NativeFade { get; init; }
    public IReadOnlyList<string> DefaultEvents { get; init; } = [ShowEvents.PlayerLap];
    public double DefaultPeriod { get; init; } = 60;
    public ListDef? List { get; init; }
    public IReadOnlyList<TimingDef> Timings { get; init; } = [];
    public IReadOnlyList<PieceDef> Pieces { get; init; } = [];

    /// <summary>Ids de opções antigas do tema cujo valor agora é editado na Exibição (o Control Center as esconde da lista de opções).</summary>
    [JsonIgnore]
    public IEnumerable<string> LegacyOptionIds => new[] { Duration?.LegacyOption, List?.RowsOption, List?.AroundOption }
        .Concat(Timings.Select(t => t.LegacyOption)).Concat(Pieces.Select(p => p.Duration?.LegacyOption)).Concat(Pieces.Select(p => p.LegacyToggle))
        .Where(s => s is not null).Select(s => s!).Distinct(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Exibição efetiva de um widget (resolvida contra o catálogo do tema). Imutável; os widgets guardam uma por Configure.</summary>
public sealed class ShowPlan
{
    readonly ShowSettings _s;
    readonly WidgetSettings _cfg;
    public ShowDef Def { get; }
    public string Trigger { get; }
    public IReadOnlyList<string> Events { get; }
    public double Period { get; }
    public double Duration { get; }
    public double FadeIn { get; }
    public double FadeOut { get; }
    public string? Form { get; }
    public int Rows { get; }
    public int Around { get; }

    ShowPlan(WidgetSettings cfg, ShowDef def)
    {
        _cfg = cfg; Def = def; _s = cfg.Show ?? new ShowSettings();
        Trigger = def.Triggers ? ShowTriggers.Canonical(_s.Trigger) ?? ShowTriggers.Native : ShowTriggers.Native;
        Events = ShowEvents.Canonical(_s.Events) ?? def.DefaultEvents.ToArray();
        Period = _s.PeriodSeconds is { } p && double.IsFinite(p) ? Math.Clamp(p, ShowSettings.MinPeriod, ShowSettings.MaxPeriod) : def.DefaultPeriod;
        var dur = def.Duration ?? ShowDef.GenericDuration;
        Duration = _s.DurationSeconds is { } d && double.IsFinite(d) ? Math.Clamp(d, dur.Min, dur.Max) : LegacyOrDefault(dur, cfg);
        FadeIn = _s.FadeInSeconds is { } fi && double.IsFinite(fi) ? Math.Clamp(fi, 0, ShowSettings.MaxFade) : def.FadeIn;
        FadeOut = _s.FadeOutSeconds is { } fo && double.IsFinite(fo) ? Math.Clamp(fo, 0, ShowSettings.MaxFade) : def.FadeOut;
        if (def.List is { } list)
        {
            Form = ShowForms.Canonical(_s.Form) ?? list.DefaultForm;
            Rows = _s.Rows is { } r ? Math.Clamp(r, list.MinRows, list.MaxRows) : LegacyOrDefault(list, cfg);
            Around = _s.Around is { } a ? Math.Clamp(a, 0, ShowSettings.MaxAround)
                : list.AroundOption is { } ao && Num(cfg, ao) is { } av ? (int)Math.Clamp(Math.Round(av), 0, ShowSettings.MaxAround) : list.DefaultAround;
        }
    }

    public static ShowPlan For(WidgetSettings cfg, string? themeId) => new(cfg, ShowCatalog.For(themeId, cfg.Id));

    /// <summary>Gatilho padrão do widget (o comportamento antigo, com as durações da Exibição).</summary>
    public bool IsNative => Trigger == ShowTriggers.Native;
    /// <summary>Sempre na tela.</summary>
    public bool IsAlways => Trigger == ShowTriggers.Always;
    /// <summary>Gatilho genérico ativo: o widget desenha o conteúdo como "sempre visível" e a janela é controlada por <see cref="GateAlpha"/>.</summary>
    public bool ContentAlways => !IsNative;
    /// <summary>Forma dinâmica (janela ao redor do jogador)?</summary>
    public bool Dynamic => Form == ShowForms.Dynamic;

    /// <summary>Duração secundária <paramref name="id"/> (<see cref="ShowDef.Timings"/>); id desconhecido = <paramref name="fallback"/>.</summary>
    public double Timing(string id, double fallback)
    {
        var t = Def.Timings.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (t is null) return fallback;
        return _s.Timings is not null && _s.Timings.TryGetValue(t.Id, out var v) && double.IsFinite(v) ? Math.Clamp(v, t.Min, t.Max) : LegacyOrDefault(t, _cfg);
    }

    /// <summary>Peça ligada? Num plano com instante (<see cref="At"/>) também exige a janela do gatilho próprio da peça.</summary>
    public bool PieceEnabled(string id) => _lastEvent is null ? PieceEnabledBase(id) : PieceAlpha(id) > 0.01f;

    bool PieceEnabledBase(string id)
    {
        var p = PieceDefOf(id);
        if (p is null) return true;
        // Janela de peca (RenderSettings): so a peca dela; no pai, as pecas com janela propria visivel saem.
        if (Solo is { } solo) { if (!string.Equals(p.Id, solo, StringComparison.OrdinalIgnoreCase)) return false; }
        else if (IsDetached(p.Id)) return false;
        return Piece(p.Id)?.Enabled ?? PieceDefaultEnabled(p, _cfg);
    }

    /// <summary>Peca desenhada sozinha (janela "pai.peca"; null = widget inteiro).</summary>
    public string? Solo => _cfg.Option(RenderSettings.PieceSoloOption) is { Length: > 0 } s ? s : null;
    bool IsDetached(string id) => _cfg.Option(RenderSettings.PiecesDetachedOption) is { Length: > 0 } list
        && list.Split(',').Contains(id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Duração da peça; peça sem duração = <paramref name="fallback"/>.</summary>
    public double PieceDuration(string id, double fallback)
    {
        var p = Def.Pieces.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (p is null) return fallback;
        if (p.Duration is not { } dur)   // peça sem duração própria: só a da janela do gatilho por evento/periódico
            return Piece(p.Id)?.DurationSeconds is { } g && double.IsFinite(g) ? Math.Clamp(g, ShowDef.GenericDuration.Min, ShowDef.GenericDuration.Max) : fallback;
        return Piece(p.Id)?.DurationSeconds is { } d && double.IsFinite(d) ? Math.Clamp(d, dur.Min, dur.Max) : LegacyOrDefault(dur, _cfg);
    }

    ShowSettings? Piece(string id) => _s.Pieces is not null && _s.Pieces.TryGetValue(id, out var v) ? v : null;
    PieceDef? PieceDefOf(string id) => Def.Pieces.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    double _now = double.NaN;
    Func<string, double?>? _lastEvent;

    /// <summary>
    /// Plano no instante <paramref name="now"/>: <see cref="PieceEnabled"/> passa a considerar o gatilho de cada peça
    /// (fora da janela do evento/ciclo = desligada, e o board segue para a peça seguinte) e <see cref="PieceAlpha"/> dá o fade.
    /// Sem nenhuma peça com gatilho próprio devolve o próprio plano.
    /// </summary>
    public ShowPlan At(double now, Func<string, double?> lastEvent)
    {
        if (_s.Pieces is null || !_s.Pieces.Values.Any(p => PieceTriggerOf(p) is not null)) return this;
        var r = (ShowPlan)MemberwiseClone();
        r._now = now; r._lastEvent = lastEvent;
        return r;
    }

    static string? PieceTriggerOf(ShowSettings? p) => ShowTriggers.Canonical(p?.Trigger) is ShowTriggers.Events or ShowTriggers.Periodic ? ShowTriggers.Canonical(p!.Trigger) : null;

    /// <summary>Gatilho da peça: <see cref="ShowTriggers.Native"/>, <see cref="ShowTriggers.Events"/> ou <see cref="ShowTriggers.Periodic"/>.</summary>
    public string PieceTrigger(string id) => PieceDefOf(id) is { } p ? PieceTriggerOf(Piece(p.Id)) ?? ShowTriggers.Native : ShowTriggers.Native;
    /// <summary>Eventos do gatilho da peça (padrão = os do widget).</summary>
    public IReadOnlyList<string> PieceEvents(string id) => ShowEvents.Canonical(PieceDefOf(id) is { } p ? Piece(p.Id)?.Events : null) ?? Def.DefaultEvents.ToArray();
    public double PiecePeriod(string id) => PieceDefOf(id) is { } p && Piece(p.Id)?.PeriodSeconds is { } v && double.IsFinite(v) ? Math.Clamp(v, ShowSettings.MinPeriod, ShowSettings.MaxPeriod) : Def.DefaultPeriod;
    public double PieceFadeIn(string id) => PieceDefOf(id) is { } p && Piece(p.Id)?.FadeInSeconds is { } v && double.IsFinite(v) ? Math.Clamp(v, 0, ShowSettings.MaxFade) : Def.FadeIn;
    public double PieceFadeOut(string id) => PieceDefOf(id) is { } p && Piece(p.Id)?.FadeOutSeconds is { } v && double.IsFinite(v) ? Math.Clamp(v, 0, ShowSettings.MaxFade) : Def.FadeOut;

    /// <summary>
    /// Alfa do gatilho da peça em <paramref name="now"/>: padrão = 1; "events" = envelope do evento mais recente; "periodic" = começo de cada ciclo.
    /// A duração da janela é a da peça (<see cref="PieceDuration"/>, genérica se a peça não tiver).
    /// </summary>
    public float PieceGate(string id, double now, Func<string, double?> lastEvent)
    {
        string trigger = PieceTrigger(id);
        if (trigger == ShowTriggers.Native) return 1f;
        double hold = PieceDuration(id, ShowDef.GenericDuration.Default), fi = PieceFadeIn(id), fo = PieceFadeOut(id);
        if (trigger == ShowTriggers.Periodic)
        {
            if (!double.IsFinite(now)) return 0;
            double period = PiecePeriod(id), phase = now % period;
            if (phase < 0) phase += period;
            return Envelope(phase, Math.Min(hold, period), fi, fo);
        }
        float best = 0;
        foreach (var e in PieceEvents(id))
            if (lastEvent(e) is { } at) best = Math.Max(best, Envelope(now - at, hold, fi, fo));
        return best;
    }

    /// <summary>Alfa da peça no instante de <see cref="At"/> (sem instante = 1; desligada = 0).</summary>
    public float PieceAlpha(string id)
    {
        if (!PieceEnabledBase(id)) return 0f;
        return _lastEvent is null ? 1f : PieceGate(id, _now, _lastEvent);
    }

    /// <summary>Envelope: fade in, <see cref="Duration"/> s na tela (contando os fades) e fade out. age fora de [0, hold) = 0.</summary>
    public float Fade(double age) => Envelope(age, Duration, FadeIn, FadeOut);
    public float Fade(double age, double hold) => Envelope(age, hold, FadeIn, FadeOut);

    public static float Envelope(double age, double hold, double fadeIn, double fadeOut)
    {
        if (double.IsNaN(age) || age < 0 || age >= hold) return 0f;
        if (fadeIn > 0 && age < fadeIn) return (float)(age / fadeIn);
        if (fadeOut > 0 && age > hold - fadeOut) return (float)Math.Clamp((hold - age) / fadeOut, 0, 1);
        return 1f;
    }

    /// <summary>
    /// Alfa da janela pelo gatilho: padrão/sempre = 1; "events" = envelope do evento mais recente entre os escolhidos;
    /// "periodic" = envelope no começo de cada ciclo de <see cref="Period"/> s. <paramref name="lastEvent"/> devolve o instante do último evento (null = nunca).
    /// </summary>
    public float GateAlpha(double now, Func<string, double?> lastEvent)
    {
        switch (Trigger)
        {
            case ShowTriggers.Events:
                float best = 0;
                foreach (var e in Events)
                    if (lastEvent(e) is { } at) best = Math.Max(best, Fade(now - at));
                return best;
            case ShowTriggers.Periodic:
                if (!double.IsFinite(now)) return 0;
                double phase = now % Period;
                if (phase < 0) phase += Period;
                return Fade(phase, Math.Min(Duration, Period));
            default:
                return 1f;
        }
    }

    internal static double LegacyOrDefault(TimingDef t, WidgetSettings? cfg)
        => t.LegacyOption is { } o && cfg is not null && Num(cfg, o) is { } v ? Math.Round(Math.Clamp(v, t.Min, t.Max), 2) : t.Default;

    internal static int LegacyOrDefault(ListDef l, WidgetSettings? cfg)
        => l.RowsOption is { } o && cfg is not null && Num(cfg, o) is { } v ? (int)Math.Clamp(Math.Round(v), l.MinRows, l.MaxRows) : l.DefaultRows;

    internal static bool PieceDefaultEnabled(PieceDef p, WidgetSettings? cfg)
        => p.LegacyToggle is { } o && cfg?.Option(o) is { } v && bool.TryParse(v, out var b) ? b : p.DefaultEnabled;

    static double? Num(WidgetSettings cfg, string option)
        => cfg.Option(option) is { } s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;

    /// <summary>
    /// Settings que o widget recebe com um gatilho genérico ativo: força o "sempre visível" próprio do widget (coluna ou opção "always"),
    /// para que o conteúdo exista sempre e quem decide aparecer/sumir seja o gatilho (alfa da janela, <see cref="GateAlpha"/>).
    /// </summary>
    public static WidgetSettings ContentSettings(WidgetSettings cfg, string? themeId)
    {
        var plan = For(cfg, themeId);
        if (!plan.ContentAlways) return cfg;
        var r = cfg;
        if (WidgetCatalog.Find(cfg.Id, themeId)?.Columns.Any(c => c.Id == "always") == true && cfg.Columns is { } cols && !cols.Contains("always", StringComparer.OrdinalIgnoreCase))
            r = r with { Columns = [.. cols, "always"] };
        if (themeId is not null && WidgetCatalog.OptionsFor(themeId, cfg.Id).Any(o => o.Id == "always"))
            r = r with { Options = new Dictionary<string, string>(cfg.Options ?? [], StringComparer.OrdinalIgnoreCase) { ["always"] = "true" } };
        return r;
    }
}

/// <summary>
/// Padrões da Exibição por tema e widget: são os valores que antes ficavam fixos no código (BroadcastUi.CaptionHold, QualiLapWidget.SplitHold,
/// Broadcast93Rules, QualiBoard99Presentation...). Perfis sem bloco "show" desenham exatamente com estes valores.
/// </summary>
public static class ShowCatalog
{
    static TimingDef T(string id, string label, double def, double min, double max, string? legacy = null) => new(id, label, def, min, max, legacy);

    /// <summary>
    /// Peças do GPxSAT por tema (telas do GPxPatch agrupadas; o OverlayHost mapeia cada tela pela chave em GpxPieces). Todas ligadas por padrão:
    /// sem bloco "show" o despacho é o do GPxPatch.
    /// </summary>
    static PieceDef[] GpxSatPieces(string t) => t switch
    {
        "f1-1998" =>
        [
            new("time", "Time left / voltas restantes"),
            new("sky", "Sky messages (volta mais rápida, abandono, boxes, punição, riding with, vencedor)"),
            new("pittimer", "Cronômetro do jogador nos boxes"),
            new("split", "Splits, gaps, speed trap e cartão da classificação"),
            new("standings", "Standings (corrida, paradas, classificação)"),
        ],
        _ => [],
    };

    /// <summary>Exibição padrão do widget no tema (widget sem durações próprias = genérico com os fades do tema).</summary>
    public static ShowDef For(string? themeId, string widgetId)
    {
        string t = ThemeCatalog.Canonical(themeId ?? ThemeCatalog.Default);
        bool is18 = t == "f1-2018", is04 = t is "f1-2004" or "f1-2012", is93 = t == "f1-1993", is99 = WidgetCatalog.Is99(t);
        var d = new ShowDef { FadeIn = is04 ? .16 : .3, FadeOut = is04 ? .16 : .6, NativeFade = !is18 };
        // Janela de peca: a Exibicao (gatilho, duracao, liga/desliga) e a da peca no widget pai.
        if (WidgetCatalog.IsPiece(widgetId)) return d with { Triggers = false };
        switch (widgetId.ToLowerInvariant())
        {
            case "drivercaption":
                return d with
                {
                    NativeLabel = "Por evento (mudança de posição, volta e boxes do jogador)",
                    Duration = T("duration", "Tempo na tela por evento (s)", 6, 3, 15, is18 ? "showFor" : null),
                    DefaultEvents = [ShowEvents.Position, ShowEvents.PlayerLap],
                    FadeIn = .3, FadeOut = .6,   // a legenda usa o fade longo em todos os temas (no 2004/2012 a corrida usa o pulso do tema)
                };
            case "winner":
                return d with
                {
                    NativeLabel = "Ao fim da corrida (bandeirada do vencedor)",
                    Duration = T("duration", "Tempo na tela (s)", is18 ? 12 : 10, 5, 30, is18 ? "showFor" : null),
                    DefaultEvents = [ShowEvents.Finish],
                };
            case "pitstops":
                return d with { NativeLabel = "Quando um carro entra nos boxes", Duration = T("duration", "Tempo na tela após a entrada (s)", 8, 2, 60), DefaultEvents = [ShowEvents.Pit] };
            case "pittimer":
                return d with { NativeLabel = "Com o jogador nos boxes", Duration = T("duration", "Tempo na tela após a parada (s)", 4, 1, 30), DefaultEvents = [ShowEvents.PlayerPit] };
            case "racestart":
                return d with { NativeLabel = "Na largada, ao alcançar o alvo", Duration = T("duration", "Tempo na tela após alcançar o alvo (s)", 10, 5, 30, "showFor"), DefaultEvents = [ShowEvents.Start] };
            case "racecontrol":
                return d with { NativeLabel = "Com bandeira ou parada lenta", Duration = T("duration", "Tempo na tela da barra de parada lenta (s)", 8, 3, 15, "showFor"), DefaultEvents = [ShowEvents.Flag] };
            case "poscaption":
                return d with { NativeLabel = "Quando o jogador muda de posição", Duration = T("duration", "Tempo na tela por evento (s)", 6, 3, 15, "showFor"), DefaultEvents = [ShowEvents.Position] };
            case "qualilap":
                return d with
                {
                    NativeLabel = "Em volta lançada e no resultado",
                    Duration = T("duration", "Tempo na tela do resultado após cruzar a linha (s)", 6, 3, 15, "showFor"),
                    DefaultEvents = [ShowEvents.Sector, ShowEvents.PlayerLap],
                    Timings = is18
                        ? [T("split", "Diferença da parcial na tela (s)", 3, 1, 10), T("sectorPanel", "Painel de setor (SECTOR n) na tela (s)", 4, 1, 10)]
                        : [T("split", "Diferença da parcial na tela (s)", 3, 1, 10)],
                };
            case "qualiresult":
                return d with
                {
                    NativeLabel = "Ao fim da classificação", FadeIn = .3, FadeOut = .6, NativeFade = true,
                    Duration = T("duration", "Tempo na tela após o fim da sessão (s)", 15, 5, 60, "showFor"),
                    DefaultEvents = [ShowEvents.SessionEnd],
                };
            case "lapdelta":
                return d with { Timings = [T("sectors", "Parciais da última volta na tela (s)", 8, 2, 30)] };
            case "standings" when is18:
                return d with { Timings = [T("mode", "Modo automático: segundos por modo", 10, 5, 30, "modeSeconds")] };
            case "standings" when is93:
            case "qualitower" when is93:
                return d with { List = new ListDef(ShowForms.Static, 6, 3, 20, 3, RowsOption: "rows") };
            case "qualitower" when is18:
                return d with { List = new ListDef(ShowForms.Dynamic, 10, 5, 20, 3, RowsOption: "rows", AroundOption: "nearCount") };
            case "qualitower" when is99:
                return d with
                {
                    Timings = [T("title", "Título CLASSIFICATION na tela (s)", .6, .2, 3), T("page", "Segundos por página da classificação", 7, 3, 20)],
                };
            case "qualitower" when is04:
                return d with { List = new ListDef(ShowForms.Dynamic, 5, 1, 20, 3, RowsOption: "rows", AroundOption: "nearCount") };
            case "ticker":
                return d with { Timings = [T("page", "Segundos por página (5 pilotos)", 6, 3, 15, "pageSeconds")] };
            case "gpxsat":
                return d with
                {
                    Triggers = false, NativeLabel = "Telas do GPxPatch (cada uma com o próprio evento)",
                    Timings = [T("sky", "Sky messages na tela (s)", 8, 2, 30, "skySeconds"), T("split", "Splits/gaps/trap na tela (s)", 8, 2, 30, "splitSeconds"),
                               T("page", "Standings: segundos por página", 6, 2, 30, "pageSeconds")],
                    Pieces = GpxSatPieces(t),
                };
            case "board" when is93:
                return d with
                {
                    NativeLabel = "Peças por prioridade: boxes > volta mais rápida > gap > voltas restantes > legenda",
                    Pieces =
                    [
                        new PieceDef("pit", "Tempo de parada nos boxes", T("duration", "Resultado na tela (s)", 3, 1, 15)),
                        new PieceDef("fastest", "Volta mais rápida da corrida", T("duration", "Tempo na tela (s)", 7, 3, 15), LegacyToggle: "showFastest"),
                        new PieceDef("gap", "Gap para o carro da frente", T("duration", "Resultado na tela (s)", 6, 3, 15, "gapHoldSeconds")),
                        new PieceDef("lapsToGo", "Voltas restantes (LAPS TO GO)", T("duration", "Tempo na tela (s)", 6, 3, 15)),
                        new PieceDef("caption", "Legenda do piloto (nome e equipe / a bordo)"),
                    ],
                };
            case "board" when is99:
                return d with
                {
                    NativeLabel = "Peças por prioridade: boxes > volta mais rápida > voltas restantes > gap > torre > legenda",
                    Pieces =
                    [
                        new PieceDef("pit", "Parada nos boxes (tempo parado)"),
                        new PieceDef("fastest", "Volta mais rápida da corrida", T("duration", "Tempo na tela (s)", 7, 3, 15)),
                        new PieceDef("remaining", "Voltas restantes (a cada 5 voltas)", T("duration", "Tempo na tela (s)", 6, 3, 15)),
                        new PieceDef("gap", "Gap na linha de chegada (a cada 3 voltas)", T("duration", "Tempo na tela (s)", 6, 3, 15)),
                        new PieceDef("table", "Torre da volta (8 por página)", T("duration", "Segundos por página", 4, 2, 15)),
                        new PieceDef("caption", "Legenda do piloto (nome e equipe)"),
                    ],
                };
            case "board" when is18 || is04:
                return d with
                {
                    NativeLabel = "Peças por prioridade: torre da linha > gap no setor > comparativo de voltas > legenda",
                    Pieces =
                    [
                        new PieceDef("tower", "Torre da linha (8 por página)", T("duration", "Segundos por página", 4, 2, 15)),
                        new PieceDef("sector", "Gap no setor (S1/S2/S3)"),
                        new PieceDef("laps", "Comparativo de voltas (a cada 3 voltas)"),
                        new PieceDef("plate", "Legenda do piloto"),
                    ],
                };
            case "qualiboard" when is93:
                return d with
                {
                    NativeLabel = "Peças por prioridade: resultado > volta mais rápida > volta ao vivo > legenda",
                    Pieces =
                    [
                        new PieceDef("result", "Resultado da volta", T("duration", "Tempo na tela (s)", 6, 3, 15, "showFor")),
                        new PieceDef("fastest", "Volta mais rápida da sessão", T("duration", "Tempo na tela (s)", 7, 3, 15), LegacyToggle: "showFastest"),
                        new PieceDef("split", "Diferença na parcial", T("duration", "Tempo na tela (s)", 3, 1, 10)),
                        new PieceDef("live", "Volta ao vivo (tempo corrente)"),
                        new PieceDef("caption", "Legenda (FIRST LAP OUT OF PITS)"),
                    ],
                };
            case "qualiboard" when is99:
                return d with
                {
                    NativeLabel = "Peças por prioridade: resultado > intermediária > volta ao vivo",
                    Pieces =
                    [
                        new PieceDef("result", "Resultado da volta", T("duration", "Retenção do resultado (s)", 6, 3, 15, "showFor")),
                        new PieceDef("split", "Diferença na intermediária", T("duration", "Tempo na tela (s)", 3, 1, 10)),
                    ],
                };
            default:
                return d;
        }
    }
}
