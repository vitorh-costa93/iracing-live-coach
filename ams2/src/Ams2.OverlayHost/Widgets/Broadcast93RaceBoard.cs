using System.Globalization;
using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>1993 plates use cuts, fixed gap results, and only measured session values.</summary>
public sealed class Broadcast93RaceBoard
{
    public const float Width = 1920, Height = 300;
    public static readonly Color4 White = new(.96f, .96f, .91f, 1), Yellow = new(1, .88f, .36f, 1), Cyan = new(.27f, .84f, .94f, 1);
    public enum Mode { None, Caption, Gap, Fastest }
    string? _session;
    int _player = -1, _lastLaps = -1;
    double _best = double.PositiveInfinity, _lastNow = double.NegativeInfinity, _fastestAt = double.NegativeInfinity;
    CarSnapshot? _fastest;
    bool _observed;
    int _generation = -1;

    public static bool Visible(OverlayModel m)
        => m.Connected && m.PlayerDriving && m.Session is { InSession: true, GameState: 2, PlayerCar: not null };

    public static Mode Select(OverlayModel m)
        => !Visible(m) ? Mode.None : m.Board?.Gap93 is { } gap && m.Now >= gap.CompletedT && m.Now < gap.CloseT ? Mode.Gap : Mode.Caption;

    public void Draw(ThemeCanvas c, OverlayModel m, WidgetSettings cfg)
    {
        if (!m.Connected || m.Session is { GameState: 3 }) Reset();
        if (!Visible(m)) return;
        var session = m.Session!;
        var player = session.PlayerCar!;
        string key = session.Track + "\n" + session.TrackVariation + "\n" + session.Kind;
        if (_session != key || _generation != (m.QualiLap?.SessionGeneration ?? -1) || _player != player.Index || m.Now < _lastNow || player.LapsCompleted < _lastLaps)
        { Reset(); _session = key; _player = player.Index; _generation = m.QualiLap?.SessionGeneration ?? -1; }
        _lastNow = m.Now; _lastLaps = player.LapsCompleted;
        var bestCar = session.Cars.Where(car => double.IsFinite(car.BestLapTime) && car.BestLapTime > 0).OrderBy(car => car.BestLapTime).FirstOrDefault();
        if (bestCar is not null && bestCar.BestLapTime < _best - .0005)
        {
            if (_observed) { _fastest = bestCar; _fastestAt = m.Now; }
            _best = bestCar.BestLapTime;
        }
        _observed = true;
        var mode = Select(m);
        if (mode == Mode.Gap) Gap(c, m.Board!.Gap93!, cfg);
        else if (_fastest is not null && m.Now - _fastestAt is >= 0 and < 7 && !string.Equals(cfg.OptionOr("showFastest", "true"), "false", StringComparison.OrdinalIgnoreCase)) Fastest(c, _fastest, session.Cars, cfg);
        else
        {
            var state = BroadcastUi.State(m);
            double at = Math.Max(state.SessionSeenT, Math.Max(state.PlayerPositionChangedT, state.PlayerLapChangedT));
            if (cfg.ColumnVisible("always") || m.Now - at is >= 0 and < BroadcastUi.CaptionHold)
            {
                if (string.Equals(cfg.OptionOr("captionMode", "full"), "onboard", StringComparison.OrdinalIgnoreCase)) Onboard(c, player, session.Cars, cfg);
                else Caption(c, m, cfg);
            }
        }
    }

    void Reset() { _observed = false; _generation = -1; _session = null; _player = _lastLaps = -1; _best = double.PositiveInfinity; _lastNow = _fastestAt = double.NegativeInfinity; _fastest = null; }

    public static void Band(ThemeCanvas c, float y = 30, float height = 230)
        => c.FillRect(0, y, Width, height, c.Theme.PanelFill);

    public static void Text(ThemeCanvas c, string value, float x, float y, float width, string element, float size, Color4 color, HAlign align = HAlign.Left)
    {
        var font = (element is "time" or "gap" or "position" or "value" ? c.Theme.Numbers : c.Theme.Text) with { Element = element, Size = size };
        c.Text(value, BroadcastUi.Fit(c, value, font, width), x, y, width, Math.Max(62, size * 1.35f), color, align, c.Theme.TextShadow);
    }

    public static void Caption(ThemeCanvas c, OverlayModel m, WidgetSettings cfg, bool qualifying = false)
    {
        if (m.Session?.PlayerCar is not { } car) return;
        c.FillRect(165, 65, qualifying ? 1590 : 1130, 175, c.Theme.PanelFill);
        Text(c, cfg.Name(car, BroadcastUi.ShortName(car, m.Session.Cars)).ToUpperInvariant(), 210, 80, 1000, "name", 38, c.Theme.TextColor);
        if (qualifying || cfg.Id == "board" || cfg.ColumnVisible("team")) Text(c, BroadcastUi.Team(car).ToUpperInvariant(), 210, 157, 1000, "label", 38, c.Theme.ValueColor);
        if (qualifying)
        {
            int pos = m.Quali?.Player?.Rank ?? car.Position;
            Text(c, pos > 0 ? "(" + pos.ToString(CultureInfo.InvariantCulture) + ")" : "(-)", 1300, 80, 370, "position", 40, c.Theme.ReadoutColor, HAlign.Center);
            Text(c, QualiCaption.Gap(m, cfg), 1270, 157, 430, "gap", 50, c.Theme.ValueColor, HAlign.Center);
        }
    }

    public static void Onboard(ThemeCanvas c, CarSnapshot car, IReadOnlyList<CarSnapshot> field, WidgetSettings cfg)
        => Text(c, cfg.Name(car, car.Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "").ToUpperInvariant(), 165, 180, 1250, "name", 38, c.Theme.TextColor);

    public static void Fastest(ThemeCanvas c, CarSnapshot car, IReadOnlyList<CarSnapshot> field, WidgetSettings cfg)
    {
        if (!double.IsFinite(car.BestLapTime) || car.BestLapTime <= 0) return;
        c.FillRect(165, 0, 1590, 280, c.Theme.PanelFill);
        Text(c, "FASTEST LAP", 210, 5, 1030, "title", 40, c.Theme.ReadoutColor);
        Text(c, cfg.Fmt.LapTime is null ? Broadcast93QualiBoard.Time(car.BestLapTime) : cfg.Fmt.FormatLapTime(car.BestLapTime), 1260, 5, 440, "time", 50, c.Theme.TextColor, HAlign.Right);
        Text(c, cfg.Name(car, BroadcastUi.ShortName(car, field)).ToUpperInvariant(), 210, 100, 1420, "name", 38, c.Theme.TextColor);
        if (cfg.Id == "board" || cfg.ColumnVisible("team")) Text(c, BroadcastUi.Team(car).ToUpperInvariant(), 210, 190, 1420, "label", 38, c.Theme.ValueColor);
    }

    public static void Gap(ThemeCanvas c, BoardGap93 gap, WidgetSettings cfg)
    {
        Band(c);
        var front = gap.Ahead; var rear = gap.Behind;
        // Original generic silhouettes, not sprites extracted from broadcast footage.
        TinyCar(c, 180, 75, new(.72f, .73f, .35f, 1)); TinyCar(c, 1350, 75, new(.89f, .45f, .38f, 1));
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
