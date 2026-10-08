using System.Globalization;
using System.Numerics;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// "Race Control" do gráfico de TV 2018 (ref. f1-2018-safetycar-box-and-pitlane.jpg, f1-2018-tower-yellowflag.jpg e o 7º quadro de
/// f1-2018-scan-race-t100-850.jpg). Dois elementos numa janela de tamanho fixo (o que não está em uso fica transparente):
/// <list type="bullet">
/// <item>caixa de bandeira: parte de cima preta com o nome da bandeira na cor dela ("YELLOW FLAG", "BLUE FLAG", "RED FLAG", "CHEQUERED") e parte
///   de baixo na cor da bandeira com ícone e texto fixo preto em negrito ("INCIDENT" na amarela). O $pcars2$ não distingue Safety Car de
///   amarela (HighestFlagColour), então não há "SAFETY CAR"; sem bandeira a caixa some;</item>
/// <item>barra preta "SOBRENOME SLOW STOP -x.xs" por showFor s depois de uma parada lenta do jogador (regra em <see cref="SlowStopDetector"/>).</item>
/// </list>
/// Exclusivo do tema f1-2018. Opções (WidgetCatalog.OptionsFor("f1-2018", "racecontrol")): showFlags, showSlowStop, slowStopLimit, showFor.
/// </summary>
public sealed class RaceControlWidget : IWidget
{
    public string Id => "racecontrol";
    public const float W = 300, TopH = 52, FlagH = 88, BoxH = TopH + FlagH, Gap = 10, BarH = 36, H = BoxH + Gap + BarH;
    public (float Width, float Height) DesignSize => (W, H);
    WidgetSettings _cfg = new() { Id = "racecontrol" };
    readonly Broadcast18Motion _flagMotion18 = new(), _slowMotion18 = new();
    Kind _lastFlag;
    public void Configure(WidgetSettings s) => _cfg = s;

    bool ShowFlags => !string.Equals(_cfg.OptionOr("showFlags", "true"), "false", StringComparison.OrdinalIgnoreCase);
    bool ShowSlowStop => !string.Equals(_cfg.OptionOr("showSlowStop", "true"), "false", StringComparison.OrdinalIgnoreCase);
    double Limit => Num("slowStopLimit", 5, 3, 30);
    double ShowFor => Num("showFor", 8, 3, 15);
    double Num(string id, double def, double min, double max)
        => double.TryParse(_cfg.OptionOr(id, def.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? Math.Clamp(v, min, max) : def;

    // FLAG_COLOUR_* do SharedMemory.h ($pcars2$): HighestFlagColour do participante visto.
    const uint FlagBlue = 2, FlagRed = 5, FlagYellow = 6, FlagDoubleYellow = 7, FlagChequered = 11;

    static Color4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);
    static readonly Color4 Black = Rgb(5, 6, 10, 0.92f), Ink = Rgb(10, 10, 12), White = Rgb(255, 255, 255),
        Blue = Rgb(0, 110, 210), BlueText = Rgb(70, 160, 255), Red = Rgb(225, 6, 0), RedText = Rgb(255, 64, 52);

    enum Kind { None, Yellow, DoubleYellow, Blue, Red, Chequered }

    static Kind FlagOf(uint colour) => colour switch
    {
        FlagYellow => Kind.Yellow,
        FlagDoubleYellow => Kind.DoubleYellow,
        FlagBlue => Kind.Blue,
        FlagRed => Kind.Red,
        FlagChequered => Kind.Chequered,
        _ => Kind.None,
    };

    Kind CurrentFlag(OverlayModel m) => ShowFlags && m.Connected && m.Session is { } s ? FlagOf(s.FlagColour) : Kind.None;

    /// <summary>Parada lenta a mostrar agora e o alfa da barra (fade de showFor s a partir do fim da parada).</summary>
    (SlowStop? Stop, float Alpha) SlowNow(OverlayModel m)
    {
        if (!ShowSlowStop || !m.Connected) return (null, 0f);
        var b = BroadcastUi.State(m);
        if (b.PlayerStopped || SlowStopDetector.Evaluate(b.PlayerStops, Limit) is not { } st) return (null, 0f);
        return (st, BroadcastUi.Fade(m.Now - b.PlayerStopEndT, ShowFor));
    }

    public bool IsIdle(OverlayModel model) => CurrentFlag(model) == Kind.None && SlowNow(model).Alpha <= 0.01f;

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        var t = c.Theme;
        var flag = CurrentFlag(m);
        if (!m.Connected) { _flagMotion18.Reset(); _slowMotion18.Reset(); _lastFlag = Kind.None; return; }
        if (flag != Kind.None) _lastFlag = flag;
        float flagReveal = _flagMotion18.Presence(m.Now, flag != Kind.None, session: BroadcastUi.State(m).SessionSeenT);
        if (_lastFlag != Kind.None) BroadcastUi.WithReveal18(c, flagReveal, W, BoxH, () => DrawFlagBox(c, t, _lastFlag));

        var (stop, alpha) = SlowNow(m);
        if (stop is not null && m.Session?.PlayerCar is { } car)
        {
            string name = _cfg.Name(car, BroadcastUi.ShortName(car, m.Session.Cars)).ToUpperInvariant();
            var b = BroadcastUi.State(m);
            float reveal = _slowMotion18.Evaluate(m.Now, b.PlayerStopEndT, b.PlayerStopEndT + ShowFor, b.SessionSeenT);
            BroadcastUi.WithReveal18(c, reveal, W, BarH, () => DrawSlowStop(c, t, name, stop), y: BoxH + Gap);
        }
    }

    void DrawFlagBox(ThemeCanvas c, Theme.Theme t, Kind flag)
    {
        var (title, text, fill, titleInk) = flag switch
        {
            Kind.Yellow => ("YELLOW FLAG", "INCIDENT", t.FlagColor, t.FlagColor),
            Kind.DoubleYellow => ("DOUBLE YELLOW", "INCIDENT", t.FlagColor, t.FlagColor),
            Kind.Blue => ("BLUE FLAG", "LET FASTER CAR BY", Blue, BlueText),
            Kind.Red => ("RED FLAG", "SESSION STOPPED", Red, RedText),
            _ => ("CHEQUERED", "FINISH", White, White),
        };

        // Parte de cima: preta, só os cantos de cima arredondados; nome da bandeira na cor dela, centralizado.
        c.FillRoundRect(0, 0, W, TopH, 7, Black);
        c.FillRect(0, TopH - 8, W, 8, Black);
        var tf = t.Title with { Size = 29, Weight = 700, Tracking = 0.5f };
        c.Text(title, BroadcastUi.Fit(c, title, tf, W - 24), 0, 2, W, TopH - 2, titleInk, HAlign.Center);

        // Parte de baixo: cor da bandeira com o canto inferior direito arredondado (como as placas do 2018).
        const float r = 10;
        Span<Vector2> pts = stackalloc Vector2[3 + 9 + 1];
        float y0 = TopH, y1 = BoxH;
        pts[0] = new(0, y0); pts[1] = new(W, y0); pts[2] = new(W, y1 - r);
        for (int i = 1; i <= 9; i++)
        {
            float a = MathF.PI / 2 * i / 9;
            pts[2 + i] = new(W - r + r * MathF.Cos(a), y1 - r + r * MathF.Sin(a));
        }
        pts[12] = new(0, y1);
        c.FillPolygon(pts, fill);

        // Linha de ícones (bandeiras cruzadas) e texto fixo preto em negrito, alinhados à esquerda como na TV.
        CrossedFlags(c, 18, y0 + 10, flag == Kind.Chequered);
        var bf = t.Title with { Size = 30, Weight = 800, Tracking = 0.5f };
        c.Text(text, BroadcastUi.Fit(c, text, bf, W - 32), 16, y0 + 40, W - 24, FlagH - 42, Ink);
    }

    /// <summary>Ícone das duas bandeirinhas cruzadas (mastros em X, panos para fora); xadrez na bandeirada.</summary>
    static void CrossedFlags(ThemeCanvas c, float x, float y, bool chequered)
    {
        const float s = 26;
        c.Line(x + 4, y + s, x + s - 2, y + 2, Ink, 2.4f);
        c.Line(x + s + 4, y + s, x + 6, y + 2, Ink, 2.4f);
        Cloth(c, x + s - 2, y + 2, 14, 10, chequered, mirror: false);
        Cloth(c, x + 6, y + 2, 14, 10, chequered, mirror: true);
    }

    static void Cloth(ThemeCanvas c, float px, float py, float w, float h, bool chequered, bool mirror)
    {
        float x = mirror ? px - w : px;
        if (!chequered) { c.FillRect(x, py, w, h, Ink); return; }
        c.StrokeRect(x, py, w, h, Ink, 1.2f);
        float cw = w / 4, ch = h / 2;
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 2; j++)
                if ((i + j) % 2 == 0) c.FillRect(x + i * cw, py + j * ch, cw, ch, Ink);
    }

    void DrawSlowStop(ThemeCanvas c, Theme.Theme t, string name, SlowStop stop)
    {
        float y = BoxH + Gap;
        c.FillRect(0, y, W, BarH, Black);
        var font = t.Text with { Size = 20, Weight = 500, Tracking = 0.5f };
        (string Text, FontToken Font)[] parts = [(name, font with { Element = "name" }),
            (" SLOW STOP ", font with { Element = "label" }),
            (SlowStopDetector.Format(stop.Lost), font with { Element = "gap" })];
        float total = parts.Sum(part => c.Measure(part.Text, part.Font));
        float fit = Math.Min(1, (W - 20) / Math.Max(1, total));
        float x = (W - total * fit) / 2;
        foreach (var part in parts)
        {
            var f = part.Font with { Size = part.Font.Size * fit, Tracking = part.Font.Tracking * fit };
            float width = c.Measure(part.Text, f);
            c.Text(part.Text, f, x, y, width + 1, BarH, t.TextColor);
            x += width;
        }
    }
}
