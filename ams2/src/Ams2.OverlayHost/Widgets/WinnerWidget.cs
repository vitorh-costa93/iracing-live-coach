using System.Globalization;
using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Vencedor ao fim da corrida. 1998–2004: legenda com cabeçalho "Winner", nome, equipe e tempo/distância/média (~10 s).
/// 2018: banner superior "WINNER | Nome SOBRENOME", pódio de três cartões ou os dois (opções "style" e "showFor" do tema).
/// </summary>
public sealed class WinnerWidget : IWidget
{
    public string Id => "winner";
    public (float Width, float Height) DesignSize => _style switch
    {
        ThemeStyle.Broadcast98 => (1920, 300),
        ThemeStyle.Broadcast2000s => (CaptionPlate.WinnerWidth, CaptionPlate.Height),
        _ => Winner18.Size(Style18),
    };
    ThemeStyle _style;
    readonly Broadcast18Motion _banner18 = new(), _podium18 = new();
    public void UseTheme(Theme.Theme theme) => _style = theme.Style;
    WidgetSettings _cfg = new() { Id = "winner" };
    public void Configure(WidgetSettings s) => _cfg = s;

    /// <summary>Estilo do 2018: banner (padrão), podium ou both.</summary>
    string Style18 => _cfg.OptionOr("style", "banner").ToLowerInvariant() switch { "podium" => "podium", "both" => "both", _ => "banner" };
    /// <summary>Segundos na tela (2018), 5–30, padrão 12.</summary>
    double ShowFor18 => double.TryParse(_cfg.OptionOr("showFor", "12"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? Math.Clamp(v, 5, 30) : 12;
    double _podiumT = double.NaN;   // primeiro quadro com o pódio completo (2018)

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        if (!m.Connected || m.Session is not { } s) { _banner18.Reset(); _podium18.Reset(); _podiumT = double.NaN; return; }
        var b = BroadcastUi.State(m);
        if (b.Winner is not { } w) { _podiumT = double.NaN; _banner18.Reset(); _podium18.Reset(); return; }
        if (_style == ThemeStyle.Modern2018) { Draw18(c, m, s, w); return; }
        float alpha = _cfg.ColumnVisible("always") ? 1f : _style == ThemeStyle.Broadcast2000s
            ? BroadcastUi.Fade04(m.Now - w.FinishedT, BroadcastUi.WinnerHold) : BroadcastUi.Fade(m.Now - w.FinishedT, BroadcastUi.WinnerHold);
        BroadcastUi.WithAlpha(c, alpha, () => { if (_style == ThemeStyle.Broadcast98) Broadcast98RaceBoard.Winner(c, w, s.Cars, _cfg); else CaptionPlate.DrawWinner(c, w, s.Cars, _cfg); });
    }

    /// <summary>2018: o banner entra na bandeirada do vencedor; o pódio quando 2º e 3º também terminaram (ou saíram da corrida). Cada um fica showFor s.</summary>
    void Draw18(ThemeCanvas c, OverlayModel m, SessionSnapshot s, WinnerInfo w)
    {
        string style = Style18;
        var podium = Winner18.Podium(w, s.Cars);
        if (!Winner18.PodiumComplete(s.Cars)) _podiumT = double.NaN;
        else if (double.IsNaN(_podiumT)) _podiumT = Math.Max(m.Now, w.FinishedT);
        bool always = _cfg.ColumnVisible("always");
        double hold = ShowFor18;
        double session = BroadcastUi.State(m).SessionSeenT;
        float bannerA = always ? 1f : _banner18.Evaluate(m.Now, w.FinishedT, w.FinishedT + hold, session);
        float podiumA = always ? 1f : _podium18.Evaluate(m.Now, _podiumT, _podiumT + hold, session);
        if (style != "podium") BroadcastUi.WithReveal18(c, bannerA, Winner18.Width, Winner18.BannerH, () => Winner18.DrawBanner(c, 0, 0, w, s.Cars, _cfg));
        if (style != "banner")
        {
            float top = style == "both" ? Winner18.BannerH + Winner18.Gap : 0;
            BroadcastUi.WithReveal18(c, podiumA, Winner18.Width, Winner18.PodiumH, () => Winner18.DrawPodium(c, 0, top, podium, s.Cars, _cfg), y: top);
        }
    }
}

/// <summary>
/// Desenho do vencedor 2018 (ref. f1-2018-crops-pitmap-winner-finishtower.jpg, banner no recorte superior direito; pódio no 6º quadro de
/// f1-2018-scan-finish-t925-1190.jpg). Sem retratos, logos e bandeiras no AMS2: bloco na cor da classe com o número do carro.
/// </summary>
public static class Winner18
{
    public const float Width = 780, BannerH = 90, PodiumH = 380, Gap = 14;
    const float CheckW = 260, SideW = 220, SideH = 318, SideTop = 26, WinW = 264, CardGap = 18;

    public static (float Width, float Height) Size(string style) => style switch
    {
        "podium" => (Width, PodiumH),
        "both" => (Width, BannerH + Gap + PodiumH),
        _ => (Width, BannerH),
    };

    /// <summary>1º (o vencedor registrado na bandeirada), 2º e 3º da classificação final: só quem já recebeu a bandeirada.</summary>
    public static CarSnapshot?[] Podium(WinnerInfo w, IReadOnlyList<CarSnapshot> cars)
    {
        CarSnapshot? At(int p) => cars.FirstOrDefault(c => c.Position == p && c.RaceState == RaceState.Finished);
        return [w.Car, At(2), At(3)];
    }

    /// <summary>Pódio completo: 1º, 2º e 3º terminaram, ou a posição não tem carro em corrida (grid menor, abandono).</summary>
    public static bool PodiumComplete(IReadOnlyList<CarSnapshot> cars)
        => Enumerable.Range(1, 3).All(p => cars.FirstOrDefault(c => c.Position == p) is not { } car || car.RaceState != RaceState.Racing);

    /// <summary>Nome completo (a TV mostra nome + sobrenome); com formato de nome escolhido no perfil, o do perfil.</summary>
    static string FullName(CarSnapshot car, IReadOnlyList<CarSnapshot> field, WidgetSettings cfg)
        => cfg.Fmt.Name is null && cfg.Fmt.CarNumber != true ? car.Name : cfg.Name(car, BroadcastUi.ShortName(car, field));

    static Color4 White => new(1, 1, 1, 1);

    /// <summary>Textura de bandeira xadrez translúcida (4 fileiras) que esmaece para a direita, por cima da placa.</summary>
    static void CheckerFade(ThemeCanvas c, float x, float y, float w, float h, float alpha)
    {
        float sq = h / 4;
        int cols = (int)Math.Ceiling(w / sq);
        for (int i = 0; i < cols; i++)
        {
            float a = alpha * (1f - (float)i / cols);
            float cw = Math.Min(sq, w - i * sq);
            for (int r = 0; r < 4; r++)
            {
                var col = (i + r) % 2 == 0 ? new Color4(0.95f, 0.95f, 0.97f, a) : new Color4(0.02f, 0.02f, 0.03f, a);
                c.FillRect(x + i * sq, y + r * sq, cw, sq, col);
            }
        }
    }

    /// <summary>
    /// Banner 780×90: placa preta arredondada; à esquerda "WINNER" largo sobre a bandeira xadrez recortada, filete vertical,
    /// "Nome SOBRENOME" (nome regular, sobrenome negrito) + número em itálico na cor da classe, equipe embaixo; tempo/média (coluna "stats")
    /// e, na ponta direita, faixas inclinadas na cor da classe no lugar do logo da equipe.
    /// </summary>
    public static void DrawBanner(ThemeCanvas c, float x, float y, WinnerInfo win, IReadOnlyList<CarSnapshot> field, WidgetSettings cfg)
    {
        var t = c.Theme;
        var car = win.Car;
        float w = Width, h = BannerH;
        var tick = CaptionPlate.ClassColor(car, field);
        c.FillRoundRect(x, y, w, h, 10, t.PanelFill);
        CheckerFade(c, x + 6, y + 6, CheckW - 6, h - 12, 0.5f);
        c.Text("WINNER", BroadcastUi.Fit(c, "WINNER", t.Title with { Size = 44, Weight = 800, Tracking = 1.5f }, CheckW - 40), x + 10, y, CheckW - 10, h, White, HAlign.Center);
        c.FillRect(x + CheckW + 2, y + 16, 2f, h - 32, new Color4(1, 1, 1, 0.55f));

        // Ponta direita: duas faixas inclinadas (cor da classe e branco), como o logo da equipe no banner da TV.
        float s = 26, f1 = 30, f2 = 10, stripeX = x + w - 30 - f1 - 6 - f2 - s;
        c.FillSlant(stripeX, y + 18, f1, h - 36, s, tick);
        c.FillSlant(stripeX + f1 + 6, y + 18, f2, h - 36, s, new Color4(0.92f, 0.92f, 0.94f, 0.9f));

        // Linha 1: nome + número na largura toda; linha 2: equipe à esquerda e, com "stats", tempo total · média à direita.
        float nx = x + CheckW + 22, right = stripeX - 14;
        string full = FullName(car, field, cfg), num = CaptionPlate.CarNumber(car);
        var numFont = t.Numbers with { Size = 28, Italic = true, Weight = 700 };
        float numW = c.Measure(num, numFont) + 14;
        float nw = Chrome.TwoWeightName(c, full, t.Text with { Size = 30 }, nx, y + 8, 44, right - nx - numW, White);
        c.Text(num, numFont, nx + nw + 12, y + 8, numW + 8, 44, tick);
        float teamRight = right;
        bool showTeam = cfg.ColumnVisible("team");
        string team = showTeam ? BroadcastUi.Team(car) : "";
        if (cfg.ColumnVisible("stats"))
        {
            var su = cfg.Fmt.SpeedOrDefault;
            var sf = t.Label with { Size = 18 };
            string time = BroadcastUi.RaceTime(win.TotalSeconds);
            string st = time + "  ·  " + DisplayFormat.SpeedFromKph(win.AvgKmh, su).ToString("0.0", CultureInfo.InvariantCulture) + (su == SpeedUnit.Mph ? " mph" : " km/h");
            // Sem espaço para equipe + tempo + média na 2ª linha: só o tempo total.
            float teamMin = showTeam ? Math.Min(c.Measure(team, t.Label with { Element = "name", Size = 16, Weight = 600 }), 200) : 0;
            if (c.Measure(st, sf) + teamMin + 20 > right - nx) st = time;
            float sw = c.Measure(st, sf);
            c.Text(st, sf, right - sw - 4, y + 50, sw + 6, 30, t.ValueColor);
            teamRight = right - sw - 20;
        }
        if (showTeam)
        {
            c.Text(team, BroadcastUi.Fit(c, team, t.Label with { Element = "name", Size = 21, Weight = 600 }, teamRight - nx), nx, y + 50, teamRight - nx + 6, 30, t.LabelColor);
        }
    }

    /// <summary>Pódio 780×380: cartões 2º (esquerda), vencedor (centro, maior e mais alto) e 3º (direita). Posição sem carro que terminou = cartão ausente.</summary>
    public static void DrawPodium(ThemeCanvas c, float x, float y, CarSnapshot?[] podium, IReadOnlyList<CarSnapshot> field, WidgetSettings cfg)
    {
        float total = SideW + CardGap + WinW + CardGap + SideW, x0 = x + (Width - total) / 2;
        if (podium[1] is { } p2) Card(c, x0, y + SideTop, SideW, SideH, 2, p2, field, cfg);
        if (podium[0] is { } p1) Card(c, x0 + SideW + CardGap, y, WinW, PodiumH, 1, p1, field, cfg);
        if (podium[2] is { } p3) Card(c, x0 + SideW + CardGap + WinW + CardGap, y + SideTop, SideW, SideH, 3, p3, field, cfg);
    }

    /// <summary>Cartão vertical: faixa preta com "WINNER" (centro) ou "2ND"/"3RD" (sufixo sobrescrito); corpo escuro com bloco inclinado
    /// na cor da classe e o número do carro grande em itálico (no lugar do retrato); embaixo nome, SOBRENOME em negrito e equipe.</summary>
    static void Card(ThemeCanvas c, float x, float y, float w, float h, int place, CarSnapshot car, IReadOnlyList<CarSnapshot> field, WidgetSettings cfg)
    {
        var t = c.Theme;
        bool win = place == 1;
        var tick = CaptionPlate.ClassColor(car, field);
        float head = win ? 58 : 50, foot = win ? 96 : 88, body = h - head - foot;
        // Cabeçalho (cantos de cima arredondados) e rodapé pretos; corpo cinza-escuro translúcido.
        c.FillRoundRect(x, y, w, head, 8, t.PanelFill);
        c.FillRect(x, y + head - 8, w, 8, t.PanelFill);
        c.FillRect(x, y + head, w, body, new Color4(0.10f, 0.11f, 0.14f, 0.90f));
        c.FillRoundRect(x, y + head + body, w, foot, 8, t.PanelFill);
        c.FillRect(x, y + head + body, w, 8, t.PanelFill);
        c.FillRect(x, y + head + body, w, 3, tick);

        if (win)
            c.Text("WINNER", BroadcastUi.Fit(c, "WINNER", t.Title with { Size = 38, Weight = 800, Tracking = 1f }, w - 20), x, y + 2, w, head, White, HAlign.Center);
        else
        {
            var (n, sfx) = CaptionPlate.Ordinal(place);
            var nf = t.Title with { Element = "position", Size = 36, Weight = 800 };
            var sf = t.Title with { Element = "position", Size = 18, Weight = 800 };
            float nw = c.Measure(n, nf);
            c.Text(n, nf, x + 14, y + 2, nw + 6, head, White);
            c.Text(sfx.ToUpperInvariant(), sf, x + 14 + nw + 2, y + 8, 40, 24, White);
        }

        // Corpo: bloco inclinado na cor da classe e o número do carro.
        float by = y + head, bh = body, sl = bh * 0.35f;
        // Bloco + faixa branca inteiros dentro do cartão: [x+10%, x+90%] já contando a inclinação.
        float bx = x + w * 0.10f, bw = w * 0.80f - sl - 18;
        c.FillSlant(bx, by + bh * 0.12f, bw, bh * 0.76f, sl, new Color4(tick.R, tick.G, tick.B, 0.85f));
        c.FillSlant(bx + bw + 8, by + bh * 0.12f, 10, bh * 0.76f, sl, new Color4(0.92f, 0.92f, 0.94f, 0.75f));
        string num = CaptionPlate.CarNumber(car);
        var numFont = t.Numbers with { Size = win ? 110 : 92, Italic = true, Weight = 800 };
        c.Text(num, BroadcastUi.Fit(c, num, numFont, w - 30), x, by, w, bh, White, HAlign.Center, t.ValueShadow);

        // Rodapé: nome regular, SOBRENOME negrito e equipe.
        string full = FullName(car, field, cfg);
        var parts = full.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string first = parts.Length > 1 ? string.Join(' ', parts[..^1]) : "";
        string last = (parts.Length > 0 ? parts[^1] : full).ToUpperInvariant();
        float fy = y + head + body + 6, tx = x + 14, tw = w - 24;
        if (first.Length > 0) c.Text(first, BroadcastUi.Fit(c, first, t.Label with { Element = "name", Size = 18, Weight = 400 }, tw), tx, fy, tw + 6, 24, White);
        c.Text(last, BroadcastUi.Fit(c, last, t.Text with { Size = win ? 30 : 26, Weight = 800 }, tw), tx, fy + 20, tw + 6, 38, White);
        if (cfg.ColumnVisible("team"))
        {
            string team = BroadcastUi.Team(car);
            c.Text(team, BroadcastUi.Fit(c, team, t.Label with { Element = "name", Size = 16 }, tw), tx, fy + (win ? 58 : 54), tw + 6, 24, t.LabelColor);
        }
    }
}
