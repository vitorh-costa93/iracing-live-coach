using System.Numerics;

namespace Ams2.Core.Calc.GpxSat;

/// <summary>O SDK não fornece a malha da pista. Aprende a linha percorrida, sem desenhar um circuito fictício.</summary>
public sealed class GpxTrackMap
{
    const int Bins = 256;
    readonly Vector2?[] _points = new Vector2?[Bins];
    string? _key;
    bool _boundsDirty = true;
    float _minX, _maxX, _minY, _maxY;
    public int Count => _points.Count(x => x.HasValue);
    public bool Ready => Count >= Bins * .9;
    public void Reset() { Array.Clear(_points); _key = null; _boundsDirty = true; }

    public void Observe(SessionSnapshot s)
    {
        string key = s.Track + "|" + s.TrackVariation + "|" + s.TrackLength;
        if (_key != key) { Reset(); _key = key; }
        if (!s.InSession || s.TrackLength <= 0 || s.PlayerCar is not { HasPose: true, InPitLane: false, InGarage: false } car) return;
        if (!double.IsFinite(car.LapDistance) || !double.IsFinite(car.PosX) || !double.IsFinite(car.PosZ) || car.LapDistance < 0 || car.LapDistance >= s.TrackLength) return;
        int bin = Math.Clamp((int)(car.LapDistance / s.TrackLength * Bins), 0, Bins - 1);
        _points[bin] = new((float)car.PosX, (float)car.PosZ);
        _boundsDirty = true;
    }

    public IReadOnlyList<Vector2> Points => _points.Where(p => p.HasValue).Select(p => p!.Value).ToArray();

    public IEnumerable<(Vector2 A, Vector2 B)> Segments
    {
        get
        {
            for (int i=0; i<Bins; i++)
                if (_points[i] is { } a && _points[(i+1)%Bins] is { } b) yield return (a,b);
        }
    }

    public Vector2 Project(Vector2 p, float x, float y, float size)
    {
        if (_boundsDirty)
        {
            var points = Points;
            if (points.Count == 0) return new(x, y);
            _minX = points.Min(p => p.X); _maxX = points.Max(p => p.X);
            _minY = points.Min(p => p.Y); _maxY = points.Max(p => p.Y);
            _boundsDirty = false;
        }
        float scale = Math.Max(1, Math.Max(_maxX-_minX, _maxY-_minY));
        return new(x + 8 + (_maxX-p.X) / scale * (size-16), y + 8 + (p.Y-_minY) / scale * (size-16));
    }
}
