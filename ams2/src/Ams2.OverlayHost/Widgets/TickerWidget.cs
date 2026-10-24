using System.Globalization;
using Ams2.Core;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Faixa inferior de classificação do gráfico de TV 2012 (ref. GP da Malásia 2012): cinco células inclinadas lado a lado
/// ([posição][SIGLA][intervalo]), a do líder em vermelho com "Interval"/"Leader", e a página muda a cada <c>pageSeconds</c> (1–5, 6–10, 11–15...).
/// Piloto fora da corrida mostra "OUT" e quem está na pit lane "PIT". Exclusivo do tema f1-2012; ocupa o lugar do Board (que fica desligado).
/// Opções: mode (interval = carro da frente / gap = líder), pageSeconds.
/// </summary>
public sealed class TickerWidget : IWidget
{
    public string Id => "ticker";
    public const float W = 1600, H = 40, CellW = 312, Pitch = 322;
    const int PerPage = 5;
    public (float Width, float Height) DesignSize => (W, H);
    WidgetSettings _cfg = new() { Id = "ticker" };
    readonly FieldCodes _codes = new();
    public void Configure(WidgetSettings s) => _cfg = s;

    bool IntervalMode => !string.Equals(_cfg.OptionOr("mode", "interval"), "gap", StringComparison.OrdinalIgnoreCase);
    readonly ShowRef _show = new();
    /// <summary>Exibição (timing "page"; lê também a opção antiga pageSeconds): segundos por página, 3–15, padrão 6.</summary>
    double PageSeconds => _show.Get(_cfg, ThemeStyle.Broadcast2000s).Timing("page", 6);

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        if (!m.Connected || m.Standings.Count == 0) return;
        var rows = m.Standings;
        _codes.Update(rows.Select(r => r.Car));
        int pages = (rows.Count + PerPage - 1) / PerPage;
        double now = Math.Max(0, m.Now), period = PageSeconds;
        int page = pages <= 1 ? 0 : (int)(Math.Floor(now / period) % pages);
        // Entrada suave da pagina (0,25 s); a troca nao tem outra animacao.
        float alpha = (float)Math.Clamp((now % period) / 0.25, 0, 1);
        BroadcastUi.WithAlpha(c, Math.Max(alpha, 0.02f), () =>
        {
            for (int i = 0; i < PerPage; i++)
            {
                int idx = page * PerPage + i;
                if (idx >= rows.Count) break;
                Cell(c, rows, idx, i * Pitch);
            }
        });
    }

    void Cell(ThemeCanvas c, IReadOnlyList<StandingRow> rows, int idx, float x)
    {
        var t = c.Theme;
        var r = rows[idx];
        var car = r.Car;
        bool leader = idx == 0;
        Chrome.Box(c, x, 0, CellW, H, "", t.Text, leader ? Chrome.CellKind.Red : Chrome.CellKind.Black);
        string pos = car.Position.ToString(CultureInfo.InvariantCulture);
        var white = new Color4(1, 1, 1, 1);
        if (leader) c.Text(pos, t.Numbers, x + 6, -1, 44, H, white, HAlign.Center);
        else Chrome.Box(c, x, 0, 46, H, pos, t.Numbers, Chrome.CellKind.Gray, HAlign.Center, 0);
        string nm = _cfg.Name(car, _codes);
        c.Text(nm, BroadcastUi.Fit(c, nm, t.Text, 110), x + 62, -1, 120, H, r.IsPlayer ? t.PlayerColor : white);
        c.Text(Value(rows, idx), t.Numbers with { Weight = 500 }, x + 170, -1, CellW - 170 - 18, H, white, HAlign.Right);
    }

    string Value(IReadOnlyList<StandingRow> rows, int idx)
    {
        var r = rows[idx];
        var car = r.Car;
        if (car.RaceState is RaceState.Retired or RaceState.Dnf or RaceState.Disqualified) return "OUT";
        if (idx == 0) return IntervalMode ? "Interval" : "Leader";
        if (car.InPitLane) return "PIT";
        var f = _cfg.Fmt;
        if (r.LapsBehind > 0) return f.FormatLaps(r.LapsBehind, true);
        if (r.GapToLeader is not { } g) return f.NoGap;
        if (!IntervalMode) return f.FormatGap(g, true);
        var ahead = rows[idx - 1];
        double prev = idx - 1 == 0 ? 0 : ahead.GapToLeader ?? double.NaN;
        return double.IsNaN(prev) || ahead.LapsBehind > 0 ? f.FormatGap(g, true) : f.FormatGap(Math.Max(0, g - prev), true);
    }
}
