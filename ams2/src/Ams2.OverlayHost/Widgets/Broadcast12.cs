using System.Globalization;
using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Peças do gráfico de TV 2012 (ref. GP da Malásia 2012) que não existem no 2004: placa nome/equipe com a caixa de velocidade (km/h amarelo,
/// DRS, barra de KERS) do duelo. O resto do tema 2012 reaproveita a lógica do 2004 com a pele inclinada de <see cref="Chrome"/>.
/// </summary>
public static class Broadcast12
{
    static Color4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);
    static readonly Color4 SpeedYellow = Rgb(255, 226, 40), UnitInk = Rgb(190, 192, 200), DrsOn = Rgb(30, 190, 62), DrsOnInk = Rgb(10, 40, 14),
        DrsOffFill = Rgb(46, 46, 52), DrsOffInk = Rgb(104, 106, 114), BoxFill = Rgb(6, 6, 8, 0.96f), KersRed = Rgb(206, 18, 28), KersFrame = Rgb(92, 92, 98),
        KersActive = Rgb(255, 214, 64), KersInside = Rgb(26, 26, 30), Bolt = Rgb(255, 226, 40);

    /// <summary>true se o tema é a pele 2012 (estilo 2004 com células inclinadas).</summary>
    public static bool Is(Theme.Theme t) => t.Style == ThemeStyle.Broadcast2000s && t.CellSlant > 0;

    /// <summary>"N ROSBERG": inicial do primeiro nome + sobrenome em caixa alta, como nas placas de 2012; nome único fica inteiro.</summary>
    public static string InitialName(string fullName)
    {
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        if (parts.Length == 1) return parts[0].ToUpperInvariant();
        return (parts[0][0] + " " + string.Join(' ', parts[1..])).ToUpperInvariant();
    }

    /// <summary>Carga do KERS em 0..1 a partir do mBoostAmount (a escala não foi conferida no jogo: valores &gt; 1 são tratados como porcentagem).</summary>
    public static double KersLevel(double boostAmount) => Math.Clamp(boostAmount > 1 ? boostAmount / 100.0 : boostAmount, 0, 1);

    /// <summary>Placa nome (preto) + equipe (azul-ardósia) de largura <paramref name="w"/>, com a bandeira do país no fim da linha do nome.</summary>
    public static void NamePlate(ThemeCanvas c, float x, float y, float w, string name, string team, string? country, float rowH = 32)
    {
        var t = c.Theme;
        bool flag = c.HasFlag(country);
        float room = flag ? 46 : 0;
        Chrome.Box(c, x, y, w, rowH, name, BroadcastUi.Fit(c, name, t.Text, w - 28 - room), Chrome.CellKind.Black);
        if (flag) c.Flag(country, x + w - 8 - 34, y + (rowH - 22) / 2, 34, 22);
        if (team.Length > 0) Chrome.Box(c, x, y + rowH, w, rowH, team, BroadcastUi.Fit(c, team, t.Text with { Weight = 500 }, w - 28), Chrome.CellKind.Navy);
    }

    /// <summary>
    /// Caixa de velocidade sobre a placa: "277" amarelo com "km/h" embaixo, etiqueta DRS (verde acesa / apagada) e a barra de KERS à direita.
    /// <paramref name="drsKnown"/>/<paramref name="kersKnown"/> = false quando o AMS2 não informa o dado (carros do rival): DRS apagado e barra só com moldura.
    /// </summary>
    public static void SpeedBox(ThemeCanvas c, float x, float y, float w, float h, double kph, bool showDrs, bool drsOn, bool kersKnown, double kers, bool boostActive)
    {
        var t = c.Theme;
        c.FillRoundRect(x, y, w, h, 6, BoxFill);
        string speed = Math.Max(0, Math.Round(kph)).ToString("0", CultureInfo.InvariantCulture);
        c.Text(speed, t.Numbers with { Weight = 500, Size = 40 }, x + 8, y + 1, 84, h - 20, SpeedYellow, HAlign.Left);
        c.Text("km/h", t.Label with { Weight = 500, Size = 15 }, x + 12, y + h - 22, 60, 18, UnitInk, HAlign.Left);
        if (showDrs)
        {
            float dx = x + 92, dy = y + 12, dw = 44, dh = 30;
            c.FillRoundRect(dx, dy, dw, dh, 4, drsOn ? DrsOn : DrsOffFill);
            c.Text("DRS", t.Label with { Weight = 700, Size = 20 }, dx, dy, dw, dh, drsOn ? DrsOnInk : DrsOffInk, HAlign.Center);
        }
        Kers(c, x + w - 34, y + 6, 26, h - 12, kers, kersKnown, boostActive);
    }

    /// <summary>Barra vertical de KERS: moldura (amarela com o boost ativo), carga vermelha de baixo para cima, "+" em cima, "-" embaixo e raio amarelo.</summary>
    public static void Kers(ThemeCanvas c, float x, float y, float w, float h, double level, bool known, bool active)
    {
        var t = c.Theme;
        c.FillRoundRect(x, y, w, h, 5, active ? KersActive : KersFrame);
        c.FillRoundRect(x + 2, y + 2, w - 4, h - 4, 4, KersInside);
        if (known)
        {
            float inner = h - 4, fill = inner * (float)Math.Clamp(level, 0, 1);
            c.FillRect(x + 2, y + 2 + inner - fill, w - 4, fill, KersRed);
        }
        var small = t.Label with { Weight = 700, Size = 15 };
        c.Text("+", small, x, y, w, 16, new Color4(1, 1, 1, 1), HAlign.Center);
        c.Text("-", small, x, y + h - 16, w, 16, new Color4(1, 1, 1, 1), HAlign.Center);
        float cx = x + w / 2, cy = y + h / 2;
        c.FillPolygon([new(cx + 3, cy - 10), new(cx - 5, cy + 1), new(cx - 0.5f, cy + 1), new(cx - 3, cy + 10), new(cx + 5, cy - 2), new(cx + 0.5f, cy - 2)], Bolt);
    }

    /// <summary>Duelo 2012: duas unidades (carro da frente à esquerda, de trás à direita), cada uma com a caixa de velocidade sobre a placa nome/equipe.</summary>
    public static void DrawBattle(ThemeCanvas c, OverlayModel m, SessionSnapshot s, DuelPair p, WidgetSettings cfg, bool showDrs)
    {
        const float unitW = 328, plateW = 285, boxH = 62, plateY = 66;
        Unit(c, m, s, p.Ahead, 0, unitW, plateW, boxH, plateY, cfg, showDrs);
        Unit(c, m, s, p.Behind, BattleWidget.W12 - unitW, unitW, plateW, boxH, plateY, cfg, showDrs);
    }

    static void Unit(ThemeCanvas c, OverlayModel m, SessionSnapshot s, CarSnapshot car, float x0, float unitW, float plateW, float boxH, float plateY, WidgetSettings cfg, bool showDrs)
    {
        bool isPlayer = car.IsPlayer || car.Index == s.Player?.Index;
        var pl = isPlayer ? s.Player : null;
        double mps = pl is not null ? RenderPlayerTelemetry.Read(m.Inputs, pl).SpeedMps : car.SpeedMps;
        double kph = DisplayFormat.Speed(mps, SpeedUnit.Kph);
        string name = cfg.Fmt.Name is null && cfg.Fmt.CarNumber != true ? InitialName(car.Name) : cfg.Name(car, BroadcastUi.ShortName(car, s.Cars)).ToUpperInvariant();
        SpeedBox(c, x0 + unitW - 178, 0, 178, boxH, kph, showDrs, pl?.BoostActive == true,
            pl is not null, pl is null ? 0 : KersLevel(pl.BoostAmount), pl?.BoostActive == true);
        NamePlate(c, x0, plateY, plateW, name, BroadcastUi.Team(car), car.Country);
    }
}
