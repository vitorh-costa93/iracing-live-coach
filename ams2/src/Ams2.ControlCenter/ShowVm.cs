using System.Collections.ObjectModel;
using System.Globalization;
using Ams2.Shared.Profiles;

namespace Ams2.ControlCenter;

/// <summary>Opcao de ComboBox com valor texto (gatilho, forma).</summary>
public sealed record TextChoice(string Label, string Value);

/// <summary>Numero da Exibicao (s, linhas): caixa de texto com limites; aceita virgula ou ponto, invalido mantem o anterior.</summary>
public sealed class ShowNumVm : Notify
{
    double _value;
    public ShowNumVm(string id, string label, double min, double max, bool integer = false)
    { Id = id; Label = label; Min = min; Max = max; Integer = integer; _value = min; }
    public string Id { get; }
    public string Label { get; }
    public double Min { get; }
    public double Max { get; }
    public bool Integer { get; }
    public string Range => Min.ToString("0.##", CultureInfo.InvariantCulture) + "–" + Max.ToString("0.##", CultureInfo.InvariantCulture);
    double Fix(double v) { v = Math.Clamp(v, Min, Max); return Integer ? Math.Round(v) : Math.Round(v, 2); }
    public double Value { get => _value; set { if (!double.IsFinite(value)) return; if (Set(ref _value, Fix(value))) { Raise(nameof(Text)); Edited?.Invoke(); } } }
    public string Text
    {
        get => _value.ToString("0.##", CultureInfo.InvariantCulture);
        set { if (double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) Value = v; Raise(); }
    }
    public void Load(double v) { _value = Fix(v); Raise(nameof(Value)); Raise(nameof(Text)); }
    public event Action? Edited;
}

/// <summary>Caixa marcavel (evento do gatilho).</summary>
public sealed class ShowCheckVm : Notify
{
    bool _checked;
    public ShowCheckVm(string id, string label) { Id = id; Label = label; }
    public string Id { get; }
    public string Label { get; }
    public bool Checked { get => _checked; set { if (Set(ref _checked, value)) Edited?.Invoke(); } }
    public void Load(bool v) { _checked = v; Raise(nameof(Checked)); }
    public event Action? Edited;
}

/// <summary>Peca de um widget composto (Race Board / Qualy Board): liga/desliga, tempo de tela e gatilho proprio (padrao / por evento / periodico).</summary>
public sealed class ShowPieceVm : Notify
{
    bool _enabled = true;
    string _trigger = ShowTriggers.Native;
    public ShowPieceVm(PieceDef def)
    {
        Def = def;
        var d = def.Duration ?? ShowDef.GenericDuration;
        Duration = new ShowNumVm(d.Id, def.Duration is null ? "Tempo na tela por aparição (s)" : d.Label, d.Min, d.Max);
        Period = new ShowNumVm("period", "Intervalo entre aparições (s)", ShowSettings.MinPeriod, ShowSettings.MaxPeriod);
        EveryLaps = new ShowNumVm("everyLaps", "A cada (voltas)", 1, ShowSettings.MaxEveryLaps, integer: true);
        FadeIn = new ShowNumVm("fadeIn", "Fade de entrada (s)", 0, ShowSettings.MaxFade);
        FadeOut = new ShowNumVm("fadeOut", "Fade de saída (s)", 0, ShowSettings.MaxFade);
        Events = new(ShowEvents.All.Select(e => new ShowCheckVm(e, ShowEvents.Label(e))));
        foreach (var n in new[] { Duration, Period, EveryLaps, FadeIn, FadeOut }) n.Edited += () => Edited?.Invoke();
        foreach (var e in Events) e.Edited += () => Edited?.Invoke();
    }
    /// <summary>Gatilhos de uma peca: "sempre" e o proprio padrao (a peca aparece enquanto tem dados).</summary>
    static readonly IReadOnlyList<TextChoice> TriggerChoices =
        [new("Padrão da peça", ShowTriggers.Native), new(ShowTriggers.Label(ShowTriggers.Events), ShowTriggers.Events), new(ShowTriggers.Label(ShowTriggers.Periodic), ShowTriggers.Periodic)];
    static readonly TextChoice LapsChoice = new(ShowTriggers.Label(ShowTriggers.Laps), ShowTriggers.Laps);
    static readonly IReadOnlyList<TextChoice> LapsOfChoices = ShowLapsOf.All.Select(l => new TextChoice(ShowLapsOf.Label(l), l)).ToArray();
    /// <summary>"A cada N voltas" so nas pecas que oferecem a regra (<see cref="PieceDef.EveryLaps"/>).</summary>
    public IReadOnlyList<TextChoice> Triggers => Def.EveryLaps is null ? TriggerChoices : [.. TriggerChoices, LapsChoice];
    public IReadOnlyList<TextChoice> LapsOfs => LapsOfChoices;
    public ShowNumVm EveryLaps { get; }
    string _lapsOf = ShowLapsOf.Player;
    public string LapsOf { get => _lapsOf; set { if (ShowLapsOf.Canonical(value) is { } v && Set(ref _lapsOf, v)) Edited?.Invoke(); } }
    public PieceDef Def { get; }
    public string Label => Def.Label;
    public ShowNumVm Duration { get; }
    public ShowNumVm Period { get; }
    public ShowNumVm FadeIn { get; }
    public ShowNumVm FadeOut { get; }
    public ObservableCollection<ShowCheckVm> Events { get; }
    public bool HasDuration => Def.Duration is not null || !IsNative;
    public bool Enabled { get => _enabled; set { if (Set(ref _enabled, value)) Edited?.Invoke(); } }
    public string Trigger
    {
        get => _trigger;
        set { if (ShowTriggers.CanonicalPiece(value) is { } v && Set(ref _trigger, v == ShowTriggers.Always ? ShowTriggers.Native : v)) { RaiseTrigger(); Edited?.Invoke(); } }
    }
    public bool IsNative => _trigger == ShowTriggers.Native;
    public bool IsEvents => _trigger == ShowTriggers.Events;
    public bool IsPeriodic => _trigger == ShowTriggers.Periodic;
    public bool IsLaps => _trigger == ShowTriggers.Laps;
    public bool IsTimed => !IsNative;
    void RaiseTrigger() { Raise(nameof(Trigger)); Raise(nameof(IsNative)); Raise(nameof(IsEvents)); Raise(nameof(IsPeriodic)); Raise(nameof(IsLaps)); Raise(nameof(IsTimed)); Raise(nameof(HasDuration)); }

    public void Load(ShowPlan plan)
    {
        string id = Def.Id;
        _enabled = plan.PieceEnabled(id); Raise(nameof(Enabled));
        _trigger = plan.PieceTrigger(id); RaiseTrigger();
        Duration.Load(plan.PieceDuration(id, (Def.Duration ?? ShowDef.GenericDuration).Default));
        Period.Load(plan.PiecePeriod(id)); FadeIn.Load(plan.PieceFadeIn(id)); FadeOut.Load(plan.PieceFadeOut(id));
        EveryLaps.Load(plan.PieceEveryLaps(id)); _lapsOf = plan.PieceLapsOf(id); Raise(nameof(LapsOf));
        var events = plan.PieceEvents(id);
        foreach (var e in Events) e.Load(events.Contains(e.Id, StringComparer.OrdinalIgnoreCase));
    }

    public ShowSettings Build()
    {
        var events = Events.Where(e => e.Checked).Select(e => e.Id).ToArray();
        return new ShowSettings
        {
            Enabled = _enabled, Trigger = _trigger, DurationSeconds = Duration.Value, PeriodSeconds = Period.Value, EveryLaps = (int)EveryLaps.Value, LapsOf = _lapsOf,
            FadeInSeconds = FadeIn.Value, FadeOutSeconds = FadeOut.Value, Events = events.Length == 0 ? null : events,
        };
    }
    public event Action? Edited;
}

/// <summary>
/// Secao "Exibição" de um widget: gatilho (padrao / sempre / por evento / periodico), duracao, fades, forma da lista, tempos secundarios e pecas.
/// Mostra os valores efetivos (<see cref="ShowPlan"/>); <see cref="Build"/> devolve so o que difere do padrao do tema (null = tudo padrao).
/// </summary>
public sealed class ShowVm : Notify
{
    string _trigger = ShowTriggers.Native, _form = ShowForms.Static;
    bool _loading;

    public ShowVm(ShowDef def)
    {
        Def = def;
        Triggers = ShowTriggers.All.Select(t => new TextChoice(ShowTriggers.Label(t), t)).ToArray();
        Forms = ShowForms.All.Select(f => new TextChoice(ShowForms.Label(f), f)).ToArray();
        Events = new(ShowEvents.All.Select(e => new ShowCheckVm(e, ShowEvents.Label(e))));
        var dur = def.Duration ?? ShowDef.GenericDuration;
        Period = new ShowNumVm("period", "Intervalo entre aparições (s)", ShowSettings.MinPeriod, ShowSettings.MaxPeriod);
        Duration = new ShowNumVm(dur.Id, dur.Label, dur.Min, dur.Max);
        FadeIn = new ShowNumVm("fadeIn", "Fade de entrada (s)", 0, ShowSettings.MaxFade);
        FadeOut = new ShowNumVm("fadeOut", "Fade de saída (s)", 0, ShowSettings.MaxFade);
        if (def.List is { } l)
        {
            Rows = new ShowNumVm("rows", "Linhas visíveis", l.MinRows, l.MaxRows, integer: true);
            Around = new ShowNumVm("around", "Pilotos ao redor do jogador (inclui o jogador)", 0, ShowSettings.MaxAround, integer: true);
        }
        Timings = new(def.Timings.Select(t => new ShowNumVm(t.Id, t.Label, t.Min, t.Max)));
        Pieces = new(def.Pieces.Select(p => new ShowPieceVm(p)));
        foreach (var e in Events) e.Edited += Changed;
        foreach (var n in new[] { Period, Duration, FadeIn, FadeOut, Rows, Around }) if (n is not null) n.Edited += Changed;
        foreach (var t in Timings) t.Edited += Changed;
        foreach (var p in Pieces) p.Edited += Changed;
    }

    public ShowDef Def { get; }
    public IReadOnlyList<TextChoice> Triggers { get; }
    public IReadOnlyList<TextChoice> Forms { get; }
    public ObservableCollection<ShowCheckVm> Events { get; }
    public ShowNumVm Period { get; }
    public ShowNumVm Duration { get; }
    public ShowNumVm FadeIn { get; }
    public ShowNumVm FadeOut { get; }
    public ShowNumVm? Rows { get; }
    public ShowNumVm? Around { get; }
    public ObservableCollection<ShowNumVm> Timings { get; }
    public ObservableCollection<ShowPieceVm> Pieces { get; }

    public bool HasTriggers => Def.Triggers;
    public bool HasList => Def.List is not null;
    public bool HasTimings => Timings.Count > 0;
    public bool HasPieces => Pieces.Count > 0;
    public bool HasAny => HasTriggers || HasList || HasTimings || HasPieces;
    public string NativeLabel => "Padrão: " + Def.NativeLabel;

    public string Trigger { get => _trigger; set { if (ShowTriggers.Canonical(value) is { } v && Set(ref _trigger, v)) { RaiseTrigger(); Changed(); } } }
    public bool IsNative => _trigger == ShowTriggers.Native;
    public bool IsEvents => _trigger == ShowTriggers.Events;
    public bool IsPeriodic => _trigger == ShowTriggers.Periodic;
    /// <summary>Tempo na tela: o proprio do widget no gatilho padrao, ou o de cada aparicao (evento/periodico).</summary>
    public bool ShowDuration => IsEvents || IsPeriodic || IsNative && Def.Duration is not null;
    public bool ShowFades => IsEvents || IsPeriodic || IsNative && Def.NativeFade;

    public string Form { get => _form; set { if (ShowForms.Canonical(value) is { } v && Set(ref _form, v)) { Raise(nameof(IsDynamic)); Changed(); } } }
    public bool IsDynamic => _form == ShowForms.Dynamic;

    public event Action? Edited;

    void Changed() { if (!_loading) Edited?.Invoke(); }

    void RaiseTrigger()
    {
        Raise(nameof(Trigger)); Raise(nameof(IsNative)); Raise(nameof(IsEvents)); Raise(nameof(IsPeriodic));
        Raise(nameof(ShowDuration)); Raise(nameof(ShowFades));
    }

    /// <summary>Valores efetivos de <paramref name="s"/> no tema (bloco "show" + opcoes antigas + padroes), sem disparar <see cref="Edited"/>.</summary>
    public void Load(WidgetSettings s, string themeId)
    {
        _loading = true;
        try
        {
            var plan = ShowPlan.For(s, themeId);
            _trigger = plan.Trigger; RaiseTrigger();
            foreach (var e in Events) e.Load(plan.Events.Contains(e.Id, StringComparer.OrdinalIgnoreCase));
            Period.Load(plan.Period); Duration.Load(plan.Duration); FadeIn.Load(plan.FadeIn); FadeOut.Load(plan.FadeOut);
            if (Def.List is not null)
            {
                _form = plan.Form ?? ShowForms.Static; Raise(nameof(Form)); Raise(nameof(IsDynamic));
                Rows?.Load(plan.Rows); Around?.Load(plan.Around);
            }
            foreach (var t in Timings) t.Load(plan.Timing(t.Id, t.Min));
            foreach (var p in Pieces) p.Load(plan);
        }
        finally { _loading = false; }
    }

    /// <summary>Bloco "show" para o perfil: so os valores fora do padrao (<paramref name="owner"/> traz as opcoes antigas, que tambem contam como padrao).</summary>
    public ShowSettings? Build(WidgetSettings owner)
    {
        var events = Events.Where(e => e.Checked).Select(e => e.Id).ToArray();
        return new ShowSettings
        {
            Trigger = _trigger,
            Events = events.Length == 0 ? null : events,
            PeriodSeconds = Period.Value, DurationSeconds = Duration.Value, FadeInSeconds = FadeIn.Value, FadeOutSeconds = FadeOut.Value,
            Form = Def.List is null ? null : _form, Rows = (int?)Rows?.Value, Around = (int?)Around?.Value,
            Timings = Timings.Count == 0 ? null : Timings.ToDictionary(t => t.Id, t => t.Value, StringComparer.OrdinalIgnoreCase),
            Pieces = Pieces.Count == 0 ? null : Pieces.ToDictionary(p => p.Def.Id, p => p.Build(), StringComparer.OrdinalIgnoreCase),
        }.Normalized(Def, owner);
    }
}
