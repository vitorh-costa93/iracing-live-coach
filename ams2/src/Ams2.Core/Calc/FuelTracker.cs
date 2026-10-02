namespace Ams2.Core.Calc;

public sealed record FuelEstimate(
    double LitersLeft,
    double? PerLapAverage,
    double? LapsRemainingOnFuel,
    int? LapsToFinish,        // null em corrida por tempo ou sem dado
    double? LitersToAdd);     // null se não há como estimar ou não precisa

/// <summary>Consumo por volta: média das últimas voltas "limpas" (sem pit e sem reabastecimento).</summary>
public sealed class FuelTracker(int window = 5)
{
    readonly Queue<double> _laps = new();
    int _lapsCompleted = -1;
    double _fuelAtLapStart;
    bool _dirty; // volta com pit/reabastecimento

    public void Reset() { _laps.Clear(); _lapsCompleted = -1; _dirty = false; }

    public FuelEstimate Update(SessionSnapshot s)
    {
        var me = s.PlayerCar;
        if (s.Player is null || me is null) return new FuelEstimate(0, null, null, null, null);
        double fuel = s.Player.FuelLiters;

        if (_lapsCompleted < 0 || me.LapsCompleted < _lapsCompleted) // início ou reinício de sessão
        {
            _laps.Clear();
            _lapsCompleted = me.LapsCompleted;
            _fuelAtLapStart = fuel;
            _dirty = true; // a primeira volta é parcial
        }
        else if (me.LapsCompleted > _lapsCompleted)
        {
            double used = _fuelAtLapStart - fuel;
            if (!_dirty && me.LapsCompleted == _lapsCompleted + 1 && used > 0)
            {
                _laps.Enqueue(used);
                while (_laps.Count > window) _laps.Dequeue();
            }
            _lapsCompleted = me.LapsCompleted;
            _fuelAtLapStart = fuel;
            _dirty = false;
        }

        if (me.PitState != PitState.None || fuel > _fuelAtLapStart + 0.05) { _dirty = true; if (fuel > _fuelAtLapStart) _fuelAtLapStart = fuel; }

        double? avg = _laps.Count > 0 ? _laps.Average() : null;
        double? lapsOnFuel = avg is > 0 ? fuel / avg : null;
        int? toFinish = s.LapsInEvent > 0 ? Math.Max(0, s.LapsInEvent - me.LapsCompleted) : null;
        double? add = avg is > 0 && toFinish is { } t ? Math.Max(0, t * avg.Value - fuel) : null;
        return new FuelEstimate(fuel, avg, lapsOnFuel, toFinish, add);
    }
}
