using System.Globalization;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Qualification caption: classification rank and gap to the current leader, with explicit missing timing.</summary>
public static class QualiCaption
{
    public static string Gap(OverlayModel m, WidgetSettings cfg)
        => m.Quali?.Player?.GapToFirst is { } gap ? cfg.Fmt.FormatGap(gap) : "NO TIME";

    public static void Draw(ThemeCanvas c, OverlayModel m, WidgetSettings cfg, float width)
    {
        if (m.Session?.PlayerCar is not { } car) return;
        int rank = m.Quali?.Player?.Rank ?? car.Position;
        string gap = Gap(m, cfg);
        var t = c.Theme;
        if (t.Style == ThemeStyle.Broadcast98)
        {
            Broadcast98RaceBoard.Band(c, 80, 220);
            Chrome.Bubble(c, 165, 116, 88, 50, CaptionPlate.CarNumber(car), t.Numbers with { Size = 43 });
            string name98 = cfg.Name(car, BroadcastUi.ShortName(car, m.Session.Cars)).ToUpperInvariant();
            c.Text(name98, BroadcastUi.Fit(c, name98, t.Text with { Element = "name", Size = 50 }, 1050), 285, 110, 1050, 65, t.TextColor, shadow: t.TextShadow);
            if (cfg.ColumnVisible("team"))
            {
                string team = BroadcastUi.Team(car).ToUpperInvariant();
                c.Text(team, BroadcastUi.Fit(c, team, t.Label with { Element = "name", Size = 46 }, 650), 285, 190, 650, 60, t.LabelColor, shadow: t.TextShadow);
            }
            if (cfg.ColumnVisible("tyre")) Chrome.TyreEmblem(c, 210, 219, 27, car.TyreSupplier, t.Text with { Size = 37 });
            Chrome.AccentBox(c, 1410, 110, 140, 135, rank > 0 ? rank.ToString(CultureInfo.InvariantCulture) : "-", t.Numbers with { Element = "position", Size = 105 });
            c.FillRect(1265, 15, 520, 65, new Color4(.03f, .59f, .68f, .97f));
            c.Text("QUALIFYING", t.Text with { Element = "label", Size = 43 }, 1265, 15, 520, 65, t.TextColor, HAlign.Center, t.TextShadow);
            c.Text(gap, t.Numbers with { Element = "gap", Size = 46 }, 1000, 190, 390, 60, t.ValueColor, HAlign.Right, t.ValueShadow);
            return;
        }
        string name = cfg.Name(car, BroadcastUi.ShortName(car, m.Session.Cars));
        if (t.Style == ThemeStyle.Broadcast2000s)
        {
            float left = width - 104;
            Chrome.HeaderCell(c, 4, 4, left, 26, "QUALIFYING", t.Label);
            Chrome.WhiteCell(c, 4, 30, left, 30, name, BroadcastUi.Fit(c, name, t.Text, left - 16));
            Chrome.BlackCell(c, 4, 60, left, 30, gap, t.Numbers with { Element = "gap" }, HAlign.Right);
            if (!Chrome.TyreBox(c, 4 + left, 30, 40, 60, cfg.ColumnVisible("tyre") ? car.TyreSupplier : "", t.Text with { Size = 22 }))
                Chrome.Box(c, 4 + left, 30, 40, 60, "", t.Text, Chrome.CellKind.Navy);
            Chrome.Box(c, 44 + left, 30, 56, 60, rank > 0 ? rank.ToString(CultureInfo.InvariantCulture) : "-",
                t.Numbers with { Element = "position", Size = 38 }, rank == 1 ? Chrome.CellKind.Red : Chrome.CellKind.Navy, HAlign.Center, 0);
            return;
        }
        string full = cfg.Fmt.Name is null && cfg.Fmt.CarNumber != true ? car.Name : name;
        CaptionPlate.Plate18(c, 0, 0, width, 94, rank, full, CaptionPlate.CarNumber(car), "", bigBox: false,
            tick: CaptionPlate.ClassColor(car, m.Session.Cars));
        c.Text(gap, t.Numbers with { Element = "gap", Size = 28 }, 88, 50, width - 102, 32, t.ValueColor, HAlign.Right);
    }
}
