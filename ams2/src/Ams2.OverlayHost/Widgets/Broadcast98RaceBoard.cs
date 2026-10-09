using System.Globalization;
using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Rodapé de transmissão 1998: um único painel e um único evento por quadro.</summary>
public sealed class Broadcast98RaceBoard
{
    public const float Width = 1920, Height = 300;
    public enum Mode { None, Caption, Laps, Sector, Tower, Pit, Winner }
    long _key = long.MinValue;
    double _since;
    int _comparisonLap = -1;
    double _comparisonSince;
    double _lastNow = double.NegativeInfinity;

    public static Mode Select(OverlayModel m)
    {
        if (!m.Connected || m.Session?.PlayerCar is null) return Mode.None;
        var bc = BroadcastUi.State(m);
        if (bc.Winner is { } win && m.Now - win.FinishedT is >= 0 and < BroadcastUi.WinnerHold) return Mode.Winner;
        if (bc.PlayerStopped || m.Now - bc.PlayerStopEndT is >= 0 and < BroadcastUi.PitTimerHold) return Mode.Pit;
        var board = m.Board;
        if (board?.Mode == BoardMode.LineTower && board.Tower is not null) return Mode.Tower;
        if (board?.SectorGap98 is not null) return Mode.Sector;
        if (board?.Mode == BoardMode.LapComparison && board.LapComparison is not null) return Mode.Laps;
        return Mode.Caption;
    }

    public void Draw(ThemeCanvas c, OverlayModel m, WidgetSettings cfg)
    {
        if (m.Now < _lastNow) { _key = long.MinValue; _comparisonLap = -1; }
        _lastNow = m.Now;
        var mode = Select(m);
        var b = m.Board;
        if (mode == Mode.Laps && b?.LapComparison is { } lc)
        {
            if (_comparisonLap != lc.PlayerLapsCompleted) { _comparisonLap = lc.PlayerLapsCompleted; _comparisonSince = m.Now; }
            if (m.Now - _comparisonSince >= 6) mode = Mode.Caption;
        }
        long key = mode switch
        {
            Mode.Tower => 10000 + b!.Tower!.PageIndex,
            Mode.Sector => 20000 + (long)Math.Round(b!.SectorGap98!.OpenT * 100),
            Mode.Laps => 30000 + b!.LapComparison!.PlayerLapsCompleted,
            _ => -(int)mode,
        };
        double eventStart = mode switch
        {
            Mode.Tower => b!.Tower!.PageStartT,
            Mode.Sector => b!.SectorGap98!.OpenT,
            Mode.Pit => BroadcastUi.State(m).PlayerStopStartT,
            Mode.Winner => BroadcastUi.State(m).Winner!.FinishedT,
            _ => m.Now,
        };
        if (_key != key)
        {
            _since = _key == long.MinValue && double.IsFinite(eventStart) ? eventStart : m.Now;
            _key = key;
        }
        float alpha = (float)Math.Clamp((m.Now - _since) / 0.16, 0, 1);
        if (mode == Mode.Caption)
        {
            var bc = BroadcastUi.State(m);
            double last = Math.Max(bc.SessionSeenT, Math.Max(bc.PlayerPositionChangedT, bc.PlayerLapChangedT));
            alpha *= BroadcastUi.Fade(m.Now - last, 6, 0.16, 0.2);
            if (cfg.ColumnVisible("always")) alpha = 1;
        }
        if (mode == Mode.Pit && !BroadcastUi.State(m).PlayerStopped)
            alpha *= (float)Math.Clamp((BroadcastUi.PitTimerHold - (m.Now - BroadcastUi.State(m).PlayerStopEndT)) / 0.2, 0, 1);
        if (mode == Mode.Winner)
            alpha *= (float)Math.Clamp((BroadcastUi.WinnerHold - (m.Now - BroadcastUi.State(m).Winner!.FinishedT)) / 0.2, 0, 1);
        BroadcastUi.WithAlpha(c, alpha, () =>
        {
            switch (mode)
            {
                case Mode.Tower: Tower(c, b!.Tower!, m.Now, cfg); break;
                case Mode.Sector: Sector(c, b!.SectorGap98!, cfg); break;
                case Mode.Laps: Laps(c, b!.LapComparison!, cfg); break;
                case Mode.Caption: Caption(c, m.Session!.PlayerCar!, m.Session.Cars, cfg); break;
                case Mode.Pit: Pit(c, m, cfg); break;
                case Mode.Winner: Winner(c, BroadcastUi.State(m).Winner!, m.Session!.Cars, cfg); break;
            }
        });
    }

    public static void Band(ThemeCanvas c, float y = 0, float h = Height)
        => c.FillRect(0, y, Width, h, new Color4(0.05f, 0.05f, 0.05f, 0.48f));

    static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
    static void Name(ThemeCanvas c, string s, float x, float y, float w, HAlign align = HAlign.Left, float size = 48, string element = "name")
        => c.Text(s, BroadcastUi.Fit(c, s, c.Theme.Text with { Element = element, Size = size }, w), x, y, w, Math.Max(62, size * 1.3f), c.Theme.TextColor, align, c.Theme.TextShadow);
    static void Number(ThemeCanvas c, string s, float x, float y, float w, float size = 52, HAlign align = HAlign.Left, string element = "value")
        => c.Text(s, BroadcastUi.Fit(c, s, c.Theme.Numbers with { Element = element, Size = size }, w), x, y, w, Math.Max(65, size * 1.3f), c.Theme.ValueColor, align, c.Theme.ValueShadow);

    void Tower(ThemeCanvas c, BoardTower tower, double now, WidgetSettings cfg)
    {
        Band(c);
        double age = now - tower.PageStartT;
        if (age < 0.6)
        {
            c.Text("CLASSIFICATION", c.Theme.Text with { Element = "title", Size = 28 }, 40, 10, 800, 40,
                new Color4(1f, 0.86f, 0.1f, 1f), HAlign.Left, c.Theme.TextShadow);
            return;
        }
        foreach (var row in tower.Entries)
        {
            double start = Math.Max(row.CrossedT, tower.PageStartT + 0.6 + (row.PageSlot - 1) * 0.45);
            float a = (float)Math.Clamp((now - start) / 0.12, 0, 1);
            BroadcastUi.WithAlpha(c, a, () =>
            {
                float x = 160 + row.Column * 810, y = 20 + row.Row * 62;
                Chrome.AccentBox(c, x, y, 54, 52, N(row.Position), c.Theme.Numbers with { Element = "position", Size = 46 });
                string name = cfg.Name(row.Name, row.CarIndex, row.ShortName).ToUpperInvariant();
                Name(c, name, x + 76, y - 4, cfg.Width("name", 480));
                string value = row.GapKind switch
                {
                    BoardGapKind.Leader => "LAP " + N(row.GapLaps),
                    BoardGapKind.Laps => cfg.Fmt.FormatLaps(row.GapLaps, false),
                    _ => cfg.Fmt.FormatGap(row.GapSeconds, false),
                };
                if (row.GapKind is BoardGapKind.Leader or BoardGapKind.Laps)
                    c.Text(value, c.Theme.Label with { Element = "gap", Size = 43 }, x + 555, y - 2, cfg.Width("gap", 210), 58, c.Theme.ValueColor, HAlign.Right, c.Theme.TextShadow);
                else Number(c, value, x + 520, y - 5, cfg.Width("gap", 245), 46, HAlign.Right, element: "gap");
            });
        }
        // A transmissão não acrescenta contador X/Y à tabela. A opção continua disponível.
        if (cfg.ColumnVisible("page") && tower.PageCount > 1)
            c.Text($"{tower.PageIndex + 1}/{tower.PageCount}", c.Theme.Label with { Size = 22 }, Width - 130, 268, 90, 26, c.Theme.LabelColor, HAlign.Right);
    }

    public static void Sector(ThemeCanvas c, BoardSectorGap split, WidgetSettings cfg)
    {
        Band(c, 95, 205);
        var front = split.NeighborAhead ? split.Neighbor : split.Player;
        var rear = split.NeighborAhead ? split.Player : split.Neighbor;
        Chrome.AccentBox(c, 130, 135, 140, 125, N(front.Position), c.Theme.Numbers with { Element = "position", Size = 100 });
        Chrome.AccentBox(c, 1650, 135, 140, 125, N(rear.Position), c.Theme.Numbers with { Element = "position", Size = 100 });
        Chrome.SplitBar(c, 292, 135, 380, 32, false);
        Chrome.SplitBar(c, 1248, 135, 380, 32, true);
        Name(c, cfg.Name(front.Name, front.CarIndex, front.ShortName).ToUpperInvariant(), 290, 182, 565, size: 49);
        Name(c, cfg.Name(rear.Name, rear.CarIndex, rear.ShortName).ToUpperInvariant(), 1065, 182, 565, HAlign.Right, 49);
        string gap = split.IsSplit ? cfg.Fmt.FormatGap(Math.Abs(split.GapSeconds), false) : Math.Abs(split.GapSeconds).ToString("0.0", CultureInfo.InvariantCulture);
        Number(c, gap, 775, 105, 370, 62, HAlign.Center, element: "gap");
    }

    public static void Laps(ThemeCanvas c, BoardLapComparison comparison, WidgetSettings cfg)
    {
        Band(c);
        var left = comparison.NeighborAhead ? comparison.Neighbor : comparison.Player;
        var right = comparison.NeighborAhead ? comparison.Player : comparison.Neighbor;
        Chrome.AccentBox(c, 125, 82, 135, 128, N(left.Position), c.Theme.Numbers with { Element = "position", Size = 100 });
        Chrome.AccentBox(c, 1660, 82, 135, 128, N(right.Position), c.Theme.Numbers with { Element = "position", Size = 100 });
        Name(c, cfg.Name(left.Name, left.CarIndex, left.ShortName).ToUpperInvariant(), 292, 12, 565);
        Name(c, cfg.Name(right.Name, right.CarIndex, right.ShortName).ToUpperInvariant(), 1065, 12, 565, HAlign.Right);
        for (int i = 0; i < comparison.Laps.Count && i < 3; i++)
        {
            var row = comparison.Laps[i];
            double? lt = comparison.NeighborAhead ? row.NeighborTime : row.PlayerTime;
            double? rt = comparison.NeighborAhead ? row.PlayerTime : row.NeighborTime;
            float y = 72 + i * 60;
            Number(c, lt is { } l ? cfg.Fmt.FormatLapTime(l) : "--", 292, y, cfg.Width("time", 410), 52, element: "time");
            Number(c, rt is { } r ? cfg.Fmt.FormatLapTime(r) : "--", 1200, y, cfg.Width("time", 430), 52, HAlign.Right, element: "time");
            Name(c, "LAP " + N(row.Lap), 810, y, 300, HAlign.Center, 44, element: "label");
        }
    }

    public static void Caption(ThemeCanvas c, CarSnapshot car, IReadOnlyList<CarSnapshot> field, WidgetSettings cfg)
    {
        Band(c, 80, 220);
        Chrome.Bubble(c, 165, 116, 88, 50, CaptionPlate.CarNumber(car), c.Theme.Numbers with { Size = 43 });
        Name(c, cfg.Name(car, BroadcastUi.ShortName(car, field)).ToUpperInvariant(), 285, 110, 900, size: 50);
        if (cfg.ColumnVisible("tyre")) Chrome.TyreEmblem(c, 210, 219, 27, car.TyreSupplier, c.Theme.Text with { Size = 37 });
        if (cfg.Id == "board" || cfg.ColumnVisible("team"))
            c.Text(BroadcastUi.Team(car).ToUpperInvariant(), BroadcastUi.Fit(c, BroadcastUi.Team(car), c.Theme.Label with { Element = "name", Size = 46 }, 900), 285, 190, 900, 60, c.Theme.LabelColor, shadow: c.Theme.TextShadow);
        Chrome.AccentBox(c, 1410, 110, 140, 135, N(car.Position), c.Theme.Numbers with { Element = "position", Size = 105 });
        // Legenda ciano (patrocinador na transmissão real) omitida: o SDK do AMS2 não fornece o dado.
    }

    public static void Pit(ThemeCanvas c, OverlayModel m, WidgetSettings cfg)
    {
        if (m.Session?.PlayerCar is not { } car) return;
        Band(c, 80, 220);
        float nameWidth = Math.Min(1500, cfg.Width("name", 720));
        Name(c, cfg.Name(car, BroadcastUi.ShortName(car, m.Session.Cars)).ToUpperInvariant(), 1700 - nameWidth, 92, nameWidth, HAlign.Right, 52);
        c.Text("PIT STOP", c.Theme.Label with { Size = 52, Tracking = 6 }, 785, 175, 440, 65, c.Theme.ValueColor, shadow: c.Theme.TextShadow);
        Number(c, BroadcastUi.StopTime(BroadcastUi.State(m).PlayerStopNow(m.Now)), 1260, 160, 440, 80, HAlign.Right, element: "time");
    }

    public static void Winner(ThemeCanvas c, WinnerInfo winner, IReadOnlyList<CarSnapshot> field, WidgetSettings cfg)
    {
        // Mesmo vocabulário da legenda, sem patrocinador ou estatísticas que não aparecem neste trecho.
        Band(c, 80, 220);
        var car = winner.Car;
        Chrome.Bubble(c, 165, 116, 88, 50, CaptionPlate.CarNumber(car), c.Theme.Numbers with { Size = 43 });
        string fullName = cfg.Fmt.Name is null && cfg.Fmt.CarNumber != true ? car.Name : cfg.Name(car, BroadcastUi.ShortName(car, field));
        if (cfg.Fmt.Name is null && cfg.Fmt.CarNumber != true)
        {
            int space = fullName.LastIndexOf(' ');
            fullName = space < 0 ? fullName.ToUpperInvariant() : fullName[..(space + 1)] + fullName[(space + 1)..].ToUpperInvariant();
        }
        Name(c, fullName, 285, 110, 820, size: 48);
        Chrome.TyreEmblem(c, 210, 219, 27, car.TyreSupplier, c.Theme.Text with { Size = 37 });
        if (cfg.Id == "board" || cfg.ColumnVisible("team")) c.Text(BroadcastUi.Team(car).ToUpperInvariant(), c.Theme.Label with { Element = "name", Size = 46 }, 285, 190, 800, 60, c.Theme.LabelColor, shadow: c.Theme.TextShadow);
        if (cfg.Id == "board" || cfg.ColumnVisible("stats"))
        {
            // Estatística amarela: tempo total, média e distância (todos fornecidos pelo WinnerInfo).
            var su = cfg.Fmt.SpeedOrDefault;
            string stats = BroadcastUi.RaceTime(winner.TotalSeconds)
                + "   " + DisplayFormat.SpeedFromKph(winner.AvgKmh, su).ToString("0.000", CultureInfo.InvariantCulture) + (su == SpeedUnit.Mph ? " Mph" : " Km/h")
                + "   " + DisplayFormat.Distance(winner.DistanceKm, su).ToString("0.000", CultureInfo.InvariantCulture) + " " + DisplayFormat.DistanceLabel(su);
            c.Text(stats, BroadcastUi.Fit(c, stats, c.Theme.Label with { Element = "value", Size = 36 }, 820), 285, 245, 820, 48,
                new Color4(1f, 0.86f, 0.1f, 1f), shadow: c.Theme.TextShadow);
        }
        Chrome.Checkered(c, 1135, 103, 130, 135);
        Name(c, "WINNER", 1300, 105, 520, size: 104, element: "title");
    }
}
