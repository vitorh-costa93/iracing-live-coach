using System.Globalization;
using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Par de carros em disputa: o da frente (à esquerda na TV) e o de trás, com o intervalo entre eles (s).</summary>
public sealed record DuelPair(CarSnapshot Ahead, CarSnapshot Behind, double Gap);

/// <summary>Escolha do par do jogador com um vizinho de posição, pelo intervalo da tabela (<see cref="OverlayModel.Standings"/>).</summary>
public static class Duel
{
    /// <summary>
    /// Par do jogador com o vizinho escolhido. <paramref name="rival"/>: "ahead", "behind" ou "auto" (o vizinho mais próximo). Só vale com intervalo conhecido
    /// e <= <paramref name="maxGap"/> s, e na mesma volta; <paramref name="preview"/> (edição de layout) aceita qualquer vizinho.
    /// </summary>
    public static DuelPair? Find(OverlayModel m, string rival, double maxGap, bool preview)
    {
        var rows = m.Standings;
        int me = -1;
        for (int i = 0; i < rows.Count; i++) if (rows[i].IsPlayer) { me = i; break; }
        if (me < 0) return null;
        DuelPair? ahead = me > 0 ? Make(rows[me - 1], rows[me], maxGap, preview) : null;
        DuelPair? behind = me + 1 < rows.Count ? Make(rows[me], rows[me + 1], maxGap, preview) : null;
        return rival switch
        {
            "ahead" => ahead,
            "behind" => behind,
            _ => ahead is null ? behind : behind is null ? ahead : ahead.Gap <= behind.Gap ? ahead : behind,
        };
    }

    static DuelPair? Make(StandingRow front, StandingRow back, double maxGap, bool preview)
    {
        double? gap = Interval(front, back, front.Car.Position <= 1);
        if (gap is null) return preview ? new DuelPair(front.Car, back.Car, 0) : null;
        return preview || gap <= maxGap ? new DuelPair(front.Car, back.Car, gap.Value) : null;
    }

    /// <summary>Intervalo (s) entre dois carros consecutivos na mesma volta; null sem dado ou com voltas de diferença.</summary>
    static double? Interval(StandingRow front, StandingRow back, bool frontIsLeader)
    {
        if (front.LapsBehind != back.LapsBehind || back.GapToLeader is not { } b) return null;
        double f = front.GapToLeader ?? (frontIsLeader ? 0 : double.NaN);
        double d = b - f;
        return double.IsNaN(d) || d < 0 ? null : d;
    }
}

/// <summary>
/// "Battle" do gráfico de TV 2018 (ref. exemplo PEREZ x HAMILTON): faixa preta com posição, tique, SOBRENOME e número dos dois carros e o
/// velocímetro no meio; embaixo, para cada carro, o indicador DRS e as velocidades em km/h e mph. Aparece quando o jogador está a menos de
/// <c>gapSeconds</c> do vizinho escolhido. DRS: o AMS2 só informa o estado do carro do jogador (mBoostActive, mapeamento para o DRS ainda não
/// conferido em sessão real); o do rival fica apagado. Velocidade do rival = a do snapshot do jogo. Exclusivo do tema f1-2018.
/// Opções: rival (auto/ahead/behind), gapSeconds, showDrs, always.
/// </summary>
public sealed class BattleWidget : IWidget
{
    public string Id => "battle";
    public const float W = 760, H = 124, TopH = 48, Half = 358;
    public (float Width, float Height) DesignSize => (W, H);
    WidgetSettings _cfg = new() { Id = "battle" };
    readonly Broadcast18Motion _motion18 = new();
    DuelPair? _last;
    public void Configure(WidgetSettings s) => _cfg = s;

    string RivalMode => _cfg.OptionOr("rival", "auto").ToLowerInvariant();
    double MaxGap => double.TryParse(_cfg.OptionOr("gapSeconds", "1"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? Math.Clamp(v, 0.3, 3) : 1;
    bool ShowDrs => !string.Equals(_cfg.OptionOr("showDrs", "true"), "false", StringComparison.OrdinalIgnoreCase);
    bool Preview => string.Equals(_cfg.OptionOr("layoutPreview", "false"), "true", StringComparison.OrdinalIgnoreCase);
    public bool IgnoresDrivingGate => string.Equals(_cfg.OptionOr("always", "false"), "true", StringComparison.OrdinalIgnoreCase);

    static Color4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);
    static readonly Color4 Red = Rgb(230, 36, 43), UnitInk = Rgb(143, 176, 189), LowerFill = Rgb(22, 23, 29, 0.93f),
        DrsOn = Rgb(30, 185, 60), DrsOff = Rgb(58, 60, 68), DrsOffInk = Rgb(122, 126, 136);

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        if (!m.Connected || m.Session is not { } s) { _motion18.Reset(); _last = null; return; }
        var pair = Duel.Find(m, RivalMode, MaxGap, Preview);
        if (pair is not null) _last = pair;
        float reveal = _motion18.Presence(m.Now, pair is not null, hold: 0.6, session: BroadcastUi.State(m).SessionSeenT);
        if (reveal <= 0 || _last is not { } p) return;
        BroadcastUi.WithReveal18(c, reveal, W, H, () => DrawPlate(c, m, s, p));
    }

    void DrawPlate(ThemeCanvas c, OverlayModel m, SessionSnapshot s, DuelPair p)
    {
        var t = c.Theme;
        c.FillRect(0, 0, W, TopH, t.PanelFill);
        c.FillRect(Half, 0, W - 2 * Half, TopH, Rgb(40, 42, 50, 0.95f));
        LiveSpeedWidget.Gauge(c, W / 2, TopH / 2 - 1, 11, t.TitleColor);
        c.FillRect(0, TopH, W, H - TopH, LowerFill);
        c.FillRect(W / 2 - 0.75f, TopH + 14, 1.5f, H - TopH - 28, new Color4(1, 1, 1, 0.18f));
        Side(c, m, s, p.Ahead, 0, false);
        Side(c, m, s, p.Behind, W - Half, true);
    }

    void Side(ThemeCanvas c, OverlayModel m, SessionSnapshot s, CarSnapshot car, float x0, bool mirror)
    {
        var t = c.Theme;
        var tick = CaptionPlate.ClassColor(car, s.Cars);
        // Linha de cima: caixa de posição, tique, SOBRENOME em negrito e número em itálico (a metade direita começa no mesmo ponto relativo).
        Chrome.PosBox(c, x0 + 8, 6, 36, 36, car.Position.ToString(CultureInfo.InvariantCulture), t.Numbers with { Element = "position", Size = 22 });
        Chrome.Tick(c, x0 + 54, 10, 28, tick);
        string name = _cfg.Name(car, BroadcastUi.ShortName(car, s.Cars)).ToUpperInvariant();
        var nf = BroadcastUi.Fit(c, name, t.Text with { Weight = 700, Size = 27 }, Half - 62 - 70);
        c.Text(name, nf, x0 + 66, 0, Half - 62 - 70, TopH, t.TextColor);
        float nw = c.Measure(name, nf);
        c.Text(CaptionPlate.CarNumber(car), t.Numbers with { Size = 27, Italic = true, Weight = 700 }, x0 + 66 + nw + 10, 0, 64, TopH, tick);

        // Linha de baixo: [DRS] [MPH] [KM/H] no lado esquerdo e o espelho (KM/H, MPH, DRS) no direito.
        bool isPlayer = car.IsPlayer || car.Index == s.Player?.Index;
        double mps = isPlayer && s.Player is { } pl ? RenderPlayerTelemetry.Read(m.Inputs, pl).SpeedMps : car.SpeedMps;
        string kph = Speed(mps, SpeedUnit.Kph), mph = Speed(mps, SpeedUnit.Mph);
        float X(float left, float width) => mirror ? x0 + Half - left - width : x0 + left;
        float top = TopH;
        if (ShowDrs) Drs(c, X(16, 78), top + 36, 78, 36, isPlayer && s.Player is { BoostActive: true }, mirror);
        var big = t.Numbers with { Weight = 400, Size = 40, Tracking = 0f };
        var unit = t.Label with { Weight = 400, Size = 17, Tracking = 0.5f };
        float mphX = X(116, 84), sepX = mirror ? x0 + Half - 208 : x0 + 208, kphX = X(218, 124);
        c.Text("MPH", unit, mphX, top + 6, 84, 24, UnitInk, HAlign.Center);
        c.Text(mph, big, mphX, top + 26, 84, 46, Red, HAlign.Center);
        c.FillRect(sepX - 0.5f, top + 18, 1, H - top - 36, new Color4(1, 1, 1, 0.22f));
        c.Text("KM/H", unit, kphX, top + 6, 124, 24, UnitInk, HAlign.Center);
        c.Text(kph, big with { Size = 44 }, kphX, top + 24, 124, 50, Red, HAlign.Center);
    }

    static string Speed(double mps, SpeedUnit u)
        => Math.Max(0, Math.Round(DisplayFormat.Speed(mps, u))).ToString("0", CultureInfo.InvariantCulture);

    static void Drs(ThemeCanvas c, float x, float y, float w, float h, bool on, bool mirror)
    {
        c.FillSlant(x, y, w - 8, h, mirror ? -8 : 8, on ? DrsOn : DrsOff);
        c.Text("DRS", c.Theme.Label with { Weight = 700, Size = 21, Tracking = 0.5f }, x, y, w, h, on ? new Color4(1, 1, 1, 1) : DrsOffInk, HAlign.Center);
    }
}

/// <summary>
/// Legenda de posição do gráfico de TV 2018 (ref. "1 Lewis HAMILTON 44 Mercedes" + "2 Sergio PEREZ 11 Red Bull Racing"): duas placas lado a lado, cada uma com
/// caixa de posição, tique, "Nome SOBRENOME", número em itálico, equipe embaixo e a bandeira do país no canto (só quando o país é conhecido: jogador pela pintura,
/// IA pelo nome no catálogo de pinturas). Aparece por <c>showFor</c> s a cada mudança de posição do jogador. Exclusivo do f1-2018.
/// Opções: cars (rival = jogador e vizinho / top2 = líder e 2º), showFor, always.
/// </summary>
public sealed class PositionCaptionWidget : IWidget
{
    public string Id => "poscaption";
    public const float PlateW = CaptionPlate.Driver18Width, PlateH = CaptionPlate.Height, Gap = 40, W = PlateW * 2 + Gap;
    public (float Width, float Height) DesignSize => (W, PlateH);
    WidgetSettings _cfg = new() { Id = "poscaption" };
    readonly Broadcast18Motion _motion18 = new();
    DuelPair? _last;
    public void Configure(WidgetSettings s) => _cfg = s;

    string Cars => _cfg.OptionOr("cars", "rival").ToLowerInvariant();
    double ShowFor => double.TryParse(_cfg.OptionOr("showFor", "6"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? Math.Clamp(v, 3, 15) : 6;
    bool Preview => string.Equals(_cfg.OptionOr("layoutPreview", "false"), "true", StringComparison.OrdinalIgnoreCase);
    public bool IgnoresDrivingGate => string.Equals(_cfg.OptionOr("always", "false"), "true", StringComparison.OrdinalIgnoreCase);

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        if (!m.Connected || m.Session is not { } s) { _motion18.Reset(); _last = null; return; }
        var pair = Cars == "top2" && m.Standings.Count >= 2
            ? new DuelPair(m.Standings[0].Car, m.Standings[1].Car, 0)
            : Duel.Find(m, "auto", double.MaxValue, Preview);
        if (pair is not null) _last = pair;
        var b = BroadcastUi.State(m);
        float reveal;
        if (IgnoresDrivingGate || Preview) reveal = _motion18.Presence(m.Now, pair is not null, session: b.SessionSeenT);
        else
        {
            double t0 = Math.Max(b.SessionSeenT, b.PlayerPositionChangedT);
            reveal = pair is null ? 0 : _motion18.Evaluate(m.Now, t0, t0 + ShowFor, b.SessionSeenT);
        }
        if (reveal <= 0 || _last is not { } p) return;
        BroadcastUi.WithReveal18(c, reveal, W, PlateH, () =>
        {
            Plate(c, s, p.Ahead, 0);
            Plate(c, s, p.Behind, PlateW + Gap);
        });
    }

    void Plate(ThemeCanvas c, SessionSnapshot s, CarSnapshot car, float x)
    {
        string name = _cfg.Name(car, BroadcastUi.ShortName(car, s.Cars));
        string full = _cfg.Fmt.Name is null && _cfg.Fmt.CarNumber != true ? car.Name : name;
        CaptionPlate.Plate18(c, x, 0, PlateW, PlateH, car.Position, full, CaptionPlate.CarNumber(car), BroadcastUi.Team(car), bigBox: false,
            tick: CaptionPlate.ClassColor(car, s.Cars), country: car.Country);
    }
}
