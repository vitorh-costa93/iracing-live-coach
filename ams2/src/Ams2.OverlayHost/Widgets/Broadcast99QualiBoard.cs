using System.Globalization;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>1999 qualifying: pit-exit caption, live clock, intermediate comparison, finish result.</summary>
public sealed class Broadcast99QualiBoard
{
    public const float Width = 1920, Height = 300;
    /// <summary>
    /// Pecas (Exibicao): "result" e "split" com tempo de tela proprio e liga/desliga. <paramref name="show"/> null = plano do 1999
    /// (o 1993 passa o seu). Desligada, a peca tem tempo 0 e o board segue para a seguinte.
    /// </summary>
    public static QualiBoard99Presentation.View Resolve(OverlayModel m, WidgetSettings cfg, ShowPlan? show = null)
    {
        if (!Broadcast93RaceBoard.Visible(m)) return new(QualiBoard99Stage.Hidden, null);
        bool personal = string.Equals(cfg.OptionOr("compareTo", "leader"), "personal", StringComparison.OrdinalIgnoreCase);
        show ??= ShowPlan.For(cfg, "f1-1998");
        double hold = show.PieceEnabled("result") ? show.PieceDuration("result", QualiBoard99Presentation.ResultHold) : 0;
        double split = show.PieceEnabled("split") ? show.PieceDuration("split", QualiBoard99Presentation.SplitHold) : 0;
        return QualiBoard99Presentation.Resolve(m.QualiLap, m.Now, personal, hold, split);
    }

    readonly ShowRef _show = new();

    public void Draw(ThemeCanvas c, OverlayModel m, WidgetSettings cfg)
    {
        var show = _show.Get(cfg, ThemeStyle.Broadcast98).At(m.Now, id => m.Events?.Last(id));
        var v = Resolve(m, cfg, show);
        if (v.Stage == QualiBoard99Stage.Hidden || m.Session?.PlayerCar is not { } car) return;
        float alpha = v.Stage switch { QualiBoard99Stage.Finish => show.PieceAlpha("result"), QualiBoard99Stage.Split => show.PieceAlpha("split"), _ => 1f };
        BroadcastUi.WithAlpha(c, alpha, () => DrawStage(c, m, cfg, v, car));
    }

    void DrawStage(ThemeCanvas c, OverlayModel m, WidgetSettings cfg, QualiBoard99Presentation.View v, Ams2.Core.CarSnapshot car)
    {
        var t = c.Theme;
        const float left = 150, right = 1260, column = 520;
        var white = t.TextColor; var yellow = t.ValueColor; var cyan = t.LabelColor;
        void Text(string text, float x, float y, float w, float size, Color4 color, bool number = false,
            HAlign align = HAlign.Left, string element = "name")
        {
            var font = (number ? t.Numbers : t.Text) with { Size = size, Weight = number ? 400 : 600, Element = element };
            c.Text(text, BroadcastUi.Fit(c, text, font, w), x, y, w, size * 1.4f, color, align,
                number ? t.ValueShadow : t.TextShadow);
        }
        string Name(Ams2.Core.CarSnapshot driver) => cfg.Name(driver, BroadcastUi.ShortName(driver, m.Session.Cars)).ToUpperInvariant();
        string Exact(double seconds) => seconds < 60 && cfg.Fmt.LapTime is null
            ? seconds.ToString("0.000", CultureInfo.InvariantCulture) : cfg.Fmt.FormatLapTime(seconds);
        void Speed(double? speed, float x, bool unit)
        {
            if (speed is not { } value || !double.IsFinite(value) || value <= 0 || cfg.OptionOr("showSpeed", "true") == "false") return;
            var speedUnit = cfg.Fmt.SpeedOrDefault;
            string text = DisplayFormat.SpeedFromKph(value * DisplayFormat.MpsToKph, speedUnit).ToString("0.0", CultureInfo.InvariantCulture);
            if (unit) text += speedUnit == SpeedUnit.Mph ? " MPH" : " KPH";
            Text(text, x, 222, column, 39, yellow, true, element: "value");
        }
        if (v.Stage == QualiBoard99Stage.OutLap)
        {
            Broadcast98RaceBoard.Band(c, 105, 170);
            Chrome.Bubble(c, 145, 140, 82, 50, CaptionPlate.CarNumber(car), t.Numbers with { Size = 43 });
            Text(Name(car), 260, 120, 740, 48, white);
            if (cfg.ColumnVisible("team")) Text(BroadcastUi.Team(car).ToUpperInvariant(), 260, 200, 740, 44, cyan);
            if (cfg.ColumnVisible("tyre")) Chrome.TyreEmblem(c, 187, 233, 27, car.TyreSupplier, t.Text with { Size = 37 });
            Text("FIRST LAP OUT OF PITS", 1050, 120, 760, 44, yellow, element: "label");
            return;
        }
        bool comparison = v.Stage is QualiBoard99Stage.Approach or QualiBoard99Stage.Split or QualiBoard99Stage.Finish;
        bool crossed = v.Stage is QualiBoard99Stage.Split or QualiBoard99Stage.Finish;
        Broadcast98RaceBoard.Band(c, 68, 232);
        Text(Name(car), right, 80, column, 44, white);
        if (v.Time is { } clock)
            Text(crossed ? Exact(clock) : QualiTowerWidget.Tenths(clock), right, 142, column, 56, yellow, true, element: "time");
        if (!comparison) return;
        if (v.Stage == QualiBoard99Stage.Finish)
        {
            if (v.Position > 0)
            {
                c.FillRect(330, 106, 150, 150, t.AccentFill);
                Text(v.Position.ToString(CultureInfo.InvariantCulture), 330, 91, 150, 115, t.AccentInk, true, HAlign.Center, "position");
            }
        }
        else
        {
            var referenceCar = v.ReferenceCar ?? m.Session.Cars.FirstOrDefault(driver => driver.Index ==
                (cfg.OptionOr("compareTo", "leader") == "personal" ? car.Index : m.QualiLap!.LeaderIndex));
            if (referenceCar is not null) Text(Name(referenceCar), left, 80, 560, 44, white);
            if (v.ReferenceTime is { } reference) Text(Exact(reference), left, 142, 560, 56, yellow, true, element: "time");
            Speed(v.ReferenceSpeedMps, left, true);
        }
        Text(v.Mark == 3 ? "FINISH LINE" : "INTERMEDIATE " + v.Mark.ToString(CultureInfo.InvariantCulture),
            710, 222, 500, 38, yellow, align: HAlign.Center, element: "label");
        if (!crossed) return;
        if (v.Invalid) Text("INVALID", 710, 142, 500, 46, cyan, align: HAlign.Center, element: "label");
        else if (v.Delta is { } delta) Text(cfg.Fmt.FormatGap(delta), 710, 142, 500, 56, cyan, true, HAlign.Center, "gap");
        Speed(v.PlayerSpeedMps, right, false);
        // The existing sponsor asset provides the genuine shield; text follows the reference black strip.
        c.FillRect(1065, 0, 745, 68, new Color4(0, 0, 0, 1));
        c.Sponsor("tagheuer_shield", 1080, 6, 60, 56);
        Text("TAGHEUER", 1148, 0, 435, 45, white, element: "label");
        Text("TIMING", 1580, 10, 213, 34, white, element: "label");
    }
}
