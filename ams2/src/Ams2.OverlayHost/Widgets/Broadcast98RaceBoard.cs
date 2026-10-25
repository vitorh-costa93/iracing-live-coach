using System.Globalization;
using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Rodapé de transmissão 1999 (ID de perfil 1998): um único painel e evento por quadro.</summary>
public sealed class Broadcast98RaceBoard
{
    public const float Width = 1920, Height = 300;
    public enum Mode { None, Caption, Laps, Sector, Tower, Pit, Winner, Fastest, Remaining }

    public static Mode Select(OverlayModel m)
    {
        if (!m.Connected || !m.PlayerDriving || m.Session is not { InSession: true, GameState: 2, PlayerCar: not null }) return Mode.None;
        return m.Board?.Race99?.Mode switch
        {
            Broadcast99RaceMode.Pit => Mode.Pit,
            Broadcast99RaceMode.Fastest => Mode.Fastest,
            Broadcast99RaceMode.Table => Mode.Tower,
            Broadcast99RaceMode.Gap => Mode.Sector,
            Broadcast99RaceMode.Remaining => Mode.Remaining,
            Broadcast99RaceMode.Caption => Mode.Caption,
            _ => Mode.None,
        };
    }

    readonly ShowRef _show = new();

    /// <summary>Exibicao do Race Board 1999 (perfil): duracoes e pecas ligadas; fora do 1999 ou sem board = padrao.</summary>
    public static Ams2.Core.Calc.Race99Options CoreOptions(WidgetSettings? board, string themeId)
    {
        if (board is null || !WidgetCatalog.Is99(themeId)) return Ams2.Core.Calc.Race99Options.Default;
        var p = ShowPlan.For(board, themeId);
        var d = Ams2.Core.Calc.Race99Options.Default;
        return new()
        {
            FastestHold = p.PieceDuration("fastest", d.FastestHold), GapHold = p.PieceDuration("gap", d.GapHold),
            RemainingHold = p.PieceDuration("remaining", d.RemainingHold), PageHold = p.PieceDuration("table", 4) is var ph && ph != 4 ? ph : null,
            Pit = p.PieceEnabled("pit"), Fastest = p.PieceEnabled("fastest"), Table = p.PieceEnabled("table"), Gap = p.PieceEnabled("gap"), Remaining = p.PieceEnabled("remaining"),
        };
    }

    /// <summary>Peca do Exibicao (ShowCatalog "board" 1999) que desenha o modo; as demais sao agendadas no Core (Race99Options).</summary>
    public static string PieceOf(Mode mode) => mode switch
    {
        Mode.Pit => "pit", Mode.Fastest => "fastest", Mode.Tower => "table", Mode.Sector => "gap", Mode.Remaining => "remaining", _ => "caption",
    };

    public void Draw(ThemeCanvas c, OverlayModel m, WidgetSettings cfg)
    {
        var state = m.Board?.Race99;
        if (state is null) return;
        var mode = Select(m);
        if (mode == Mode.None) return;
        float alpha = _show.Get(cfg, ThemeStyle.Broadcast98).At(m.Now, id => m.Events?.Last(id)).PieceAlpha(PieceOf(mode));
        if (alpha <= 0.01f) return;
        BroadcastUi.WithAlpha(c, alpha, () => DrawMode(c, m, cfg, state, mode));
    }

    void DrawMode(ThemeCanvas c, OverlayModel m, WidgetSettings cfg, Broadcast99RaceState state, Mode mode)
    {
        switch (mode)
        {
            case Mode.Tower: Table99(c, state, m.Now, cfg); break;
            case Mode.Sector when state.Gap is { } gap: Sector(c, gap, cfg); break;
            case Mode.Fastest when state.Fastest is { } fastest: Fastest99(c, fastest, m.Session!.Cars, cfg); break;
            case Mode.Remaining: Remaining99(c, state, cfg); break;
            case Mode.Pit: Pit(c, m, cfg); break;
            default: Caption(c, m.Session!.PlayerCar!, m.Session.Cars, cfg); break;
        }
    }

    public static void Band(ThemeCanvas c, float y = 0, float h = Height)
        => c.FillRect(0, y, Width, h, new Color4(0.05f, 0.05f, 0.05f, 0.48f));

    static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
    static void Name(ThemeCanvas c, string s, float x, float y, float w, HAlign align = HAlign.Left, float size = 48, string element = "name")
        => c.Text(s, BroadcastUi.Fit(c, s, c.Theme.Text with { Element = element, Size = size, Weight = 600 }, w), x, y, w, Math.Max(62, size * 1.3f), c.Theme.TextColor, align, c.Theme.TextShadow);
    static void Number(ThemeCanvas c, string s, float x, float y, float w, float size = 52, HAlign align = HAlign.Left, string element = "value")
        => c.Text(s, BroadcastUi.Fit(c, s, c.Theme.Numbers with { Element = element, Size = size, Weight = 400 }, w), x, y, w, Math.Max(65, size * 1.3f), c.Theme.ValueColor, align, c.Theme.ValueShadow);

    static readonly Color4 Cyan = new(0.08f, 0.76f, 0.78f, 1);
    static readonly Color4 Yellow = new(0.92f, 0.94f, 0.22f, 1);

    static void Table99(ThemeCanvas c, Broadcast99RaceState state, double now, WidgetSettings cfg)
    {
        Band(c);
        // Eight entries per page, four rows in each column; no permanent standings/title.
        int page = Math.Max(0, (int)(state.ShownSeconds / state.PageHoldSeconds));
        int half = (state.PageSize + 1) / 2;
        foreach (var (row, i) in state.Rows.Skip(page * state.PageSize).Take(state.PageSize).Select((r, i) => (r, i)))
        {
            float x = 155 + i / half * 810, y = 20 + i % half * Math.Min(62, 260f / half);
            Chrome.AccentBox(c, x, y, 54, 52, N(row.Driver.Position), c.Theme.Numbers with { Element = "position", Size = 46, Weight = 400 });
            Name(c, cfg.Name(row.Driver.Name, row.Driver.CarIndex, row.Driver.ShortName).ToUpperInvariant(), x + 76, y - 4, 480);
            string value = row.Driver.Position == 1 ? "LAP " + N(state.Lap)
                : row.LapsBehind > 0 ? cfg.Fmt.FormatLaps(row.LapsBehind, false)
                : row.GapSeconds is { } gap ? cfg.Fmt.FormatGap(Math.Abs(gap), false) : "--";
            c.Text(value, c.Theme.Numbers with { Element = "gap", Size = 46, Weight = 400 }, x + 520, y - 4, 245, 62, Yellow, HAlign.Right, c.Theme.ValueShadow);
        }
    }

    public static void Fastest99(ThemeCanvas c, FastestLapEvent fastest, IReadOnlyList<CarSnapshot> field, WidgetSettings cfg)
    {
        Caption(c, fastest.Car, field, cfg);
        c.Text("LAP", c.Theme.Label with { Size = 51, Weight = 600 }, 1150, 101, 190, 65, Yellow, shadow: c.Theme.TextShadow);
        c.Text("FASTEST LAP", c.Theme.Text with { Element = "title", Size = 43, Weight = 600 }, 1360, 113, 510, 60, Cyan, shadow: c.Theme.TextShadow);
        c.Text(fastest.Car.LapsCompleted > 0 ? N(fastest.Car.LapsCompleted) : "--", c.Theme.Numbers with { Size = 68, Weight = 400 }, 1150, 174, 160, 90, Yellow, HAlign.Center, c.Theme.ValueShadow);
        c.Text(cfg.Fmt.FormatLapTime(fastest.LapTime), c.Theme.Numbers with { Element = "time", Size = 61, Weight = 400 }, 1350, 180, 510, 90, Yellow, HAlign.Right, c.Theme.ValueShadow);
        c.FillRect(1070, 10, 780, 72, new Color4(0, 0, 0, 1));
        c.Sponsor("tagheuer_shield", 1090, 18, 70, 56);
        c.Text("TAGHeuer  TIMING", c.Theme.Text with { Size = 47, Weight = 600 }, 1180, 14, 650, 64, new Color4(1, 1, 1, 1));
    }

    public static void Remaining99(ThemeCanvas c, Broadcast99RaceState state, WidgetSettings cfg)
    {
        Band(c, 100, 200);
        c.FillRect(1060, 20, 750, 72, new Color4(0, .61f, .73f, 1));
        c.Text("SIEMENS", c.Theme.Text with { Size = 49, Weight = 600 }, 1070, 23, 730, 65, new Color4(1, 1, 1, 1), HAlign.Center, c.Theme.TextShadow);
        c.Text(N(state.LapsRemaining ?? 0) + " LAPS REMAINING", c.Theme.Text with { Element = "title", Size = 55, Tracking = 4, Weight = 600 }, 450, 105, 1300, 80, Yellow, HAlign.Center, c.Theme.TextShadow);
        var leader = state.Leader;
        Name(c, "Leader - " + (leader is null ? "--" : cfg.Name(leader.Name, leader.CarIndex, leader.ShortName).ToUpperInvariant()), 530, 206, 1200, HAlign.Center, 50);
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
        Name(c, cfg.Name(front.Name, front.CarIndex, front.ShortName).ToUpperInvariant(), 290, 182, 450, size: 49);
        Name(c, cfg.Name(rear.Name, rear.CarIndex, rear.ShortName).ToUpperInvariant(), 1210, 182, 420, HAlign.Right, 49);
        string gap = split.IsSplit ? cfg.Fmt.FormatGap(Math.Abs(split.GapSeconds), false) : Math.Abs(split.GapSeconds).ToString("0.0", CultureInfo.InvariantCulture);
        c.Text(gap, c.Theme.Numbers with { Element = "gap", Size = 62, Weight = 400 }, 775, 105, 370, 80, Yellow, HAlign.Center, c.Theme.ValueShadow);
        if (split.PreviousGapSeconds is { } previous)
            c.Text("(" + cfg.Fmt.FormatGap(Math.Abs(previous), false) + ")", c.Theme.Numbers with { Element = "gap", Size = 49, Weight = 400 }, 750, 188, 420, 65, Yellow, HAlign.Center, c.Theme.ValueShadow);
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
        Chrome.Bubble(c, 165, 116, 88, 50, N(car.Position), c.Theme.Numbers with { Element = "position", Size = 43, Weight = 400 });
        Name(c, cfg.Name(car, BroadcastUi.ShortName(car, field)).ToUpperInvariant(), 285, 110, 800, size: 50);
        if (cfg.ColumnVisible("tyre")) Chrome.TyreEmblem(c, 210, 219, 27, car.TyreSupplier, c.Theme.Text with { Size = 37 });
        if (cfg.Id == "board" || cfg.ColumnVisible("team"))
            c.Text(BroadcastUi.Team(car).ToUpperInvariant(), BroadcastUi.Fit(c, BroadcastUi.Team(car), c.Theme.Label with { Element = "name", Size = 46 }, 900), 285, 190, 900, 60, Cyan, shadow: c.Theme.TextShadow);
    }

    public static void Pit(ThemeCanvas c, OverlayModel m, WidgetSettings cfg)
    {
        if (m.Session?.PlayerCar is not { } car) return;
        Band(c, 80, 220);
        float nameWidth = Math.Min(1500, cfg.Width("name", 720));
        Name(c, cfg.Name(car, BroadcastUi.ShortName(car, m.Session.Cars)).ToUpperInvariant(), 1700 - nameWidth, 92, nameWidth, HAlign.Right, 52);
        c.Text("PIT STOP", c.Theme.Label with { Size = 52, Tracking = 6 }, 785, 175, 440, 65, Yellow, shadow: c.Theme.TextShadow);
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
