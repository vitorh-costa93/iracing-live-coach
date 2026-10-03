using System.Globalization;
using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Lista de paradas da transmissao: grade de 2 colunas x ate 4 linhas ([posicao][nome][N Stops]), em ordem de posicao, em
/// blocos de 8 pilotos; mostra o bloco do ultimo piloto que entrou nos boxes. Aparece ~8 s apos uma entrada nos boxes (coluna "always" = fixa).
/// </summary>
public sealed class PitStopsWidget : IWidget
{
    public string Id => "pitstops";
    public int Rows { get; set; } = 4;
    WidgetSettings _cfg = new() { Id = "pitstops" };
    public void Configure(WidgetSettings s) { _cfg = s; Rows = s.Rows ?? 4; }
    public (float Width, float Height) DesignSize => (X0 * 2 + ColW * 2 + ColGap, Y0 * 2 + HeadH + Rows * Pitch + 2);

    const float X0 = 4, Y0 = 4, PosW = 36, NameW = 190, StopsW = 110, ColW = PosW + NameW + StopsW, ColGap = 36, HeadH = 26, RowH = 28, Pitch = 30;

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        if (!m.Connected || m.Session is not { } s) return;
        var b = BroadcastUi.State(m);
        float alpha = _cfg.ColumnVisible("always") ? 1f : BroadcastUi.Fade(m.Now - b.LastPitEntryT, BroadcastUi.PitListHold);
        if (alpha <= 0.01f) return;
        var cars = s.Cars.Where(x => x.Position > 0).OrderBy(x => x.Position).ToList();
        if (cars.Count == 0) return;
        int block = Rows * 2, start = 0;
        int li = b.LastPitCarIndex >= 0 ? cars.FindIndex(x => x.Index == b.LastPitCarIndex) : -1;
        if (li >= 0) start = li / block * block;
        var page = cars.Skip(start).Take(block).ToList();
        BroadcastUi.WithAlpha(c, alpha, () => DrawGrid(c, page, cars, b));
    }

    void DrawGrid(ThemeCanvas c, List<CarSnapshot> page, List<CarSnapshot> field, BroadcastState b)
    {
        var t = c.Theme;
        bool b04 = t.Style == ThemeStyle.Broadcast2000s;
        var (w, h) = DesignSize;
        bool b98 = t.Style == ThemeStyle.Broadcast98;
        if (b98)
        {
            // Faixa translucida + selo ciano no canto (no lugar do patrocinador da transmissao); linhas em caixa-alta.
            c.Panel(0, 0, w, h);
            c.FillRect(w - 154, 0, 150, 24, new Vortice.Win32.Numerics.Color4(21 / 255f, 150 / 255f, 176 / 255f, 0.97f));
            c.Text("PIT STOPS", t.Label with { Size = 20 }, w - 154, -1, 150, 24, new Vortice.Win32.Numerics.Color4(1, 1, 1, 1), HAlign.Center, t.TextShadow);
        }
        else if (!b04) { c.Panel(0, 0, w, h); Chrome.Header(c, "PIT STOPS", 16, 2, 200, underline: false); }
        else Chrome.HeaderCell(c, X0 + PosW, Y0, NameW, HeadH, "PIT STOPS", t.Label);
        for (int i = 0; i < page.Count; i++)
        {
            var car = page[i];
            int col = i / Rows, row = i % Rows;
            float x = X0 + col * (ColW + ColGap), y = Y0 + HeadH + row * Pitch;
            string name = BroadcastUi.ShortName(car, field), stops = BroadcastUi.Stops(b.StopsOf(car.Index));
            if (b98) { name = name.ToUpperInvariant(); stops = stops.ToUpperInvariant(); }
            if (b04)
            {
                Chrome.PositionBox(c, x, y, PosW, RowH, car.Position, t.Text);
                Chrome.WhiteCell(c, x + PosW, y, NameW, RowH, name, BroadcastUi.Fit(c, name, t.Text, NameW - 14), ink: car.IsPlayer ? Chrome.PlayerInk : null);
                Chrome.BlackCell(c, x + PosW + NameW, y, StopsW, RowH, stops, t.Text, HAlign.Right);
            }
            else
            {
                Chrome.AccentBox(c, x + 12, y + 1, PosW, RowH, car.Position.ToString(CultureInfo.InvariantCulture), t.Text);
                c.Text(name, BroadcastUi.Fit(c, name, t.Text, NameW - 42), x + PosW + 22, y, NameW - 8, RowH, car.IsPlayer ? t.PlayerColor : t.TextColor, shadow: t.TextShadow);
                c.Text(stops, t.Label, x + PosW + NameW - 4, y, StopsW, RowH, t.ValueColor, HAlign.Right, t.TextShadow);
            }
        }
    }
}
