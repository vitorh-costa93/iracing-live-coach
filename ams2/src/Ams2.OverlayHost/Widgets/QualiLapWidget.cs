using System.Globalization;
using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Placa da volta em andamento do jogador na classificação (<see cref="QualiLapState"/>, QualiLapTracker), por tema:
/// <para>2018 (ref. quali-2018-lap-panel.jpg / quali-2018-sector-panel.jpg): linha preta [posição][tique da classe][SOBRENOME][número em itálico]
/// [composto], corpo com o tempo corrente grande (décimos) e o comparativo à direita (líder ou melhor pessoal: nome + tempo), barra S1 S2 S3
/// (verde = melhor pessoal, roxo = melhor geral, amarelo = mais lento, cinza = ainda não). Na parcial (S1/S2) o tempo congela por
/// <see cref="SplitHold"/> s com a diferença colorida no lugar do tempo do comparativo; ao cruzar a linha, por showFor s, "1:18.917 +0.685 [7]".
/// Painel de setor à direita ("SECTOR 2 / SOBRENOME / 35.643", faixa roxa/verde) por <see cref="SectorPanelHold"/> s ao fechar S1/S2.</para>
/// <para>2004 (ref. quali-2004-lap-bar.jpg): pilha alinhada à direita — nome "F Alonso" em célula branca, tempo em célula preta, faixa fina
/// dos setores e, com comparação (parcial ou resultado), caixa vermelha com a posição + célula laranja com "+0.471".</para>
/// <para>1998 (ref. quali-1998-sheet.jpg / quali-1998-finish-comparison.jpg): só texto com sombra — NOME em caixa alta e o tempo corrente
/// amarelo; no resultado, à esquerda tempo da volta (+ velocidade, opcional) e à direita o comparativo com a diferença e "FINISH LINE".</para>
/// Visibilidade: sem "always" aparece só em volta lançada (tempo corrente conhecido, fora do box e da volta de saída) e no resultado.
/// Janela de tamanho fixo (só o painel de setor do 2018 muda a largura).
/// </summary>
public sealed class QualiLapWidget : IWidget
{
    public string Id => "qualilap";
    WidgetSettings _cfg = new() { Id = "qualilap" };
    ThemeStyle _style = ThemeStyle.Broadcast98;

    public void UseTheme(Theme.Theme theme) { _style = theme.Style; }
    public void Configure(WidgetSettings s) { _cfg = s; }

    public (float Width, float Height) DesignSize => _style switch
    {
        ThemeStyle.Modern2018 => (ShowSectorPanel ? PlateW + PanelGap + PanelW : PlateW, PlateH),
        ThemeStyle.Broadcast2000s => (W04 + 2 * X04, H04),
        _ => (W98, H98),
    };

    /// <summary>Segundos com a parcial congelada (tempo do setor + diferença) e com o painel de setor do 2018.</summary>
    public const double SplitHold = 3, SectorPanelHold = 4;

    // ---- Opções (WidgetCatalog.OptionsFor(tema, "qualilap")); padrão não gravado ----
    bool Flag(string id, bool def) => _cfg.Option(id) is { } v ? !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) : def;
    bool ComparePersonal => string.Equals(_cfg.OptionOr("compareTo", "leader"), "personal", StringComparison.OrdinalIgnoreCase);
    bool ShowSectors => _style != ThemeStyle.Broadcast98 && Flag("showSectors", true);
    bool ShowSectorPanel => _style == ThemeStyle.Modern2018 && Flag("showSectorPanel", true);
    bool ShowSpeed => _style == ThemeStyle.Broadcast98 && Flag("showSpeed", true);
    bool Always => Flag("always", false);
    double ShowFor => double.TryParse(_cfg.OptionOr("showFor", "6"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? Math.Clamp(v, 3, 15) : 6;

    static Color4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);
    static readonly Color4 Green = Rgb(37, 194, 58), Yellow = Rgb(250, 205, 0), Purple = Rgb(160, 32, 200), PurpleInk = Rgb(200, 120, 255), White = Rgb(255, 255, 255);
    static readonly Color4 SectorOff = Rgb(96, 98, 106, 0.92f);

    // ---- Estado da placa (público para teste) ----

    public enum Phase { Hidden, Idle, Running, Result }

    /// <summary>
    /// Fase da placa: Result enquanto o resultado tem menos de <paramref name="showFor"/> s; Running em volta lançada (tempo corrente
    /// conhecido, fora do box e da volta de saída); senão Idle com <paramref name="always"/> ou Hidden.
    /// </summary>
    public static Phase PhaseOf(QualiLapState? q, double now, double showFor, bool always)
    {
        if (q is null || q.CarIndex < 0) return always ? Phase.Idle : Phase.Hidden;
        if (q.LastResult is { } r && now - r.At >= 0 && now - r.At < showFor) return Phase.Result;
        if (IsRunning(q)) return Phase.Running;
        return always ? Phase.Idle : Phase.Hidden;
    }

    static bool IsRunning(QualiLapState q) => q.Elapsed is not null && !q.InPit && !q.OutLap;

    /// <summary>Diferença na parcial: a do tracker (parciais da melhor volta observada) ou, sem ela, a soma dos melhores setores
    /// (pessoais, ou os melhores gerais quando compara com o líder).</summary>
    public static double? SplitDelta(QualiLapState q, QualiSplit sp, bool personal)
    {
        if ((personal ? sp.DeltaPersonal : sp.DeltaLeader) is { } d) return d;
        var refs = personal ? q.PersonalBestSectors : q.OverallBestSectors;
        double sum = 0;
        for (int k = 0; k < sp.Sector && k < refs.Count; k++)
        {
            if (refs[k] is not { } v) return null;
            sum += v;
        }
        return sp.Sector > 0 ? sp.Elapsed - sum : null;
    }

    /// <summary>Tempo com milésimos: "35.643" abaixo de 1 min, senão "1:18.917" (formato do perfil).</summary>
    string Exact(double t) => t < 60 && _cfg.Fmt.LapTime is null ? t.ToString("0.000", CultureInfo.InvariantCulture) : _cfg.Fmt.FormatLapTime(t);
    string Delta(double d) => _cfg.Fmt.FormatGap(d);
    static string Running(double? t) => t is { } v ? QualiTowerWidget.Tenths(v) : "--.-";

    /// <summary>Nome "F Alonso" (inicial + sobrenome) do 2004; um nome só fica como está.</summary>
    public static string InitialName(string full)
    {
        var p = full.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return p.Length switch { 0 => "", 1 => p[0], _ => p[0][0] + " " + string.Join(' ', p[1..]) };
    }

    /// <summary>Tudo o que os três temas desenham, resolvido uma vez por quadro.</summary>
    sealed record View(
        Phase Phase, CarSnapshot Car, IReadOnlyList<CarSnapshot> Field, QualiLapState Q, int Position,
        string RefName, double? RefTime, QualiSplit? Split, double? SplitDelta, QualiLapResult? Result, double? ResultDelta,
        IReadOnlyList<QualiSector?> Sectors, float Alpha);

    View? Resolve(OverlayModel m)
    {
        if (!m.Connected || m.Session is not { PlayerCar: { } car } s) return null;
        var q = m.QualiLap ?? QualiLapState.Empty;
        var phase = PhaseOf(m.QualiLap, m.Now, ShowFor, Always);
        if (phase == Phase.Hidden) return null;
        bool personal = ComparePersonal;
        int pos = m.Quali?.Player?.Rank ?? car.Position;
        string refName; double? refTime;
        if (personal) { refName = "PERSONAL BEST"; refTime = q.PersonalBestLap; }
        else
        {
            var lead = s.Cars.FirstOrDefault(c => c.Index == q.LeaderIndex);
            refName = lead is null ? "LEADER" : _cfg.Name(lead, BroadcastUi.ShortName(lead, s.Cars)).ToUpperInvariant();
            refTime = q.LeaderBestLap;
        }
        QualiSplit? split = phase == Phase.Running && q.LastSplit is { } sp && m.Now - sp.At >= 0 && m.Now - sp.At < SplitHold ? sp : null;
        var result = phase == Phase.Result ? q.LastResult : null;
        double? resDelta = result is null ? null : personal ? result.DeltaPersonal : result.GapToFirst;
        if (result is not null) pos = result.Position;
        float alpha = 1f;
        // Sem volta seguinte (entrou no box) e sem "always": o resultado entra e sai com fade.
        if (result is not null && !Always && !IsRunning(q)) alpha = BroadcastUi.Fade(m.Now - result.At, ShowFor);
        return new View(phase, car, s.Cars, q, pos, refName, refTime, split, split is null ? null : SplitDelta(q, split, personal), result, resDelta,
            result?.Sectors ?? q.Sectors, alpha);
    }

    /// <summary>Posição provisória na parcial (2004): onde ficaria uma volta = tempo do comparativo + diferença.</summary>
    static int ProjectedPosition(OverlayModel m, View v)
    {
        if (v.RefTime is not { } rt || v.SplitDelta is not { } d || m.Quali is null) return v.Position;
        double est = rt + d;
        return 1 + m.Quali.Rows.Count(r => !r.IsPlayer && r.BestLap is { } b && b < est);
    }

    // ---- Desenho ----

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        var v = Resolve(m);
        if (v is null) return;
        BroadcastUi.WithAlpha(c, v.Alpha, () =>
        {
            switch (_style)
            {
                case ThemeStyle.Modern2018: Draw18(c, m, v); break;
                case ThemeStyle.Broadcast2000s: Draw04(c, m, v); break;
                default: Draw98(c, m, v); break;
            }
        });
    }

    static Color4 MarkColor(QualiSector? s) => s?.Mark switch
    {
        SectorMark.OverallBest => Purple,
        SectorMark.PersonalBest => Green,
        SectorMark.Slower => Yellow,
        _ => SectorOff,
    };

    /// <summary>Texto no lugar do tempo fora da volta lançada (só com "always").</summary>
    static string? IdleText(QualiLapState q) => q.InPit ? "IN PIT" : q.OutLap ? "OUT LAP" : null;

    // ---- 2018 ----
    const float PlateW = 400, RowH = 50, BodyH = 84, BarH = 18, PlateH = RowH + BodyH + BarH;
    const float PanelGap = 14, PanelW = 250, PanelRule = 4, PanelRowH = 38, PanelTimeH = 44, PanelH = PanelRule + 2 * PanelRowH + PanelTimeH;
    static readonly Color4 Body18 = Rgb(28, 30, 37, 0.9f);

    void Draw18(ThemeCanvas c, OverlayModel m, View v)
    {
        var t = c.Theme;
        var car = v.Car;
        // Linha preta: posição, tique da classe, SOBRENOME, número em itálico e composto do jogador.
        c.FillRect(0, 0, PlateW, RowH, t.PanelFill);
        Chrome.PosBox(c, 8, 7, 36, 36, v.Position.ToString(CultureInfo.InvariantCulture), t.Numbers with { Size = 22 });
        var tick = CaptionPlate.ClassColor(car, v.Field);
        Chrome.Tick(c, 54, 10, 30, tick, 5);
        string last = _cfg.Name(car, BroadcastUi.ShortName(car, v.Field)).ToUpperInvariant();
        string num = CaptionPlate.CarNumber(car);
        var nf = BroadcastUi.Fit(c, last, t.Text with { Size = 26 }, PlateW - 68 - 100);
        float nw = c.Measure(last, nf);
        c.Text(last, nf, 68, 1, nw + 8, RowH, t.TextColor);
        c.Text(num, t.Numbers with { Size = 26, Italic = true, Weight = 700 }, 68 + nw + 12, 1, 60, RowH, tick);
        string letter = QualiTowerWidget.CompoundLetter(m.Session?.Player?.Wheels is { Count: > 0 } w ? w[0].Compound : null);
        if (letter != "-")
        {
            float cx = PlateW - 26, cy = RowH / 2, rad = 15;
            c.FillEllipse(cx, cy, rad, rad, Rgb(8, 8, 10));
            c.StrokeEllipse(cx, cy, rad - 1.5f, rad - 1.5f, CompoundColor(letter), 3f);
            c.Text(letter, t.Label with { Size = 17, Weight = 700 }, cx - rad, cy - rad - 1, 2 * rad, 2 * rad, White, HAlign.Center);
        }

        // Corpo: tempo grande à esquerda, comparativo à direita.
        float by = RowH, lw = MathF.Round(PlateW * 0.52f), rx = lw + 6, rw = PlateW - rx - 10;
        c.FillRect(0, by, PlateW, BodyH, Body18);
        c.FillRect(lw - 0.75f, by + 14, 1.5f, BodyH - 28, Rgb(255, 255, 255, 0.35f));
        var big = t.Numbers with { Size = 46 };
        string main; Color4 mainInk = t.ValueColor;
        if (v.Result is { } r)
        {
            main = Exact(r.LapTime);
            mainInk = r.Invalid ? t.LabelColor : r.Improved ? (r.Position == 1 ? PurpleInk : Green) : t.ValueColor;
        }
        else if (v.Split is { } sp) main = Exact(sp.Elapsed);
        else main = (v.Phase == Phase.Idle ? IdleText(v.Q) : null) ?? Running(v.Q.Elapsed);
        var mf = main.Any(char.IsLetter) ? t.Text with { Size = 30 } : big;
        c.Text(main, BroadcastUi.Fit(c, main, mf, lw - 24), 6, by, lw - 12, BodyH, mainInk, HAlign.Center);

        var lf = t.Label with { Size = 18, Tracking = 0.5f };
        var vf = t.Numbers with { Size = 30 };
        if (v.Result is { } res)
        {
            // "1:18.917 +0.685 [7]": diferença (ou INVALID) e a posição obtida em caixa branca.
            const float box = 44;
            float bx = PlateW - 12 - box, gw = bx - rx - 8;
            string lab = res.Invalid ? "INVALID" : v.RefName;
            c.Text(lab, BroadcastUi.Fit(c, lab, lf, gw), rx, by + 8, gw, 26, t.LabelColor, HAlign.Center);
            string g = res.Invalid ? "-" : v.ResultDelta is { } d ? Delta(d) : "-";
            c.Text(g, BroadcastUi.Fit(c, g, vf, gw), rx, by + 34, gw, 40, DeltaInk(v.ResultDelta, t), HAlign.Center);
            Chrome.PosBox(c, bx, by + (BodyH - box) / 2, box, box, res.Position.ToString(CultureInfo.InvariantCulture), t.Numbers with { Size = 26 });
        }
        else
        {
            c.Text(v.RefName, BroadcastUi.Fit(c, v.RefName, lf, rw), rx, by + 8, rw, 26, t.LabelColor, HAlign.Center);
            string val; Color4 ink = t.ValueColor;
            if (v.Split is not null && v.SplitDelta is { } d) { val = Delta(d); ink = DeltaInk(d, t); }
            else val = v.RefTime is { } rt ? Exact(rt) : "NO TIME";
            c.Text(val, BroadcastUi.Fit(c, val, vf, rw), rx, by + 34, rw, 40, ink, HAlign.Center);
        }

        // Barra S1 S2 S3.
        if (ShowSectors)
        {
            float sy = by + BodyH, gap = 2, sw = (PlateW - 2 * gap) / 3;
            var sf = t.Label with { Size = 13, Weight = 700 };
            for (int k = 0; k < 3; k++)
            {
                var sec = k < v.Sectors.Count ? v.Sectors[k] : null;
                var col = MarkColor(sec);
                float sx = k * (sw + gap);
                c.FillRect(sx, sy, sw, BarH, col);
                c.Text("S" + (k + 1), sf, sx, sy - 1, sw, BarH, sec is null || sec.Mark == SectorMark.None ? Rgb(220, 222, 228) : Rgb(16, 16, 20), HAlign.Center);
            }
        }

        // Painel de setor à direita ao fechar S1/S2.
        if (ShowSectorPanel && v.Phase == Phase.Running && v.Q.LastSplit is { } ls && v.Q.Sectors.Count >= ls.Sector && v.Q.Sectors[ls.Sector - 1] is { } st)
        {
            float a = BroadcastUi.Fade(m.Now - ls.At, SectorPanelHold);
            BroadcastUi.WithAlpha(c, a, () => SectorPanel18(c, t, ls.Sector, last, st));
        }
    }

    void SectorPanel18(ThemeCanvas c, Theme.Theme t, int sector, string name, QualiSector st)
    {
        float x = PlateW + PanelGap, y = PlateH - PanelH;
        c.FillRect(x, y, PanelW, PanelRule, t.AccentBar);
        y += PanelRule;
        c.FillRect(x, y, PanelW, 2 * PanelRowH, t.PanelFill);
        c.Text("SECTOR " + sector.ToString(CultureInfo.InvariantCulture), t.Title with { Weight = 400, Size = 24, Tracking = 1f }, x, y + 1, PanelW, PanelRowH, t.TitleColor, HAlign.Center);
        c.Text(name, BroadcastUi.Fit(c, name, t.Text with { Weight = 400, Size = 24, Tracking = 0.5f }, PanelW - 20), x, y + PanelRowH, PanelW, PanelRowH, t.TextColor, HAlign.Center);
        y += 2 * PanelRowH;
        var fill = st.Mark switch { SectorMark.OverallBest => Purple, SectorMark.PersonalBest => Rgb(30, 150, 48), _ => t.SubPanelFill };
        c.FillRect(x, y, PanelW, PanelTimeH, fill);
        string tm = Exact(st.Time);
        c.Text(tm, t.Numbers with { Size = 30, Weight = 700 }, x, y, PanelW, PanelTimeH, White, HAlign.Center);
    }

    static Color4 DeltaInk(double? d, Theme.Theme t) => d is not { } v ? t.ValueColor : v < 0 ? Green : Yellow;

    static Color4 CompoundColor(string letter) => letter switch
    {
        "S" => Rgb(225, 6, 0),
        "M" => Rgb(250, 205, 0),
        "H" => Rgb(240, 240, 240),
        "I" => Rgb(40, 180, 60),
        "W" => Rgb(30, 110, 230),
        _ => Rgb(130, 132, 140),
    };

    // ---- 2004 ----
    const float X04 = 4, W04 = 270, Cell04 = 36, Strip04 = 6, Pos04 = 40, H04 = 4 + 3 * Cell04 + 10 + 4;

    void Draw04(ThemeCanvas c, OverlayModel m, View v)
    {
        var t = c.Theme;
        float y = 4;
        string name = _cfg.Name(v.Car, InitialName(v.Car.Name));
        Chrome.WhiteCell(c, X04, y, W04, Cell04, name, BroadcastUi.Fit(c, name, t.Text, W04 - 16), HAlign.Right);
        y += Cell04;
        string val = v.Result is { } r ? Exact(r.LapTime) : v.Split is { } sp ? Exact(sp.Elapsed)
            : (v.Phase == Phase.Idle ? IdleText(v.Q) : null) ?? Running(v.Q.Elapsed);
        Chrome.BlackCell(c, X04, y, W04, Cell04, val, BroadcastUi.Fit(c, val, t.Numbers, W04 - 12), HAlign.Right);
        y += Cell04;
        if (ShowSectors)
        {
            float gap = 2, sw = (W04 - 2 * gap) / 3;
            for (int k = 0; k < 3; k++)
                c.FillRect(X04 + k * (sw + gap), y + 2, sw, Strip04, MarkColor(k < v.Sectors.Count ? v.Sectors[k] : null));
        }
        y += 10;
        // Comparação (parcial ou resultado): [posição vermelha][+0.471 em laranja].
        int? pos = null; string? diff = null;
        if (v.Result is { } res) { pos = res.Position; diff = res.Invalid ? "INVALID" : v.ResultDelta is { } d ? Delta(d) : null; }
        else if (v.Split is not null && v.SplitDelta is { } sd) { pos = ProjectedPosition(m, v); diff = Delta(sd); }
        if (pos is not { } p || diff is null) return;
        Chrome.Box(c, X04, y, Pos04, Cell04, p.ToString(CultureInfo.InvariantCulture), t.Numbers, Chrome.CellKind.Red, HAlign.Center, 0);
        Chrome.BlackCell(c, X04 + Pos04, y, W04 - Pos04, Cell04, diff, BroadcastUi.Fit(c, diff, t.Numbers, W04 - Pos04 - 12), HAlign.Right, kind: Chrome.CellKind.Orange);
    }

    // ---- 1998 ----
    const float W98 = 640, H98 = 124, Col98 = 300, Gap98 = 16, L98 = 12, NameH98 = 34, TimeH98 = 48, LowH98 = 30;

    void Draw98(ThemeCanvas c, OverlayModel m, View v)
    {
        var t = c.Theme;
        float lx = L98, rx = L98 + Col98 + Gap98, y1 = 4, y2 = y1 + NameH98, y3 = y2 + TimeH98;
        string name = _cfg.Name(v.Car, BroadcastUi.ShortName(v.Car, v.Field)).ToUpperInvariant();
        c.Text(name, BroadcastUi.Fit(c, name, t.Text, Col98), lx, y1, Col98 + 8, NameH98, t.TextColor, shadow: t.TextShadow);
        var big = t.Numbers with { Size = 42 };
        string main = v.Result is { } r ? Exact(r.LapTime) : v.Split is { } sp ? Exact(sp.Elapsed) : Running(v.Q.Elapsed);
        string? idle = v.Phase == Phase.Idle ? IdleText(v.Q) : null;
        if (idle is not null) c.Text(idle, t.Text, lx, y2, Col98, TimeH98, t.ValueColor, shadow: t.TextShadow);
        else c.Text(main, BroadcastUi.Fit(c, main, big, Col98), lx, y2, Col98 + 8, TimeH98, t.ValueColor, shadow: t.ValueShadow);

        if (v.Result is { } res)
        {
            if (ShowSpeed && m.Session?.PlayerCar is { } pc)
            {
                var su = _cfg.Fmt.SpeedOrDefault;
                double spd = DisplayFormat.SpeedFromKph(pc.SpeedMps * DisplayFormat.MpsToKph, su);
                Chrome.ValueUnit(c, spd.ToString("0.0", CultureInfo.InvariantCulture), su == SpeedUnit.Mph ? "mph" : "Km/h", lx, y3, LowH98, t.ValueColor, false);
            }
            Right98(c, t, rx, v.RefName, res.Invalid ? "INVALID" : v.ResultDelta is { } d ? Delta(d) : "-", "FINISH LINE");
        }
        else if (v.Split is { } s2 && v.SplitDelta is { } sd)
            Right98(c, t, rx, v.RefName, Delta(sd), "SECTOR " + s2.Sector.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Coluna da direita do 1998: nome do comparativo, diferença amarela grande e o rótulo ("FINISH LINE"), alinhados à direita.</summary>
    static void Right98(ThemeCanvas c, Theme.Theme t, float x, string refName, string value, string label)
    {
        float y1 = 4, y2 = y1 + NameH98, y3 = y2 + TimeH98;
        c.Text(refName, BroadcastUi.Fit(c, refName, t.Text, Col98), x, y1, Col98, NameH98, t.TextColor, HAlign.Right, t.TextShadow);
        bool letters = value.Any(char.IsLetter);
        var vf = letters ? t.Text : t.Numbers with { Size = 42 };
        c.Text(value, BroadcastUi.Fit(c, value, vf, Col98), x, y2, Col98, TimeH98, t.ValueColor, HAlign.Right, letters ? t.TextShadow : t.ValueShadow);
        c.Text(label, t.Text with { Size = 26 }, x, y3, Col98, LowH98, t.ValueColor, HAlign.Right, t.TextShadow);
    }
}
