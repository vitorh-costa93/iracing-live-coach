using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Retains measured events while pit/fastest obscure them; holds count visible time.</summary>
public sealed class Broadcast93RacePresentation
{
    public sealed record Frame(Broadcast93RaceBoard.Mode Mode, BoardGap93? Gap = null, int? Remaining = null);
    BoardGap93? _gap;
    int? _remaining;
    double _gapShown, _remainingShown, _now = double.NegativeInfinity,
        _gapAt = double.NegativeInfinity, _lapAt = double.NegativeInfinity;
    string? _key;
    Broadcast93RaceBoard.Mode _mode;

    public void Reset()
    {
        _gap = null; _remaining = null; _gapShown = _remainingShown = 0;
        _now = _gapAt = _lapAt = double.NegativeInfinity; _key = null;
        _mode = Broadcast93RaceBoard.Mode.None;
    }

    public Frame Update(OverlayModel m, FastestLapEvent? fastest = null, bool fastestEnabled = true, Broadcast93RaceBoard.Holds? holds = null,
        bool pitEnabled = true, bool gapEnabled = true, bool lapsToGoEnabled = true)
    {
        holds ??= Broadcast93RaceBoard.Holds.Default;
        if (!Broadcast93RaceBoard.Visible(m)) { Reset(); return new(Broadcast93RaceBoard.Mode.None); }
        var s = m.Session!;
        var me = s.PlayerCar!;
        var key = $"{s.Kind}|{s.Track}|{s.TrackVariation}|{m.QualiLap?.SessionGeneration}|{me.Index}|{me.Name}|{me.CarName}|{me.ClassName}";
        if (_key != key || m.Now < _now || m.Now - _now > 2) Reset();
        double dt = double.IsFinite(_now) ? Math.Max(0, m.Now - _now) : 0;
        _key = key; _now = m.Now;
        if (_mode == Broadcast93RaceBoard.Mode.Gap) _gapShown += dt;
        if (_mode == Broadcast93RaceBoard.Mode.LapsToGo) _remainingShown += dt;
        if (_gap is { } held && (_gapShown >= held.CloseT - held.CompletedT
            || !s.Cars.Any(c => c.Index == held.Player.CarIndex && c.Name == held.Player.Name)
            || !s.Cars.Any(c => c.Index == held.Neighbor.CarIndex && c.Name == held.Neighbor.Name))) _gap = null;
        if (_remainingShown >= holds.LapsToGo) _remaining = null;
        if (gapEnabled && m.Board?.Gap93 is { } gap && m.Now >= gap.CompletedT && m.Now < gap.CloseT && gap.CompletedT > _gapAt)
        { _gap = gap; _gapAt = gap.CompletedT; _gapShown = 0; }
        var b = BroadcastUi.State(m);
        if (lapsToGoEnabled && Broadcast93RaceBoard.LapsToGoActive(m, b, holds.LapsToGo) && b.PlayerLapChangedT > _lapAt)
        { _lapAt = b.PlayerLapChangedT; _remaining = m.Board?.LapsRemaining ?? Broadcast93Rules.LapsRemaining(s); _remainingShown = 0; }
        _mode = pitEnabled && Broadcast93RaceBoard.PitActive(b, m.Now, holds.Pit) ? Broadcast93RaceBoard.Mode.Pit
            : fastestEnabled && fastest?.ActiveAt(m.Now, holds.Fastest) == true ? Broadcast93RaceBoard.Mode.Fastest
            : _gap is not null ? Broadcast93RaceBoard.Mode.Gap
            : _remaining is not null ? Broadcast93RaceBoard.Mode.LapsToGo : Broadcast93RaceBoard.Mode.Caption;
        return new(_mode, _gap, _remaining);
    }
}
