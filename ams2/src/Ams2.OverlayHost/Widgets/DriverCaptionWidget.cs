using System.Globalization;
using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Legenda de piloto da transmissao: cabecalho branco (nome do widget em teal), nome em celula branca, equipe em celula
/// azul-ardosia; a direita caixinha do fornecedor de pneus e caixa de posicao (vermelha no lider).
/// Mostra o piloto do jogador por alguns segundos ao conectar, ao mudar de posicao e ao cruzar a linha; a coluna "always" a mantem fixa.
/// </summary>
public sealed class DriverCaptionWidget : IWidget
{
    public string Id => "drivercaption";
    public (float Width, float Height) DesignSize => (_b18 ? CaptionPlate.Driver18Width : CaptionPlate.DriverWidth, CaptionPlate.Height);
    bool _b18;
    public void UseTheme(Theme.Theme theme) => _b18 = theme.Style == ThemeStyle.Modern2018;
    WidgetSettings _cfg = new() { Id = "drivercaption" };
    public void Configure(WidgetSettings s) => _cfg = s;

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        if (!m.Connected || m.Session is not { } s || s.PlayerCar is not { } car) return;
        var b = BroadcastUi.State(m);
        float alpha = 1f;
        if (!_cfg.ColumnVisible("always"))
        {
            double last = Math.Max(b.SessionSeenT, Math.Max(b.PlayerPositionChangedT, b.PlayerLapChangedT));
            alpha = BroadcastUi.Fade(m.Now - last, BroadcastUi.CaptionHold);
            if (b.Winner is { } w && m.Now - w.FinishedT < BroadcastUi.WinnerHold + 0.5) alpha = 0f; // a legenda do vencedor ocupa o lugar
        }
        BroadcastUi.WithAlpha(c, alpha, () => CaptionPlate.DrawDriver(c, car, s.Cars, _cfg));
    }
}

/// <summary>Desenho das legendas (piloto e vencedor), por tema.</summary>
public static class CaptionPlate
{
    public const float Height = 94, DriverWidth = 334, WinnerWidth = 448, Winner98Width = 450, Driver18Width = 480, Winner18Width = 660;

    /// <summary>
    /// Placa 2018 (ref. f1-2018-driver-caption.jpg / f1-2018-result-caption.jpg): placa preta translúcida, caixa de posição branca
    /// (grande no resultado), tique vertical, "Nome SOBRENOME" em dois pesos, número do carro em itálico e a equipe embaixo.
    /// O AMS2 não informa cor/logo da equipe: tique no vermelho do tema e número em cinza claro, sem logos. Devolve a borda direita do texto.
    /// </summary>
    public static float Plate18(ThemeCanvas c, float x, float y, float w, float h, int position, string fullName, string number, string team, bool bigBox, float rightReserve = 0)
    {
        var t = c.Theme;
        c.FillRoundRect(x, y, w, h, 6, t.PanelFill);
        float box = bigBox ? h - 16 : 42, bx = x + 10, byy = bigBox ? y + 8 : y + 10;
        Chrome.PosBox(c, bx, byy, box, box, position.ToString(CultureInfo.InvariantCulture), t.Numbers with { Size = bigBox ? 40 : 24 });
        float tx = bx + box + 12;
        Chrome.Tick(c, tx, y + 14, bigBox ? 34 : 32, t.AccentBar);
        float nx = tx + 14, maxName = w - (nx - x) - 56 - rightReserve;
        float nw = Chrome.TwoWeightName(c, fullName, t.Text with { Size = 26 }, nx, y + 8, 40, maxName, t.TextColor);
        if (number.Length > 0) c.Text(number, t.Numbers with { Size = 26, Italic = true }, nx + nw + 14, y + 8, 70, 40, t.LabelColor);
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
        // 2018: legenda de resultado (caixa de posição branca grande, nome em dois pesos, número em itálico, equipe) e, à direita,
        // tempo total, distância e média num bloco separado por filete vertical.
        float w = Winner18Width, h = Height;
        string full = cfg.Fmt.Name is null && cfg.Fmt.CarNumber != true ? car.Name : name;
        float statsW = stats ? 150 : 0;
        Plate18(c, 0, 0, w, h, 1, full, CarNumber(car), team, bigBox: true, rightReserve: stats ? statsW + 24 : 0);
        if (!stats) return;
        float sx = w - statsW - 12;
        c.FillRect(sx - 12, 14, 1.5f, h - 28, t.Divider);
        c.Text(time, BroadcastUi.Fit(c, time, t.Numbers with { Size = 22 }, statsW), sx, 8, statsW, 30, t.ValueColor, HAlign.Right);
        c.Text(dist, BroadcastUi.Fit(c, dist, t.Label, statsW), sx, 36, statsW, 26, t.LabelColor, HAlign.Right);
        c.Text(avg, BroadcastUi.Fit(c, avg, t.Label, statsW), sx, 60, statsW, 26, t.LabelColor, HAlign.Right);
    }
}

/// <summary>Legenda do vencedor: cabecalho vermelho "Winner" + bandeira quadriculada, nome, equipe e coluna preta com tempo, distancia e media. Aparece ao fim da corrida por ~10 s.</summary>
public sealed class WinnerWidget : IWidget
{
    public string Id => "winner";
    public (float Width, float Height) DesignSize => (_style == ThemeStyle.Broadcast98 ? CaptionPlate.Winner98Width : _style == ThemeStyle.Broadcast2000s ? CaptionPlate.WinnerWidth : CaptionPlate.Winner18Width, CaptionPlate.Height);
    ThemeStyle _style;
    public void UseTheme(Theme.Theme theme) => _style = theme.Style;
    WidgetSettings _cfg = new() { Id = "winner" };
    public void Configure(WidgetSettings s) => _cfg = s;

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        if (!m.Connected || m.Session is not { } s) return;
        var b = BroadcastUi.State(m);
        if (b.Winner is not { } w) return;
        float alpha = _cfg.ColumnVisible("always") ? 1f : BroadcastUi.Fade(m.Now - w.FinishedT, BroadcastUi.WinnerHold);
        BroadcastUi.WithAlpha(c, alpha, () => CaptionPlate.DrawWinner(c, w, s.Cars, _cfg));
    }
}
