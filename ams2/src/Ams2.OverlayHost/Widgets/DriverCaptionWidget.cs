using System.Globalization;
using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Legenda de piloto da transmissao: cabecalho branco (nome do widget em teal), nome em celula branca, equipe em celula
/// azul-ardosia; a direita caixinha do fornecedor de pneus e caixa de posicao (vermelha no lider).
/// Mostra o piloto do jogador por alguns segundos ao conectar, ao mudar de posicao e ao cruzar a linha; a coluna "always" a mantem fixa.
/// </summary>
public sealed class DriverCaptionWidget : IWidget
{
    public string Id => "drivercaption";
    public (float Width, float Height) DesignSize => _b18 ? (CaptionPlate.Caption18Width, CaptionPlate.Caption18Height) : (CaptionPlate.DriverWidth, CaptionPlate.Height);
    bool _b18;
    public void UseTheme(Theme.Theme theme) => _b18 = theme.Style == ThemeStyle.Modern2018;
    WidgetSettings _cfg = new() { Id = "drivercaption" };
    public void Configure(WidgetSettings s) => _cfg = s;

    /// <summary>Variante escolhida no perfil (2018): driver, startednow, result ou auto (padrão).</summary>
    string Variant18 => _cfg.OptionOr("variant", "auto").ToLowerInvariant();
    /// <summary>Segundos na tela por evento (2018), 3–15, padrão 6.</summary>
    double ShowFor18 => double.TryParse(_cfg.OptionOr("showFor", "6"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? Math.Clamp(v, 3, 15) : 6;
    double _finishT = double.NaN;   // primeiro quadro em que o jogador apareceu com a bandeirada (2018)

    /// <summary>
    /// Variante efetiva do 2018. Automático, como na TV: resultado depois da bandeirada do jogador; STARTED / NOW na 1ª volta da corrida
    /// e a cada mudança de posição (só com o grid de largada conhecido); a placa simples nos demais eventos (conexão, linha de chegada).
    /// </summary>
    public static string EffectiveVariant18(string variant, bool finished, bool hasGrid, bool firstLap, bool positionEvent) => variant switch
    {
        "driver" or "startednow" or "result" => variant,
        _ => finished ? "result" : hasGrid && (firstLap || positionEvent) ? "startednow" : "driver",
    };

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        if (!m.Connected || m.Session is not { } s || s.PlayerCar is not { } car) return;
        var b = BroadcastUi.State(m);
        if (_b18) { Draw18(c, m, s, car, b); return; }
        float alpha = 1f;
        if (!_cfg.ColumnVisible("always"))
        {
            double last = Math.Max(b.SessionSeenT, Math.Max(b.PlayerPositionChangedT, b.PlayerLapChangedT));
            alpha = BroadcastUi.Fade(m.Now - last, BroadcastUi.CaptionHold);
            if (b.Winner is { } w && m.Now - w.FinishedT < BroadcastUi.WinnerHold + 0.5) alpha = 0f; // a legenda do vencedor ocupa o lugar
        }
        BroadcastUi.WithAlpha(c, alpha, () => CaptionPlate.DrawDriver(c, car, s.Cars, _cfg));
    }

    /// <summary>2018: escolhe a variante, a janela de exibição (showFor) e desenha.</summary>
    void Draw18(ThemeCanvas c, OverlayModel m, SessionSnapshot s, CarSnapshot car, BroadcastState b)
    {
        bool race = s.Kind == SessionKind.Race;
        bool finished = race && car.RaceState == RaceState.Finished;
        if (!finished) _finishT = double.NaN;
        else if (double.IsNaN(_finishT)) _finishT = m.Now;
        int? started = race && m.Grid is { } g && g.TryGetValue(car.Index, out var gp) && gp > 0 ? gp : null;
        double last = Math.Max(b.SessionSeenT, Math.Max(b.PlayerPositionChangedT, b.PlayerLapChangedT));
        // Ultrapassagem = mudança de posição dentro da janela de exibição (mesmo que outro evento, como a linha de chegada, venha junto).
        bool posEvent = m.Now - b.PlayerPositionChangedT < ShowFor18;
        string v = EffectiveVariant18(Variant18, finished, started is not null, race && car.LapsCompleted == 0, posEvent);
        float alpha = 1f;
        if (!_cfg.ColumnVisible("always"))
        {
            double hold = ShowFor18, t0 = last;
            // Resultado: entra na bandeirada do jogador. Sem espera pelo vencedor: no 2018 o banner WINNER fica no alto da tela.
            if (v == "result" && finished) t0 = _finishT;
            alpha = BroadcastUi.Fade(m.Now - t0, hold);
        }
        BroadcastUi.WithAlpha(c, alpha, () => CaptionPlate.Draw18(c, v, car, s.Cars, started, _cfg));
    }
}

/// <summary>Desenho das legendas (piloto e vencedor), por tema.</summary>
public static class CaptionPlate
{
    public const float Height = 94, DriverWidth = 334, WinnerWidth = 448, Winner98Width = 450, Driver18Width = 480;
    /// <summary>Janela da legenda 2018 (todas as variantes): a de resultado é a mais larga, a STARTED / NOW a mais alta. Desenho alinhado embaixo.</summary>
    public const float Caption18Width = 600, Caption18Height = 132, Started18Top = 56;

    /// <summary>Ordinal em inglês separado em número e sufixo (1 st, 2 nd, 3 rd, 11 th, 22 nd...).</summary>
    public static (string Number, string Suffix) Ordinal(int n)
    {
        string num = n.ToString(CultureInfo.InvariantCulture);
        int h = n % 100, d = n % 10;
        string sfx = h is >= 11 and <= 13 ? "th" : d switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return (num, sfx);
    }

    /// <summary>Cor do tique (e do número) do carro: tom fixo da classe (o AMS2 não informa a cor da equipe).</summary>
    public static Color4 ClassColor(CarSnapshot car, IReadOnlyList<CarSnapshot> field)
        => Chrome.ClassTick(Math.Max(0, field.Select(x => x.ClassName).Distinct().ToList().IndexOf(car.ClassName)));

    /// <summary>
    /// Legenda 2018 na variante <paramref name="variant"/> (driver, startednow, result), alinhada embaixo da janela <see cref="Caption18Width"/> x <see cref="Caption18Height"/>.
    /// driver = placa preta (ref. f1-2018-driver-caption.jpg); startednow = linha do piloto + faixa cinza "STARTED 2nd | NOW 1st";
    /// result = caixa de posição branca grande e ponta diagonal com faixas na cor da classe (ref. f1-2018-result-caption.jpg).
    /// </summary>
    public static void Draw18(ThemeCanvas c, string variant, CarSnapshot car, IReadOnlyList<CarSnapshot> field, int? started, WidgetSettings cfg)
    {
        var t = c.Theme;
        string name = cfg.Name(car, BroadcastUi.ShortName(car, field)), team = cfg.ColumnVisible("team") ? BroadcastUi.Team(car) : "";
        string full = cfg.Fmt.Name is null && cfg.Fmt.CarNumber != true ? car.Name : name;
        string supplier = cfg.ColumnVisible("tyre") ? car.TyreSupplier : "";
        var tick = ClassColor(car, field);
        float H = Caption18Height;
        switch (variant)
        {
            case "result":
                Plate18(c, 0, H - Height, Caption18Width, Height, car.Position, full, CarNumber(car), team, bigBox: true, tick: tick, slantEnd: true);
                return;
            case "startednow":
                StartedNow18(c, car, full, team, started, tick);
                return;
            default:
                Plate18(c, 0, H - Height, Driver18Width, Height, car.Position, full, CarNumber(car), team, bigBox: false, tick: tick);
                if (supplier.Length > 0) c.Text(supplier, t.Label with { Weight = 700 }, Driver18Width - 50, H - Height + 50, 36, 32, t.ValueColor, HAlign.Center);
                return;
        }
    }

    /// <summary>Variante STARTED / NOW (ref. 4o recorte de f1-2018-crops-livespeed-racestart-radio-caption.jpg): linha preta com caixa de posição,
    /// tique, nome e número; embaixo faixa cinza translúcida com os dois ordinais grandes (sufixo sobrescrito) separados por filete vertical.</summary>
    static void StartedNow18(ThemeCanvas c, CarSnapshot car, string full, string team, int? started, Color4 tick)
    {
        var t = c.Theme;
        float w = Driver18Width, top = Started18Top, bh = Caption18Height - top;
        c.FillRect(0, 0, w, top, t.PanelFill);
        Chrome.PosBox(c, 10, 8, 40, 40, car.Position.ToString(CultureInfo.InvariantCulture), t.Numbers with { Size = 24 });
        Chrome.Tick(c, 60, 12, 32, tick);
        var teamFont = t.Label with { Size = 18 };
        float teamW = team.Length > 0 ? Math.Min(c.Measure(team, teamFont), 130) : 0;
        string num = CarNumber(car);
        float nx = 74, maxName = w - nx - 64 - (teamW > 0 ? teamW + 20 : 0);
        float nw = Chrome.TwoWeightName(c, full, t.Text with { Size = 26 }, nx, 6, 44, maxName, t.TextColor);
        c.Text(num, t.Numbers with { Size = 28, Italic = true, Weight = 700 }, nx + nw + 14, 6, 70, 44, tick);
        if (teamW > 0) c.Text(team, BroadcastUi.Fit(c, team, teamFont, teamW), w - teamW - 14, 6, teamW + 6, 44, t.LabelColor, HAlign.Right);

        c.FillRect(0, top, w, bh, new Color4(38 / 255f, 40 / 255f, 46 / 255f, 0.82f));
        c.FillRect(w / 2 - 0.75f, top + 16, 1.5f, bh - 32, new Color4(1, 1, 1, 0.35f));
        var (sn, ss) = started is { } p ? Ordinal(p) : ("-", "");
        var (nn, ns) = Ordinal(car.Position);
        OrdinalBlock(c, "STARTED", sn, ss, 0, top, w / 2, bh);
        OrdinalBlock(c, "NOW", nn, ns, w / 2, top, w / 2, bh);
    }

    /// <summary>"ROTULO 12th": rótulo pequeno na linha de base do ordinal grande, sufixo pequeno sobrescrito; o conjunto centrado em [x, x+w].</summary>
    static void OrdinalBlock(ThemeCanvas c, string label, string number, string suffix, float x, float y, float w, float h)
    {
        var t = c.Theme;
        var lf = t.Label with { Size = 20 };
        const float N = 50;
        var nf = t.Numbers with { Size = N };
        var sf = t.Label with { Size = 22 };
        float lw = c.Measure(label, lf), nw = c.Measure(number, nf), sw = suffix.Length > 0 ? c.Measure(suffix, sf) : 0;
        float total = lw + 16 + nw + (sw > 0 ? 3 + sw : 0), x0 = x + (w - total) / 2;
        float cy = y + h / 2 + 1, baseline = cy + N * 0.36f;
        c.Text(label, lf, x0, baseline - 20 * 0.36f - 14, lw + 6, 28, t.TextColor);
        c.Text(number, nf, x0 + lw + 16, cy - 36, nw + 6, 72, t.TextColor);
        if (sw > 0) c.Text(suffix, sf, x0 + lw + 16 + nw + 3, cy - N * 0.36f - 5, sw + 6, 26, t.TextColor);
    }

    /// <summary>
    /// Placa 2018 (ref. f1-2018-driver-caption.jpg / f1-2018-result-caption.jpg): placa preta translúcida, caixa de posição branca
    /// (grande no resultado), tique vertical, "Nome SOBRENOME" em dois pesos, número do carro em itálico e a equipe embaixo.
    /// O AMS2 não informa cor/logo da equipe: tique no vermelho do tema e número em cinza claro, sem logos. Devolve a borda direita do texto.
    /// </summary>
    public static float Plate18(ThemeCanvas c, float x, float y, float w, float h, int position, string fullName, string number, string team, bool bigBox, float rightReserve = 0,
        Color4? tick = null, bool slantEnd = false)
    {
        var t = c.Theme;
        if (slantEnd)
        {
            // Ponta direita em diagonal "/" seguida de duas faixas inclinadas (no lugar da bandeira do país): cor da classe e branco.
            float s = h * 0.42f, gap = 6, f1 = 14, f2 = 8, right = x + w - (gap + f1 + gap + f2);
            c.FillPolygon([new(x, y), new(right, y), new(right - s, y + h), new(x, y + h)], t.PanelFill);
            c.FillSlant(right - s + gap, y, f1, h, s, tick ?? t.AccentBar);
            c.FillSlant(right - s + gap + f1 + gap, y, f2, h, s, new Color4(0.92f, 0.92f, 0.94f, 0.9f));
            rightReserve += s + gap + f1 + gap + f2;
        }
        else c.FillRoundRect(x, y, w, h, 6, t.PanelFill);
        float box = bigBox ? h - 16 : 42, bx = x + 10, byy = bigBox ? y + 8 : y + 10;
        Chrome.PosBox(c, bx, byy, box, box, position.ToString(CultureInfo.InvariantCulture), t.Numbers with { Size = bigBox ? 40 : 24 });
        float tx = bx + box + 12;
        Chrome.Tick(c, tx, y + 14, bigBox ? 34 : 32, tick ?? t.AccentBar);
        float nx = tx + 14, maxName = w - (nx - x) - 56 - rightReserve;
        float nw = Chrome.TwoWeightName(c, fullName, t.Text with { Size = 26 }, nx, y + 8, 40, maxName, t.TextColor);
        if (number.Length > 0)
            c.Text(number, t.Numbers with { Size = tick is null ? 26 : 28, Italic = true, Weight = tick is null ? t.Numbers.Weight : 700 }, nx + nw + 14, y + 8, 70, 40, tick ?? t.LabelColor);
        if (team.Length > 0) c.Text(team, BroadcastUi.Fit(c, team, t.Label with { Size = 20 }, w - (nx - x) - 20 - rightReserve), nx, y + 50, w - (nx - x) - 16 - rightReserve, 32, t.LabelColor);
        return nx + nw;
    }
    const float X0 = 4, Y0 = 4, LeftW = 230, WinLeftW = 280, HeadH = 26, RowH = 30;

    /// <summary>Legenda do piloto; <paramref name="cfg"/> traz o formato do nome e as colunas "team"/"tyre" (equipe e fornecedor de pneus).</summary>
    public static void DrawDriver(ThemeCanvas c, CarSnapshot car, IReadOnlyList<CarSnapshot> field, WidgetSettings cfg)
    {
        var t = c.Theme;
        string name = cfg.Name(car, BroadcastUi.ShortName(car, field)), team = cfg.ColumnVisible("team") ? BroadcastUi.Team(car) : "";
        if (!cfg.ColumnVisible("tyre")) car = car with { TyreSupplier = "" };
        if (t.Style == ThemeStyle.Broadcast2000s)
        {
            Chrome.HeaderCell(c, X0, Y0, LeftW, HeadH, "DRIVER", t.Label);
            Chrome.WhiteCell(c, X0, Y0 + HeadH, LeftW, RowH, name, BroadcastUi.Fit(c, name, t.Text, LeftW - 16));
            Chrome.Box(c, X0, Y0 + HeadH + RowH, LeftW, RowH, team, BroadcastUi.Fit(c, team, t.Text, LeftW - 16), Chrome.CellKind.Navy);
            float x = X0 + LeftW;
            var posKind = car.Position == 1 ? Chrome.CellKind.Red : Chrome.CellKind.Navy;
            if (!Chrome.TyreBox(c, x, Y0 + HeadH, 40, RowH * 2, car.TyreSupplier, t.Text with { Size = 22 }))
                Chrome.Box(c, x, Y0 + HeadH, 40, RowH * 2, "", t.Text, Chrome.CellKind.Navy);
            Chrome.Box(c, x + 40, Y0 + HeadH, 56, RowH * 2, car.Position.ToString(CultureInfo.InvariantCulture), t.Numbers with { Size = 38 }, posKind, HAlign.Center, 0);
            return;
        }
        if (t.Style == ThemeStyle.Broadcast98) { DriverBand(c, car, field, name, team); return; }
        // 2018: placa preta com caixa de posição, tique, "Nome SOBRENOME", número em itálico e equipe; fornecedor de pneus no canto.
        // Sem formato de nome escolhido, o nome completo (a TV mostra nome + sobrenome); com formato, o do perfil.
        string full = cfg.Fmt.Name is null && cfg.Fmt.CarNumber != true ? car.Name : name;
        Plate18(c, 0, 0, Driver18Width, Height, car.Position, full, CarNumber(car), team, bigBox: false);
        if (car.TyreSupplier.Length > 0) c.Text(car.TyreSupplier, t.Label with { Weight = 700 }, Driver18Width - 50, 50, 36, 32, t.ValueColor, HAlign.Center);
    }

    const float BandTop = 26, TagH = 24;

    /// <summary>Número do carro da legenda 1998–2001: o AMS2 não expõe o número real, então usa o índice do carro + 1.</summary>
    public static string CarNumber(CarSnapshot car) => (car.Index + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>Selo ciano de canto acima da faixa (no lugar do patrocinador da transmissão): o nome do widget.</summary>
    static void CyanTag(ThemeCanvas c, float right, string text, float w = 150)
    {
        c.FillRect(right - w, BandTop - TagH, w, TagH, new Vortice.Win32.Numerics.Color4(21 / 255f, 150 / 255f, 176 / 255f, 0.97f));
        c.Text(text, c.Theme.Label with { Size = 20 }, right - w, BandTop - TagH - 1, w, TagH, new Vortice.Win32.Numerics.Color4(1, 1, 1, 1), HAlign.Center, c.Theme.TextShadow);
    }

    /// <summary>Linhas [bolha ciana com o número][NOME] e [emblema do pneu][EQUIPE em ciano] sobre a faixa translúcida.</summary>
    static void NameLines(ThemeCanvas c, CarSnapshot car, string name, string team, float x, float maxW)
    {
        var t = c.Theme;
        float y1 = BandTop + 8, y2 = BandTop + 38;
        Chrome.Bubble(c, x, y1, 50, 28, CarNumber(car), t.Numbers with { Size = 24, Tracking = 1f });
        string nm = name.ToUpperInvariant(), tm = team.ToUpperInvariant();
        c.Text(nm, BroadcastUi.Fit(c, nm, t.Text, maxW), x + 62, y1 - 1, maxW + 8, 30, t.TextColor, shadow: t.TextShadow);
        float tx = x + 62;
        if (Chrome.TyreEmblem(c, x + 25, y2 + 14, 12, car.TyreSupplier, t.Text with { Size = 17 })) { }
        c.Text(tm, BroadcastUi.Fit(c, tm, t.Label, maxW), tx, y2, maxW + 8, 28, t.LabelColor, shadow: t.TextShadow);
    }

    static void DriverBand(ThemeCanvas c, CarSnapshot car, IReadOnlyList<CarSnapshot> field, string name, string team)
    {
        float w = DriverWidth, h = Height;
        c.Panel(0, BandTop, w, h - BandTop);
        CyanTag(c, w - 4, "DRIVER");
        NameLines(c, car, name, team, 16, w - 16 - 62 - 14);
    }

    /// <summary>Legenda do vencedor; colunas "team" (equipe) e "stats" (tempo, distância e média, na unidade de velocidade do perfil).</summary>
    public static void DrawWinner(ThemeCanvas c, WinnerInfo win, IReadOnlyList<CarSnapshot> field, WidgetSettings cfg)
    {
        var t = c.Theme;
        var car = win.Car;
        string name = cfg.Name(car, BroadcastUi.ShortName(car, field)), team = cfg.ColumnVisible("team") ? BroadcastUi.Team(car) : "";
        var su = cfg.Fmt.SpeedOrDefault;
        bool stats = cfg.ColumnVisible("stats");
        string time = stats ? BroadcastUi.RaceTime(win.TotalSeconds) : "";
        string dist = stats ? DisplayFormat.Distance(win.DistanceKm, su).ToString("0.000", CultureInfo.InvariantCulture) + " " + DisplayFormat.DistanceLabel(su) : "";
        string avg = stats ? DisplayFormat.SpeedFromKph(win.AvgKmh, su).ToString("0.000", CultureInfo.InvariantCulture) + " " + (su == SpeedUnit.Mph ? "mph" : "Km/h") : "";
        if (t.Style == ThemeStyle.Broadcast2000s)
        {
            Chrome.Box(c, X0, Y0, WinLeftW, HeadH, "Winner", t.Text, Chrome.CellKind.Red);
            Chrome.Checkered(c, X0 + WinLeftW - 130, Y0, 130, HeadH);
            Chrome.WhiteCell(c, X0, Y0 + HeadH, WinLeftW, RowH, name, BroadcastUi.Fit(c, name, t.Text, WinLeftW - 16));
            Chrome.Box(c, X0, Y0 + HeadH + RowH, WinLeftW, RowH, team, BroadcastUi.Fit(c, team, t.Text, WinLeftW - 66), Chrome.CellKind.Navy);
            Chrome.TyreBox(c, X0 + WinLeftW - 46, Y0 + HeadH + RowH + 3, 38, RowH - 6, car.TyreSupplier, t.Text with { Size = 22 });
            float bx = X0 + WinLeftW + 6, bw = 160;
            float ch = (HeadH + 2 * RowH) / 3f;
            var vf = t.Text with { Size = 21 };
            if (!stats) return;
            Chrome.BlackCell(c, bx, Y0, bw, ch - 1, time, vf, HAlign.Right);
            Chrome.BlackCell(c, bx, Y0 + ch, bw, ch - 1, dist, vf, HAlign.Right);
            Chrome.BlackCell(c, bx, Y0 + 2 * ch, bw, ch - 1, avg, vf, HAlign.Right);
            return;
        }
        if (t.Style == ThemeStyle.Broadcast98)
        {
            float bw = Winner98Width, bh = Height;
            c.Panel(0, BandTop, bw, bh - BandTop);
            CyanTag(c, bw - 4, "WINNER");
            NameLines(c, car, name, team, 16, 190);
            float vr = bw - 16;
            c.Text(time, BroadcastUi.Fit(c, time, t.Numbers with { Size = 26 }, 150), vr - 160, BandTop + 2, 160, 28, t.ValueColor, HAlign.Right, t.ValueShadow);
            c.Text(dist, t.Label with { Size = 19 }, vr - 160, BandTop + 28, 160, 20, t.ValueColor, HAlign.Right, t.TextShadow);
            c.Text(avg, BroadcastUi.Fit(c, avg, t.Label with { Size = 19 }, 160), vr - 160, BandTop + 47, 160, 20, t.ValueColor, HAlign.Right, t.TextShadow);
            return;
        }
        // 2018: banner / pódio próprios (Winner18, em WinnerWidget.cs).
        Winner18.DrawBanner(c, 0, 0, win, field, cfg);
    }
}
