namespace Ams2.Core.Calc;

/// <summary>Car-identity row movement. Duration is an approximate presentation adjustment from Monza 2021.</summary>
public sealed class Broadcast18RowMotion
{
    public const double Duration = .28;
    readonly Dictionary<int, (float From, float To, double At)> _rows = [];
    object? _context;
    double _last = double.NegativeInfinity;

    public void Reset() { _rows.Clear(); _context = null; _last = double.NegativeInfinity; }

    static float At((float From, float To, double At) row, double now) =>
        row.From + (row.To - row.From) * (float)Math.Clamp((now - row.At) / Duration, 0, 1);

    public IReadOnlyDictionary<int, float> Evaluate(double now, object context, IReadOnlyDictionary<int, float> targets)
    {
        if (!double.IsFinite(now)) { Reset(); return targets.ToDictionary(pair => pair.Key, pair => pair.Value); }
        if (now < _last || !Equals(context, _context)) Reset();
        _context = context; _last = now;
        foreach (int removed in _rows.Keys.Where(id => !targets.ContainsKey(id)).ToArray()) _rows.Remove(removed);
        var result = new Dictionary<int, float>();
        foreach (var (id, target) in targets)
        {
            if (!_rows.TryGetValue(id, out var row)) row = (target, target, now);
            else if (row.To != target) row = (At(row, now), target, now);
            _rows[id] = row;
            result[id] = At(row, now);
        }
        return result;
    }
}
