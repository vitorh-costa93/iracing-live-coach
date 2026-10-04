using System.Globalization;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Resultado da classificação ao fim da sessão (<see cref="QualiEndState"/>: relógio zerado, bandeira xadrez ou todos com a bandeirada):
/// aparece por <c>showFor</c> s; uma volta final que muda a tabela enquanto ele está na tela estende o tempo, depois que saiu o traz de
/// volta. Com <c>always</c> mostra a lista atual o tempo todo. Janela de tamanho fixo para as opções (linhas e bloco de eliminados reservados).
/// 2018 (ref. quali-2018-eliminated.jpg e o último quadro de quali-2018-sheet-2.jpg): com <c>eliminationFrom</c> &gt; 0, bloco "ELIMINATED"
/// (uma faixa por eliminado: caixa vermelha, SOBRENOME e a diferença grande para o último classificado, sem retrato/bandeira) e, abaixo,
/// a tabela "CLASSIFICATION" (posição, Nome SOBRENOME, tempo do 1º e +diferença dos demais), jogador destacado.
/// 2004 (ref. quali-2004-sheet.jpg): faixa de siglas [posição][SIGLA][tempo/+gap] com o cabeçalho [Q|CLASSIFICATION] (xadrez no fim),
/// eliminados com a caixa de posição clara com contorno e número vermelhos (como os números da zona na TV e na torre).
/// 1998 (ref. quali-1998-classification-list.jpg): duas colunas [caixa amarela][NOME], 1º com o tempo e os demais a diferença sem "+".
/// </summary>
public sealed class QualiResultWidget : IWidget
{
    public string Id => "qualiresult";
    WidgetSettings _cfg = new() { Id = "qualiresult" };
    ThemeStyle _style = ThemeStyle.Broadcast98;

    public void UseTheme(Theme.Theme theme) { _style = theme.Style; }
    public void Configure(WidgetSettings s) { _cfg = s; }

    public (float Width, float Height) DesignSize => _style switch
    {
        ThemeStyle.Modern2018 => Size18,
        ThemeStyle.Broadcast2000s => Size04,
        _ => Size98,
    };

    // ---- Opções (WidgetCatalog.OptionsFor(tema, "qualiresult")) ----
    int Num(string id, int def, int min, int max)
        => double.TryParse(_cfg.OptionOr(id, def.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v)
            ? (int)Math.Clamp(Math.Round(v), min, max) : def;
    int Rows => Num("rows", 10, 3, 30);
    int Cutoff => Num("eliminationFrom", 0, 0, 30);
    int MaxElim => Num("maxEliminated", 5, 3, 5);
    double ShowFor => Num("showFor", 15, 5, 60);
    bool Always => _cfg.Option("always") is { } v && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);

    const double FadeIn = 0.3, FadeOut = 0.6;

    /// <summary>Opacidade do resultado: 0 fora da janela de exibição (<see cref="QualiEndState.Window"/>), com fade na entrada e na saída.</summary>
    public static float Alpha(QualiEndState? end, double now, double showFor, bool always)
    {
        if (always) return 1f;
        if (end is null) return 0f;
        var (start, stop) = end.Window(showFor);
        if (now < start || now >= stop) return 0f;
        if (now - start < FadeIn) return (float)((now - start) / FadeIn);
        if (stop - now < FadeOut) return (float)((stop - now) / FadeOut);
        return 1f;
    }

    /// <summary>Linhas da lista: as <paramref name="rows"/> primeiras, com o jogador sempre (no lugar da última se estiver fora).</summary>
    public static IReadOnlyList<QualiRow> Pick(IReadOnlyList<QualiRow> all, int rows)
    {
        int pi = -1;
        for (int i = 0; i < all.Count; i++) if (all[i].IsPlayer) { pi = i; break; }
        return StandingsSelector.Select(all.Count, pi, rows - 1, 1).Select(p => all[p.Index]).ToList();
    }

    /// <summary>Tempo do 1º; diferença dos demais (com ou sem "+"); null = sem tempo.</summary>
    string? Value(QualiRow r, bool sign)
    {
        if (r.BestLap is not { } best) return null;
        return r.Rank == 1 || r.GapToFirst is not { } g ? _cfg.Fmt.FormatLapTime(best) : _cfg.Fmt.FormatGap(Math.Max(0, g), defaultSign: sign);
    }

    static Color4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);
    static readonly Color4 ZoneFill = Rgb(178, 18, 28), White = Rgb(255, 255, 255);

    readonly FieldCodes _codes = new();

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        if (!m.Connected || m.Quali is not { Rows.Count: > 0 } q) return;
        _codes.Update(q.Rows.Select(r => r.Car));
        float a = Alpha(m.QualiEnd, m.Now, ShowFor, Always);
        BroadcastUi.WithAlpha(c, a, () =>
        {
            bool ended = m.QualiEnd is not null;
            switch (_style)
            {
                case ThemeStyle.Modern2018: Draw18(c, q, ended); break;
                case ThemeStyle.Broadcast2000s: Draw04(c, q, ended); break;
                default: Draw98(c, q); break;
            }
        });
    }

    // ---- 2018 ----
    const float W18 = 500, ERule = 4, EHead = 44, EPitch = 52, EGap = 12, CHead = 46, CRule = 4, CPad = 6, CPitch = 32;
    float ElimH18 => Cutoff > 0 ? ERule + EHead + MaxElim * EPitch + EGap : 0;
    (float, float) Size18 => (W18, ElimH18 + CHead + CRule + 2 * CPad + Rows * CPitch);

    void Draw18(ThemeCanvas c, QualiTableState q, bool ended)
    {
        var t = c.Theme;
        var rows = q.Rows;
        var field = rows.Select(r => r.Car).ToList();
        int cut = Cutoff;
        float y = 0;
        if (cut > 0)
        {
            DrawEliminated18(c, t, rows, field, cut, y);
            y += ElimH18;
        }

        // Tabela "CLASSIFICATION": cabeçalho preto (cantos de cima arredondados), filete vermelho e as linhas.
        c.FillRoundRect(0, y, W18, CHead, 7, t.PanelFill);
        c.FillRect(0, y + CHead - 8, W18, 8, t.PanelFill);
        c.Text("CLASSIFICATION", t.Title with { Size = 24, Tracking = 1f }, 18, y, W18 - 120, CHead, t.TitleColor);
        if (ended) Chrome.Checkered(c, W18 - 18 - 48, y + (CHead - 24) / 2, 48, 24);
        y += CHead;
        c.FillRect(0, y, W18, CRule, t.AccentBar);
        y += CRule;
        var picks = Pick(rows, Rows);
        float bodyH = 2 * CPad + picks.Count * CPitch;
        c.FillRect(0, y, W18, bodyH, t.PanelFill);
        float ry = y + CPad;
        const float box = 28;
        foreach (var r in picks)
        {
            if (r.IsPlayer) c.FillRect(0, ry, W18, CPitch, Rgb(255, 255, 255, 0.12f));
            float by = ry + (CPitch - box) / 2;
            Chrome.PosBox(c, 14, by, 34, box, r.Rank.ToString(CultureInfo.InvariantCulture), t.Numbers with { Size = 19 }, r.InEliminationZone(cut) ? ZoneFill : null);
            c.FillRect(56, by + 3, 3, box - 6, CaptionPlate.ClassColor(r.Car, field));
            string full = _cfg.Fmt.Name is null && _cfg.Fmt.CarNumber != true ? r.Car.Name : _cfg.Name(r.Car, BroadcastUi.ShortName(r.Car, field));
            Chrome.TwoWeightName(c, full, t.Text with { Size = 19 }, 68, by, box, 290, r.IsPlayer ? t.PlayerColor : White);
            if (Value(r, true) is { } v)
                c.Text(v, BroadcastUi.Fit(c, v, t.Numbers with { Size = 21 }, 120), W18 - 136, by, 122, box, t.ValueColor, HAlign.Right);
            else
                c.Text("NO TIME", t.Label with { Size = 16 }, W18 - 136, by, 122, box, t.LabelColor, HAlign.Right);
            ry += CPitch;
        }
    }

    /// <summary>Bloco "ELIMINATED": filete vermelho, cabeçalho preto e uma faixa por eliminado (caixa vermelha, SOBRENOME, +diferença grande
    /// para o último classificado). Reserva <see cref="MaxElim"/> faixas; o jogador eliminado além do limite entra no lugar da última.</summary>
    void DrawEliminated18(ThemeCanvas c, Theme.Theme t, IReadOnlyList<QualiRow> rows, List<Ams2.Core.CarSnapshot> field, int cut, float y)
    {
        var zone = rows.Where(r => r.Rank >= cut).ToList();
        var picks = Pick(zone, MaxElim);
        double? reference = cut >= 2 && cut - 1 <= rows.Count ? rows[cut - 2].BestLap : null;
        c.FillRect(0, y, W18, ERule, t.AccentBar);
        y += ERule;
        c.FillRect(0, y, W18, EHead, t.PanelFill);
        c.Text("ELIMINATED", t.Title with { Size = 26, Tracking = 1f }, 0, y, W18, EHead, t.TitleColor, HAlign.Center);
        y += EHead;
        foreach (var r in picks)
        {
            c.FillRect(0, y, W18, EPitch, t.PanelFill);
            c.FillRect(0, y + EPitch - 1, W18, 1, Rgb(255, 255, 255, 0.12f));
            float by = y + (EPitch - 34) / 2;
            Chrome.PosBox(c, 14, by, 38, 34, r.Rank.ToString(CultureInfo.InvariantCulture), t.Numbers with { Size = 21 }, ZoneFill);
            c.FillRect(60, by + 4, 4, 26, CaptionPlate.ClassColor(r.Car, field));
            string nm = _cfg.Name(r.Car, BroadcastUi.ShortName(r.Car, field)).ToUpperInvariant();
            c.Text(nm, BroadcastUi.Fit(c, nm, t.Text with { Size = 24, Weight = 800 }, 220), 74, by, 230, 34, r.IsPlayer ? t.PlayerColor : White);
            string? v = r.BestLap is { } b
                ? (reference is { } refT ? _cfg.Fmt.FormatGap(Math.Max(0, b - refT)) : r.GapToFirst is { } g ? _cfg.Fmt.FormatGap(Math.Max(0, g)) : _cfg.Fmt.FormatLapTime(b))
                : null;
            if (v is not null)
                c.Text(v, BroadcastUi.Fit(c, v, t.Numbers with { Size = 34, Weight = 800 }, 170), W18 - 196, y, 180, EPitch, White, HAlign.Right, t.ValueShadow);
            else
                c.Text("NO TIME", t.Label with { Size = 18 }, W18 - 196, y, 180, EPitch, t.LabelColor, HAlign.Right);
            y += EPitch;
        }
    }

    // ---- 2004–2008 (vocabulário da torre de siglas, QualiTowerWidget.Draw04) ----
    const float X04 = 4, Top04 = 4, Lab04 = 40, Pos04 = 40, Name04 = 82, Time04 = 124, H04 = 34, Pitch04 = 36, Lead04 = 6;
    (float, float) Size04 => (X04 + Pos04 + Name04 + Time04 + 6, Top04 + H04 + Lead04 + Rows * Pitch04 + 2);

    static readonly BarStop[] ZoneStops04 = [new(0f, Rgb(252, 244, 244, 0.94f)), new(1f, Rgb(216, 202, 204, 0.94f))];
    static readonly Color4 Red04 = Rgb(206, 22, 30);

    void Draw04(ThemeCanvas c, QualiTableState q, bool ended)
    {
        var t = c.Theme;
        // Cabeçalho no vocabulário da caixa do relógio: [Q | CLASSIFICATION], o rótulo vira xadrez com a sessão encerrada.
        float x = X04, y = Top04;
        if (ended) Chrome.Checkered(c, x, y, Lab04, H04);
        else Chrome.Box(c, x, y, Lab04, H04, "Q", t.Text, Chrome.CellKind.Black, HAlign.Center, 0);
        float hw = Pos04 + Name04 + Time04 - Lab04;
        Chrome.WhiteCell(c, x + Lab04, y, hw, H04, "CLASSIFICATION", BroadcastUi.Fit(c, "CLASSIFICATION", t.Text, hw - 16), HAlign.Center);
        y += H04 + Lead04;
        int cut = Cutoff;
        foreach (var r in Pick(q.Rows, Rows))
        {
            x = X04;
            if (r.InEliminationZone(cut))
            {
                c.FillRect(x, y + H04, Pos04, 1.5f, new Color4(0.04f, 0.04f, 0.08f, 0.6f));
                c.VGradientRect(x, y, Pos04, H04, ZoneStops04);
                c.StrokeRect(x + 1, y + 1, Pos04 - 2, H04 - 2, Red04, 2f);
                c.Text(r.Rank.ToString(CultureInfo.InvariantCulture), t.Numbers, x, y - 1, Pos04, H04, Red04, HAlign.Center);
            }
            else Chrome.PositionBox(c, x, y, Pos04, H04, r.Rank, t.Numbers);
            x += Pos04;
            string nm = _cfg.Name(r.Car, _codes);
            Chrome.WhiteCell(c, x, y, Name04, H04, nm, BroadcastUi.Fit(c, nm, t.Text, Name04 - 16), ink: r.IsPlayer ? Chrome.PlayerInk : null);
            x += Name04;
            if (Value(r, true) is { } v) Chrome.BlackCell(c, x, y, Time04, H04, v, BroadcastUi.Fit(c, v, t.Numbers, Time04 - 12));
            else Chrome.BlackCell(c, x, y, Time04, H04, "NO TIME", t.Label with { Size = 16 }, HAlign.Center);
            y += Pitch04;
        }
    }

    // ---- 1998–2001 (vocabulário da lista da torre, QualiTowerWidget.Draw98) ----
    const float X98 = 22, Top98 = 12, Head98 = 42, Pitch98 = 40, Box98H = 34, Box98W = 36, Name98 = 196, Val98 = 136, ColGap98 = 30, Bottom98 = 8;
    const float ColW98 = Box98W + 12 + Name98 + Val98;
    int PerCol98 => (Rows + 1) / 2;
    (float, float) Size98 => (2 * X98 + 2 * ColW98 + ColGap98, Top98 + Head98 + PerCol98 * Pitch98 + Bottom98);

    void Draw98(ThemeCanvas c, QualiTableState q)
    {
        var t = c.Theme;
        var (w, h) = Size98;
        c.Panel(0, 0, w, h);
        Chrome.Header(c, "QUALIFYING", X98, Top98, 2000, true, w - X98);
        float y0 = Top98 + Head98;
        var rows = q.Rows;
        var field = rows.Select(r => r.Car).ToList();
        var picks = Pick(rows, Rows);
        int cut = Cutoff, per = PerCol98;
        for (int i = 0; i < picks.Count; i++)
        {
            var r = picks[i];
            float x = X98 + (i / per) * (ColW98 + ColGap98), y = y0 + (i % per) * Pitch98;
            string rank = r.Rank.ToString(CultureInfo.InvariantCulture);
            if (r.InEliminationZone(cut))
            {
                c.FillRect(x, y, Box98W, Box98H, t.BrakeColor);
                c.Text(rank, t.Numbers, x, y - 1, Box98W, Box98H, White, HAlign.Center);
            }
            else Chrome.AccentBox(c, x, y, Box98W, Box98H, rank, t.Numbers);
            string name = _cfg.Name(r.Car, BroadcastUi.ShortName(r.Car, field)).ToUpperInvariant();
            c.Text(name, BroadcastUi.Fit(c, name, t.Text, Name98), x + Box98W + 12, y - 1, Name98, Box98H, r.IsPlayer ? t.PlayerColor : t.TextColor, shadow: t.TextShadow);
            float vx = x + ColW98 - Val98;
            if (Value(r, false) is { } v)
                c.Text(v, BroadcastUi.Fit(c, v, t.Numbers, Val98 - 6), vx, y, Val98, Box98H, t.ValueColor, HAlign.Right, t.ValueShadow);
            else
                c.Text("NO TIME", t.Label, vx, y, Val98, Box98H, t.ValueColor, HAlign.Right, t.TextShadow);
        }
    }
}
