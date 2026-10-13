using System.Globalization;
using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Placa inferior da corrida 1993: so aparece quando ha algo a mostrar, por prioridade PIT STOP &gt; FASTEST LAP &gt; LAPS TO GO &gt; GAP.
/// Sem nenhum ativo nao desenha nada (o Race Standings fica o tempo todo; a placa some).
/// </summary>
public sealed class Broadcast93RaceBoard
{
    public const float Width = 1920, Height = 300;
    public static readonly Color4 White = new(.96f, .96f, .91f, 1), Yellow = new(1, .88f, .36f, 1), Cyan = new(.27f, .84f, .94f, 1),
        Blue = new(.30f, .46f, 1f, 1), BandBlue = new(20 / 255f, 40 / 255f, 200 / 255f, 1f), Pure = new(1, 1, 1, 1);
    public enum Mode { None, Gap, Fastest, Pit, LapsToGo }
    readonly FastestLapDetector _fastest = new();

    public static bool Visible(OverlayModel m)
        => m.Connected && m.PlayerDriving && m.Session is { InSession: true, GameState: 2, PlayerCar: not null };

    /// <summary>PIT STOP: parado no box, na pit lane antes da parada desta passagem, ou nos 3 s depois de sair do box.</summary>
    public static bool PitActive(BroadcastState b, double now)
        => b.PlayerStopped || b.PlayerInPitLane && b.PlayerStopEndT < b.PlayerPitLaneStartT
            || now - b.PlayerStopEndT is >= 0 && now - b.PlayerStopEndT < Broadcast93Rules.PitResultHold;

    /// <summary>Cronometro do pit: corre enquanto parado; depois da saida mostra a ultima parada; antes de parar, 0.0.</summary>
    public static double PitSeconds(BroadcastState b, double now)
        => b.PlayerStopped ? b.PlayerStopNow(now) : now - b.PlayerStopEndT is >= 0 && now - b.PlayerStopEndT < Broadcast93Rules.PitResultHold ? b.PlayerStopSeconds : 0;

    /// <summary>LAPS TO GO: nos primeiros 6 s da volta em que as voltas restantes sao multiplo de 5 (corrida por voltas).</summary>
    public static bool LapsToGoActive(OverlayModel m, BroadcastState b)
        => Broadcast93Rules.IsLapsToGo(Remaining(m)) && m.Now - b.PlayerLapChangedT is >= 0 && m.Now - b.PlayerLapChangedT < Broadcast93Rules.LapsToGoHold;

    static int? Remaining(OverlayModel m) => m.Board?.LapsRemaining ?? (m.Session is { } s ? Broadcast93Rules.LapsRemaining(s) : null);

    public static Mode Select(OverlayModel m, FastestLapEvent? fastest = null, bool fastestEnabled = true)
    {
        if (!Visible(m)) return Mode.None;
        var b = BroadcastUi.State(m);
        if (PitActive(b, m.Now)) return Mode.Pit;
        if (fastestEnabled && fastest?.ActiveAt(m.Now) == true) return Mode.Fastest;
        if (LapsToGoActive(m, b)) return Mode.LapsToGo;
        return m.Board?.Gap93 is { } gap && m.Now >= gap.CompletedT && m.Now < gap.CloseT ? Mode.Gap : Mode.None;
    }

    public void Draw(ThemeCanvas c, OverlayModel m, WidgetSettings cfg)
    {
        if (!m.Connected || m.Session is { GameState: 3 }) _fastest.Reset();
        if (!Visible(m)) return;
        var session = m.Session!;
        _fastest.Update(m.Now, session, m.QualiLap?.SessionGeneration ?? -1);
        bool fastestOn = !string.Equals(cfg.OptionOr("showFastest", "true"), "false", StringComparison.OrdinalIgnoreCase);
        switch (Select(m, _fastest.Latest, fastestOn))
        {
            case Mode.Pit: Pit(c, PitSeconds(BroadcastUi.State(m), m.Now)); break;
            case Mode.Fastest: Fastest(c, _fastest.Latest!.Car, session.Cars, cfg, session.TrackLength); break;
            case Mode.LapsToGo: LapsToGo(c, Remaining(m) ?? 0); break;
            case Mode.Gap: Gap(c, m.Board!.Gap93!, cfg); break;
        }
    }

    public static void Band(ThemeCanvas c, float y = 30, float height = 230)
        => c.FillRect(0, y, Width, height, c.Theme.PanelFill);

    public static void Text(ThemeCanvas c, string value, float x, float y, float width, string element, float size, Color4 color, HAlign align = HAlign.Left)
    {
        var font = (element is "time" or "gap" or "position" or "value" ? c.Theme.Numbers : c.Theme.Text) with { Element = element, Size = size };
        c.Text(value, BroadcastUi.Fit(c, value, font, width), x, y, width, Math.Max(62, size * 1.35f), color, align, c.Theme.TextShadow);
    }

    /// <summary>"Mark BLUNDELL": nome em caixa normal e SOBRENOME em caixa alta (ou o nome do perfil, se houver).</summary>
    public static string FullName(CarSnapshot car, IReadOnlyList<CarSnapshot> field, WidgetSettings cfg)
    {
        string name = cfg.Name(car, BroadcastUi.ShortName(car, field));
        if (cfg.Fmt.Name is null && cfg.Fmt.CarNumber != true)
        {
            string full = car.Name.Trim();
            int space = full.LastIndexOf(' ');
            name = space < 0 ? full.ToUpperInvariant() : full[..(space + 1)] + full[(space + 1)..].ToUpperInvariant();
        }
        return name;
    }

    /// <summary>Iniciais do primeiro e do ultimo nome ("Mark Blundell" -> "MB").</summary>
    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        return (parts[0][..1] + (parts.Length > 1 ? parts[^1][..1] : "")).ToUpperInvariant();
    }

    /// <summary>Foto do piloto (Assets\Drivers) ou, sem arquivo, silhueta cinza com as iniciais.</summary>
    public static void Photo(ThemeCanvas c, string fullName, float x, float y, float w, float h)
    {
        if (c.DriverPhoto(fullName, x, y, w, h)) return;
        c.FillRect(x, y, w, h, new(.40f, .42f, .43f, 1));
        var dark = new Color4(.27f, .28f, .29f, 1);
        c.FillEllipse(x + w / 2, y + h * .34f, w * .17f, h * .17f, dark);
        c.FillPolygon([new(x + w * .10f, y + h), new(x + w * .16f, y + h * .68f), new(x + w * .38f, y + h * .56f), new(x + w * .62f, y + h * .56f), new(x + w * .84f, y + h * .68f), new(x + w * .90f, y + h)], dark);
        string initials = Initials(fullName);
        if (initials.Length > 0)
            c.Text(initials, c.Theme.Text with { Size = h * .26f }, x, y + h * .66f, w, h * .34f, new(.92f, .92f, .88f, 1), HAlign.Center, c.Theme.TextShadow);
    }

    /// <summary>Linha "26 Mark BLUNDELL": numero do carro em amarelo (se conhecido) e nome em branco.</summary>
    public static void NameLine(ThemeCanvas c, CarSnapshot car, string name, float x, float y, float width, float size)
    {
        float nx = x;
        if (car.CarNumber.Length > 0)
        {
            Text(c, car.CarNumber, x, y, 70, "position", size, Yellow, HAlign.Right);
            nx = x + 85;
        }
        Text(c, name, nx, y, width - (nx - x), "name", size, c.Theme.TextColor);
    }

    /// <summary>Painel comum da legenda e do FASTEST LAP: foto, "26 Mark BLUNDELL", equipe em amarelo e faixa olivetti.</summary>
    static void Plate(ThemeCanvas c, CarSnapshot car, IReadOnlyList<CarSnapshot> field, WidgetSettings cfg, float top, float height, float firstRow, bool tag = false)
    {
        c.FillRect(165, top, 1130, height - 52, c.Theme.PanelFill);
        Photo(c, car.Name.Trim(), 180, top + 12, 150, height - 12 - 70);
        NameLine(c, car, FullName(car, field, cfg), 355, top + firstRow, 520, 40);
        Text(c, BroadcastUi.Team(car).ToUpperInvariant(), 355, top + firstRow + 58, 520, "label", 36, Yellow);
        // Faixa mais curta, centralizada logo abaixo do painel (como na TV): Olivetti na lenta/fastest lap, TAG Heuer na legenda.
        float sy = top + height - 52 + 6, sw = 540, sx = 165 + (1130 - sw) / 2;
        if (tag) SponsorStrip.TagHeuer(c, sx, sy, 400, 46);
        else SponsorStrip.Olivetti(c, 165 + (1130 - sw) / 2, sy, sw, 46, "computer");
    }

    /// <summary>Legenda do piloto do jogador (ou observado): foto, "26 Mark BLUNDELL", equipe e faixa olivetti / TAG Heuer.</summary>
    public static void Caption(ThemeCanvas c, OverlayModel m, WidgetSettings cfg, bool qualifying = false)
    {
        if (m.Session?.PlayerCar is not { } car) return;
        Plate(c, car, m.Session.Cars, cfg, 40, 215, 28, tag: true);
    }

    public static void Onboard(ThemeCanvas c, CarSnapshot car, IReadOnlyList<CarSnapshot> field, WidgetSettings cfg)
        => Text(c, cfg.Name(car, car.Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "").ToUpperInvariant(), 165, 180, 1250, "name", 38, c.Theme.TextColor);

    /// <summary>FASTEST LAP: foto a esquerda, titulo ciano e tempo (1'27.438) em cima, "2 Alain PROST", equipe em amarelo, velocidade media
    /// (Kmh / Mph, so com a extensao da pista conhecida) a direita e faixa olivetti.</summary>
    public static void Fastest(ThemeCanvas c, CarSnapshot car, IReadOnlyList<CarSnapshot> field, WidgetSettings cfg, double trackLengthM = 0)
    {
        if (!double.IsFinite(car.BestLapTime) || car.BestLapTime <= 0) return;
        Plate(c, car, field, cfg, 40, 235, 70);
        Text(c, "FASTEST LAP", 355, 48, 480, "title", 40, Cyan);
        Text(c, cfg.Fmt.LapTime is null ? Broadcast93QualiBoard.Time(car.BestLapTime) : cfg.Fmt.FormatLapTime(car.BestLapTime), 870, 48, 400, "time", 46, c.Theme.TextColor, HAlign.Right);
        if (!double.IsFinite(trackLengthM) || trackLengthM <= 0) return;
        double kmh = trackLengthM / 1000 / (car.BestLapTime / 3600);
        Text(c, kmh.ToString("0.00", CultureInfo.InvariantCulture) + " Kmh", 870, 110, 400, "value", 36, c.Theme.TextColor, HAlign.Right);
        Text(c, (kmh * 0.621371).ToString("0.00", CultureInfo.InvariantCulture) + " Mph", 870, 168, 400, "value", 36, c.Theme.TextColor, HAlign.Right);
    }

    /// <summary>PIT STOP: faixa "PIT STOP" com seta triangular e cronometro grande numa placa escura com o texto "TAG Heuer".</summary>
    public static void Pit(ThemeCanvas c, double seconds)
    {
        // Uma unica barra translucida: "PIT STOP" ciano + seta amarela colada, cronometro amarelo a direita (como na TV).
        const float bx = 165, bw = 860, by = 70, bh = 100;
        c.FillRect(bx, by, bw, bh, c.Theme.PanelFill);
        Text(c, "PIT STOP", bx + 30, by + 8, 380, "title", 66, c.Theme.LabelColor);
        c.FillPolygon([new(bx + 346, by + 6), new(bx + 410, by + bh / 2), new(bx + 346, by + bh - 6)], Yellow);
        Text(c, seconds.ToString("0.0", CultureInfo.InvariantCulture), bx + 470, by + 2, 300, "time", 80, c.Theme.ValueColor, HAlign.Center);
        // Placa TAG Heuer (mais estreita, escudo maior) centralizada abaixo da barra.
        const float pw = 340, ph = 80;
        SponsorStrip.TagHeuer(c, bx + (bw - pw) / 2, by + bh + 22, pw, ph, .5f);
    }

    /// <summary>LAPS TO GO: bandeira quadriculada pequena (quadrados preto e branco) e "10 LAPS TO GO" ("1 LAP TO GO" no singular).</summary>
    public static void LapsToGo(ThemeCanvas c, int remaining)
    {
        c.FillRect(165, 80, 980, 130, c.Theme.PanelFill);
        const float cell = 16;
        for (int row = 0; row < 5; row++)
            for (int col = 0; col < 8; col++)
                c.FillRect(200 + col * cell, 105 + row * cell, cell, cell, (row + col) % 2 == 0 ? new Color4(0, 0, 0, 1) : Pure);
        c.Line(198, 103, 198, 187, new(.85f, .86f, .82f, 1), 3);
        c.Text(LapsToGoText(remaining), c.Theme.Title with { Size = 70 }, 360, 98, 760, 80, c.Theme.TextColor, HAlign.Left, c.Theme.TextShadow);
    }

    public static string LapsToGoText(int remaining)
        => remaining.ToString(CultureInfo.InvariantCulture) + (remaining == 1 ? " LAP TO GO" : " LAPS TO GO");

    public static void Gap(ThemeCanvas c, BoardGap93 gap, WidgetSettings cfg)
    {
        Band(c);
        var front = gap.Ahead; var rear = gap.Behind;
        // Silhueta em Assets\Cars (se o usuario forneceu); senao o desenho generico original, nunca sprites extraidos da transmissao.
        if (!c.CarImage(front.Team, 180, 65, 290, 90)) TinyCar(c, 180, 75, new(.72f, .73f, .35f, 1));
        if (!c.CarImage(rear.Team, 1350, 65, 290, 90)) TinyCar(c, 1350, 75, new(.89f, .45f, .38f, 1));
        Text(c, front.Position > 0 ? front.Position.ToString(CultureInfo.InvariantCulture) : "-", 490, 65, 120, "position", 40, c.Theme.TextColor);
        Text(c, rear.Position > 0 ? rear.Position.ToString(CultureInfo.InvariantCulture) : "-", 1660, 65, 120, "position", 40, c.Theme.TextColor);
        Text(c, cfg.Name(front.Name, front.CarIndex, front.ShortName).ToUpperInvariant(), 165, 172, 590, "name", 38, c.Theme.TextColor);
        Text(c, cfg.Name(rear.Name, rear.CarIndex, rear.ShortName).ToUpperInvariant(), 1330, 172, 530, "name", 38, c.Theme.TextColor);
        Text(c, gap.GapText, 760, 65, 400, "gap", 50, c.Theme.ValueColor, HAlign.Center);
        c.Line(800, 207, 1120, 207, new(.85f, .86f, .82f, 1), 10);
        if (!string.Equals(cfg.OptionOr("gapArrow", "true"), "false", StringComparison.OrdinalIgnoreCase))
            c.FillPolygon([new(940, 181), new(988, 207), new(940, 233)], new(.40f, .81f, .47f, 1));
    }

    static void TinyCar(ThemeCanvas c, float x, float y, Color4 color)
    {
        c.FillPolygon([new(x, y + 39), new(x + 43, y + 23), new(x + 90, y), new(x + 145, y), new(x + 173, y + 23), new(x + 270, y + 39), new(x + 270, y + 53), new(x, y + 53)], color);
        c.FillRect(x + 99, y + 5, 47, 25, White);
        c.FillRect(x + 243, y + 5, 16, 40, color);
        var tyre = new Color4(.14f, .16f, .15f, 1);
        c.FillEllipse(x + 60, y + 50, 27, 27, tyre); c.FillEllipse(x + 216, y + 50, 27, 27, tyre);
    }
}
