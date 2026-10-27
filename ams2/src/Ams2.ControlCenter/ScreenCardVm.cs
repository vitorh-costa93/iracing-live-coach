using System.Collections.ObjectModel;
using System.Windows.Media;
using Ams2.Shared.Profiles;

namespace Ams2.ControlCenter;

/// <summary>Textos em português simples dos widgets para a tela "Telas e exibição" (id -> título, descrição de uma linha, ícone MDL2).</summary>
public static class ScreenTexts
{
    static readonly Dictionary<string, (string Title, string Desc, string Icon)> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["standings"] = ("Classificação", "Tabela de posições da corrida, com as diferenças para o líder.", ""),
        ["relative"] = ("Relativo", "Os pilotos logo à frente e atrás de você, com a diferença de tempo.", ""),
        ["fuel"] = ("Combustível", "Voltas restantes, consumo por volta e quanto abastecer.", ""),
        ["tyres"] = ("Pneus", "Temperatura, desgaste e composto dos quatro pneus.", ""),
        ["weather"] = ("Clima", "Temperatura do ar e da pista e chuva.", ""),
        ["inputs"] = ("Pedais e velocímetro", "Acelerador, freio, marcha e velocidade.", ""),
        ["inputgraph"] = ("Gráfico de pedais", "Histórico recente do acelerador e do freio.", ""),
        ["radar"] = ("Radar", "Avisa quando há um carro ao seu lado.", ""),
        ["lapcounter"] = ("Contador de voltas", "Volta atual e total de voltas da corrida.", ""),
        ["drivercaption"] = ("Legenda do piloto", "Nome, equipe e posição do piloto em destaque.", ""),
        ["pitstops"] = ("Paradas nos boxes", "Quem entrou nos boxes e quantas paradas já fez.", ""),
        ["pittimer"] = ("Cronômetro dos boxes", "Tempo que o seu carro fica parado nos boxes.", ""),
        ["board"] = ("Painel da corrida (Race Board)", "Faixa de TV que alterna gaps, volta mais rápida, boxes e legenda.", ""),
        ["winner"] = ("Vencedor", "Tela do vencedor ao fim da corrida.", ""),
        ["livespeed"] = ("Velocidade ao vivo", "Painel de TV com a sua velocidade em km/h e mph.", ""),
        ["racestart"] = ("Largada 0-200", "Tempo de 0 a 100 e 200 km/h na largada.", ""),
        ["racecontrol"] = ("Controle de prova", "Bandeiras, incidentes e barra de parada lenta.", ""),
        ["battle"] = ("Duelo (Battle)", "Duelo com o carro da frente: DRS e velocidades.", ""),
        ["poscaption"] = ("Legenda de posição", "Aparece quando você muda de posição, com a bandeira do país.", ""),
        ["ticker"] = ("Faixa de classificação", "Faixa inferior com 5 pilotos por página.", ""),
        ["qualitower"] = ("Torre da classificação", "Tempos de volta de todos os pilotos na classificação.", ""),
        ["qualilap"] = ("Volta lançada (Quali Lap)", "Cronômetro, parciais e diferenças da sua volta.", ""),
        ["qualiboard"] = ("Painel da classificação (Qualy Board)", "Faixa de TV com volta de saída, intermediárias e resultado.", ""),
        ["qualiresult"] = ("Resultado da classificação", "Tabela final ao encerrar a classificação.", ""),
        ["lapdelta"] = ("Delta da volta", "Diferença ao vivo para a melhor volta, parciais e última volta.", ""),
        ["gpxgauges"] = ("Instrumentos GPxCInfo", "Velocímetro e instrumentos do GPxPatch.", ""),
        ["gpxsat"] = ("Telas do GPxSAT", "Camada de telas de TV do GPxPatch (cada peça tem a sua regra).", ""),
    };

    public static (string Title, string Desc, string Icon) For(WidgetDef def)
        => Map.TryGetValue(def.Id, out var t) ? t : (def.DisplayName, "Widget do overlay.", "");
}

/// <summary>
/// Cartão da tela "Telas e exibição": um widget inteiro ou uma peça de widget composto (Race Board, GPxSAT...), numa situação
/// (Treino/Classificação/Corrida). Adapta <see cref="WidgetVm"/>/<see cref="ShowVm"/>/<see cref="ShowPieceVm"/> para uma frase editável.
/// </summary>
public sealed class ScreenCardVm : Notify
{
    static readonly IReadOnlyList<TextChoice> WidgetTriggers =
        [new("no padrão do tema", ShowTriggers.Native), new("sempre", ShowTriggers.Always), new("a cada evento…", ShowTriggers.Events), new("periodicamente", ShowTriggers.Periodic)];
    static readonly IReadOnlyList<TextChoice> PieceTriggers =
        [new("no padrão do tema", ShowTriggers.Native), new("a cada evento…", ShowTriggers.Events), new("periodicamente", ShowTriggers.Periodic)];
    static readonly IReadOnlyList<TextChoice> FormChoices = [new("estática (topo da lista)", ShowForms.Static), new("dinâmica (ao redor do jogador)", ShowForms.Dynamic)];

    readonly bool _defaultVisible;
    readonly Action? _preview;
    string? _previewStatus;
    ImageSource? _previewImage;
    bool _previewOpen;

    public ScreenCardVm(WidgetVm owner, ShowPieceVm? piece, int pieceIndex, string session, bool defaultVisible, Action<ScreenCardVm>? previewRequested)
    {
        Owner = owner; Piece = piece; Session = session; _defaultVisible = defaultVisible;
        PieceIndex = pieceIndex;
        var t = ScreenTexts.For(owner.Def);
        if (piece is null) { Title = t.Title; Description = t.Desc; Icon = t.Icon; }
        else { Title = piece.Label; Description = "Parte do " + t.Title + " (prioridade " + (pieceIndex + 1) + ")."; Icon = t.Icon; }
        ParentTitle = piece is null ? "" : t.Title;
        Help = BuildHelp();
        if (previewRequested is not null) _preview = () => previewRequested(this);
        owner.PropertyChanged += (_, _) => Refresh();
        owner.Show.PropertyChanged += (_, _) => Refresh();
        if (piece is not null) piece.PropertyChanged += (_, _) => Refresh();
        owner.Edited += (_, _) => Refresh();
    }

    public WidgetVm Owner { get; }
    public ShowPieceVm? Piece { get; }
    ShowVm Show => Owner.Show;
    public string Session { get; }
    public int PieceIndex { get; }
    public bool IsPiece => Piece is not null;
    public string Title { get; }
    public string ParentTitle { get; }
    public string Description { get; }
    public string Icon { get; }
    public string Help { get; }

    string BuildHelp()
    {
        string native = Show.Def.NativeLabel;
        string basis = IsPiece
            ? $"Esta é uma peça do {ParentTitle}: o painel mostra uma peça por vez, na ordem de prioridade do tema (a peça 1 vence as demais)."
            : "Cada cartão é um widget do overlay. A ordem dos cartões é a prioridade/ordem de desenho do widget.";
        return $"{Description}\n\n{basis}\n\nPadrão do tema: {native}.\n\n"
             + "Ligado/Desligado vale só para esta situação. \"A cada evento\" mostra o item por alguns segundos quando algo acontece (volta, boxes, bandeira…); "
             + "\"periodicamente\" repete a cada N segundos. Campos alterados ganham um marcador; \"Restaurar padrão\" volta ao comportamento original do tema.";
    }

    void Refresh() => Raise(string.Empty);   // string.Empty = todas as propriedades mudaram

    // ---- ligado/desligado ----
    public bool IsOn
    {
        get => IsPiece ? Piece!.Enabled : Owner.Visible && InSession;
        set
        {
            if (IsPiece) { Piece!.Enabled = value; return; }
            if (value)
            {
                Owner.Visible = true;
                SetSession(true);
            }
            else if (!SetSession(false)) Owner.Visible = false;   // ultima situacao do widget: desliga o widget
        }
    }

    bool InSession => Session switch { SessionIds.Practice => Owner.ShowPractice, SessionIds.Qualify => Owner.ShowQualify, _ => Owner.ShowRace };

    /// <summary>Liga/desliga a situação do cartão no widget; false se não foi possível (única situação marcada).</summary>
    bool SetSession(bool on)
    {
        switch (Session)
        {
            case SessionIds.Practice: Owner.ShowPractice = on; break;
            case SessionIds.Qualify: Owner.ShowQualify = on; break;
            default: Owner.ShowRace = on; break;
        }
        return InSession == on;
    }

    public string StateText => IsOn ? "Ligado" : "Desligado";

    // ---- frase ----
    public bool HasTrigger => IsPiece || Show.HasTriggers;
    public bool NoTrigger => !HasTrigger;
    public bool CanReorder => !IsPiece;
    static readonly IReadOnlyList<TextChoice> PieceTriggersLaps = [.. PieceTriggers, new("a cada N voltas…", ShowTriggers.Laps)];
    static readonly IReadOnlyList<TextChoice> PieceTriggersMerged =
        [new("sempre (puro GPxPatch)", ShowTriggers.Native), new("a cada evento…", ShowTriggers.Events), new("periodicamente", ShowTriggers.Periodic), new("a cada N voltas…", ShowTriggers.Laps)];
    public IReadOnlyList<TextChoice> Triggers => IsPiece ? (Piece!.Def.LapsByDefault ? PieceTriggersMerged : Piece.Def.EveryLaps is null ? PieceTriggers : PieceTriggersLaps) : WidgetTriggers;
    public string Trigger
    {
        get => IsPiece ? Piece!.Trigger : Show.Trigger;
        set { if (IsPiece) Piece!.Trigger = value; else Show.Trigger = value; }
    }
    public bool IsNative => IsPiece ? Piece!.IsNative : Show.IsNative;
    public bool IsEvents => IsPiece ? Piece!.IsEvents : Show.IsEvents;
    public bool IsPeriodic => IsPiece ? Piece!.IsPeriodic : Show.IsPeriodic;
    public bool IsLaps => IsPiece && Piece!.IsLaps;
    public ShowNumVm? EveryLaps => IsPiece ? Piece!.EveryLaps : null;
    public IReadOnlyList<TextChoice>? LapsOfs => IsPiece ? Piece!.LapsOfs : null;
    public string? LapsOf { get => IsPiece ? Piece!.LapsOf : null; set { if (IsPiece && value is not null) Piece!.LapsOf = value; } }
    public string NativeText => IsPiece ? "pela regra do tema" : Show.Def.NativeLabel.ToLowerInvariant().Replace("padrão:", "").Trim();
    public ObservableCollection<ShowCheckVm> Events => IsPiece ? Piece!.Events : Show.Events;
    public ShowNumVm Period => IsPiece ? Piece!.Period : Show.Period;
    public ShowNumVm Duration => IsPiece ? Piece!.Duration : Show.Duration;
    public ShowNumVm FadeIn => IsPiece ? Piece!.FadeIn : Show.FadeIn;
    public ShowNumVm FadeOut => IsPiece ? Piece!.FadeOut : Show.FadeOut;
    public bool ShowDuration => IsPiece ? Piece!.HasDuration : Show.ShowDuration;
    public bool ShowFades => IsPiece ? Piece!.IsTimed : Show.ShowFades;

    public bool HasList => !IsPiece && Show.HasList;
    public IReadOnlyList<TextChoice> Forms => FormChoices;
    public string Form { get => Show.Form; set => Show.Form = value; }
    public bool IsDynamic => !IsPiece && Show.IsDynamic;
    public ShowNumVm? Rows => IsPiece ? null : Show.Rows;
    public ShowNumVm? Around => IsPiece ? null : Show.Around;
    public ObservableCollection<ShowNumVm>? Timings => IsPiece ? null : Show.Timings;
    public bool HasTimings => !IsPiece && Show.HasTimings;

    // ---- valor alterado / restaurar ----
    public bool IsModified
    {
        get
        {
            if (IsPiece) return Owner.ShowOverrides?.Pieces?.ContainsKey(Piece!.Def.Id) == true;
            return Owner.ShowOverrides is not null || Owner.Visible != _defaultVisible || !SessionIds.SameSet(Owner.Sessions, Owner.Def.Sessions);
        }
    }

    /// <summary>Restaurar padrão do tema deste cartão: exibição (e, no widget, também ligado/desligado e situações).</summary>
    public void ResetToDefault()
    {
        if (IsPiece)
        {
            var p = Piece!;
            var plan = ShowPlan.For(new WidgetSettings { Id = Owner.Id }, Owner.ThemeId);
            p.Enabled = plan.PieceEnabled(p.Def.Id);
            p.Trigger = plan.PieceTrigger(p.Def.Id);   // mescla (voltas) na peca time do GPxSAT; "native" nas demais
            p.Duration.Value = plan.PieceDuration(p.Def.Id, (p.Def.Duration ?? ShowDef.GenericDuration).Default);
            p.Period.Value = plan.PiecePeriod(p.Def.Id);
            p.EveryLaps.Value = plan.PieceEveryLaps(p.Def.Id);
            p.LapsOf = plan.PieceLapsOf(p.Def.Id);
            p.FadeIn.Value = plan.PieceFadeIn(p.Def.Id);
            p.FadeOut.Value = plan.PieceFadeOut(p.Def.Id);
            var ev = plan.PieceEvents(p.Def.Id);
            foreach (var e in p.Events) e.Checked = ev.Contains(e.Id, StringComparer.OrdinalIgnoreCase);
            return;
        }
        Owner.ResetShow();
        Owner.ResetSessions();
        Owner.Visible = _defaultVisible;
    }

    // ---- prévia ----
    public bool PreviewOpen
    {
        get => _previewOpen;
        set { if (Set(ref _previewOpen, value) && value) _preview?.Invoke(); }
    }
    public ImageSource? PreviewImage { get => _previewImage; set => Set(ref _previewImage, value); }
    public string? PreviewStatus { get => _previewStatus; set => Set(ref _previewStatus, value); }
    public bool HasPreview => _preview is not null;
}
