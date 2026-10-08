using System.Globalization;
using System.Numerics;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Painel "LIVE SPEED" do gráfico de TV 2018 (ref. f1-2018-crops-livespeed-racestart-radio-caption.jpg, 1º recorte): placa preta ≈ 90 %
/// com filete vermelho no topo, "LIVE SPEED" branco + ícone de velocímetro, sobrenome do jogador centralizado e a velocidade em vermelho
/// ("330" KM/H / "205" MPH, separados por uma barra diagonal fina, rótulos azul-acinzentados); canto inferior direito arredondado.
/// Exclusivo do tema f1-2018. Opções (WidgetCatalog.OptionsFor("f1-2018", "livespeed")): units (both/kph/mph), showName, always
/// (padrão: só com o jogador no carro, pela regra única OverlayModel.PlayerDriving do host; ligado = fica na tela mesmo fora do carro).
/// </summary>
public sealed class LiveSpeedWidget : IWidget
{
    public string Id => "livespeed";
    public bool HighFrequency => true;
    public const float W = 300, H = 196, NameH = 30;
    public (float Width, float Height) DesignSize => (W, ShowName ? H : H - NameH);
    WidgetSettings _cfg = new() { Id = "livespeed" };
    readonly Broadcast18Motion _motion18 = new();
    bool _seenConnection18;
    public void Configure(WidgetSettings s) => _cfg = s;

    string Units => _cfg.OptionOr("units", "both").ToLowerInvariant();
    bool ShowName => !string.Equals(_cfg.OptionOr("showName", "true"), "false", StringComparison.OrdinalIgnoreCase);
    /// <summary>Opção "always": o host deixa a janela aberta mesmo sem o jogador no carro.</summary>
    public bool IgnoresDrivingGate => string.Equals(_cfg.OptionOr("always", "false"), "true", StringComparison.OrdinalIgnoreCase);

    static Color4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);
    static readonly Color4 Red = Rgb(230, 36, 43), UnitInk = Rgb(143, 176, 189), Plate = Rgb(0, 0, 0, 0.9f);

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        var t = c.Theme;
        var (w, h) = DesignSize;
        if (!m.Connected) { _motion18.Reset(); _seenConnection18 = false; }
        if (m.Connected && !_seenConnection18)
        {
            // Editing/always stays settled; a newly connected live plate reveals once.
            if (!IgnoresDrivingGate)
            {
                double connectedAt = BroadcastUi.State(m).SessionSeenT;
                _motion18.Presence(connectedAt, false);
                _motion18.Presence(connectedAt, true, session: connectedAt);
            }
            _seenConnection18 = true;
        }
        float reveal = m.Connected ? _motion18.Presence(m.Now, true, session: BroadcastUi.State(m).SessionSeenT) : 1;
        using var clip = c.Theme.Style == ThemeStyle.Modern2018 ? c.Clip(0, 0, w * reveal, h) : null;
        var player = m.Connected ? m.Session?.Player : null;
        var car = m.Connected ? m.Session?.PlayerCar : null;

        // Placa: um poligono so (canto inferior direito arredondado) + filete vermelho no topo.
        const float r = 12;
        Span<Vector2> pts = stackalloc Vector2[3 + 9 + 1];
        pts[0] = new(0, 0); pts[1] = new(w, 0); pts[2] = new(w, h - r);
        for (int i = 1; i <= 9; i++)
        {
            float a = MathF.PI / 2 * i / 9;
            pts[2 + i] = new(w - r + r * MathF.Cos(a), h - r + r * MathF.Sin(a));
        }
        pts[12] = new(0, h);
        c.FillPolygon(pts, Plate);
        c.FillRect(0, 0, w, 5, t.AccentBar.A > 0 ? t.AccentBar : Red);

        c.Text("LIVE SPEED", t.Title with { Weight = 400, Size = 27, Tracking = 0.5f }, 15, 6, 220, 40, t.TitleColor);
        Gauge(c, 266, 29, 16, t.TitleColor);

        float y = 54;
        if (ShowName)
        {
            string name = car is null ? "" : _cfg.Name(car, BroadcastUi.ShortName(car, m.Session!.Cars)).ToUpperInvariant();
            c.Text(name, BroadcastUi.Fit(c, name, t.Text with { Weight = 700, Size = 21, Tracking = 0.5f }, w - 30), 0, y, w, NameH, t.TextColor, HAlign.Center);
            y += NameH;
        }

        double speed = player is null ? 0 : RenderPlayerTelemetry.Read(m.Inputs, player).SpeedMps;
        string kph = player is null ? "---" : Speed(speed, SpeedUnit.Kph);
        string mph = player is null ? "---" : Speed(speed, SpeedUnit.Mph);
        var big = t.Numbers with { Weight = 400, Size = 55, Tracking = 0f };
        var unit = t.Label with { Weight = 400, Size = 24, Tracking = 0.5f };
        switch (Units)
        {
            case "kph": Single(c, kph, "KM/H", big, unit, y, w); break;
            case "mph": Single(c, mph, "MPH", big, unit, y, w); break;
            default:
                // Esquerda: numero em cima, rotulo embaixo; direita (deslocada para baixo): rotulo em cima, numero embaixo.
                c.Text(kph, big, 20, y + 6, 140, 60, Red);
                c.Text("KM/H", unit, 23, y + 64, 120, 30, UnitInk);
                c.Line(166, y + 24, 146, y + 82, Rgb(255, 255, 255, 0.3f), 1.4f);
                c.Text("MPH", unit, 150, y + 20, 125, 30, UnitInk, HAlign.Right);
                c.Text(mph, big, 130, y + 39, 146, 60, Red, HAlign.Right);
                break;
        }
    }

    static string Speed(double mps, SpeedUnit u)
        => Math.Max(0, Math.Round(DisplayFormat.Speed(mps, u))).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>Uma unidade so: numero grande centralizado e rotulo embaixo.</summary>
    static void Single(ThemeCanvas c, string value, string label, FontToken big, FontToken unit, float y, float w)
    {
        c.Text(value, big, 0, y + 4, w, 60, Red, HAlign.Center);
        c.Text(label, unit, 0, y + 64, w, 28, UnitInk, HAlign.Center);
    }

    /// <summary>Ícone de velocímetro: arco de ~270° aberto embaixo, ponteiro para cima-direita e cubo.</summary>
    static void Gauge(ThemeCanvas c, float cx, float cy, float rad, Color4 ink)
    {
        const int n = 18;
        float a0 = MathF.PI * 0.75f, a1 = MathF.PI * 2.25f;   // de baixo-esquerda, passando pelo topo, ate baixo-direita
        for (int i = 0; i < n; i++)
        {
            float u = a0 + (a1 - a0) * i / n, v = a0 + (a1 - a0) * (i + 1) / n;
            c.Line(cx + rad * MathF.Cos(u), cy + rad * MathF.Sin(u), cx + rad * MathF.Cos(v), cy + rad * MathF.Sin(v), ink, 3f);
        }
        float na = -MathF.PI * 0.3f;
        c.Line(cx, cy + 2, cx + rad * 0.72f * MathF.Cos(na), cy + 2 + rad * 0.72f * MathF.Sin(na), ink, 2.6f);
        c.FillEllipse(cx, cy + 2, 3, 3, ink);
    }
}
