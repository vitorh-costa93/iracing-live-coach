using System;
using System.Collections.Generic;
using System.Linq;
using IRSDKSharper;
using IracingLiveCoach.Core.Coach;

namespace IracingLiveCoach.Core.Telemetry;

/// <summary>Pit-rejoin prediction ("if you pit now, you rejoin P9"): learns the pit loss of the track from every
/// car's real pit-road visits (<see cref="PitLossModel"/>) and projects the field with
/// <see cref="PitRejoinPredictor"/>. ~6 Hz, only while someone listens (widget or coach).</summary>
public partial class TelemetryReader
{
    private const int PitRejoinRefreshTicks = 10;
    private const int PitSvFuelFlag = 0x10, PitSvTireFlags = 0x0F;

    private readonly PitLossModel _pitLoss = new() { Store = new CarServiceStore() };
    private int _pitLossTrackId;
    private int _rejoinTick = PitRejoinRefreshTicks;
    private volatile PitRejoinPrediction? _lastRejoin;
    // Player's stop in progress: total loss frozen at the pit entry (the service shrinks while fuel goes in).
    private double _rejoinFrozenLoss = double.NaN, _rejoinFrozenEnterT = double.NaN;
    private object? _rejoinDriversSource;
    private Dictionary<int, (string Number, string Name, string ClassName, string? ClassColor, bool Skip)> _rejoinDrivers = new();

    /// <summary>Raised ~6 Hz with the current prediction (Available = false with a Reason when it cannot be made).</summary>
    public event Action<PitRejoinPrediction>? PitRejoinUpdated;

    /// <summary>One line per learned pit-loss sample.</summary>
    public event Action<string>? PitLossLog
    {
        add => _pitLoss.Log += value;
        remove => _pitLoss.Log -= value;
    }

    public PitRejoinPrediction? LastPitRejoin => _lastRejoin;

    private void ResetPitRejoinSession()
    {
        _pitLoss.ResetVisits();
        _lastRejoin = null;
        _rejoinFrozenLoss = _rejoinFrozenEnterT = double.NaN;
    }

    private void UpdatePitRejoin()
    {
        if (++_rejoinTick < PitRejoinRefreshTicks) return;
        _rejoinTick = 0;
        PitRejoinPrediction result;
        try { result = ComputePitRejoin(); }
        catch (Exception ex) { result = PitRejoinPrediction.Unavailable("error: " + ex.GetType().Name); }
        _lastRejoin = result;
        try { PitRejoinUpdated?.Invoke(result); } catch { /* a listener must not break the telemetry path */ }
    }

    private void RefreshRejoinDrivers(IRacingSdkSessionInfo? info)
    {
        var drivers = info?.DriverInfo?.Drivers;
        if (drivers is null || ReferenceEquals(drivers, _rejoinDriversSource)) return;
        var map = new Dictionary<int, (string, string, string, string?, bool)>();
        foreach (var d in drivers)
        {
            bool skip = d.CarIsPaceCar != 0 || Read(() => Convert.ToInt32(d.IsSpectator) != 0, false);
            map[d.CarIdx] = (d.CarNumber ?? "", FormatDriverName(d.UserName, d.CarNumber), d.CarClassShortName ?? "", d.CarClassColor, skip);
        }
        _rejoinDrivers = map;
        _rejoinDriversSource = drivers;
    }

    private PitRejoinPrediction ComputePitRejoin()
    {
        var data = _sdk.Data;
        var info = data.SessionInfo;
        if (_playerCarIdx < 0) return PitRejoinPrediction.Unavailable("no player car");
        if (_trackId != _pitLossTrackId) { _pitLoss.Reset(); _pitLossTrackId = _trackId; _rejoinFrozenLoss = double.NaN; }
        RefreshRejoinDrivers(info);
        var me0 = Read(() => info?.DriverInfo?.Drivers?.FirstOrDefault(d => d.CarIdx == _playerCarIdx), null);
        _pitLoss.SetCar(CarServiceStore.CarKey(me0?.CarPath, me0?.CarScreenName));

        double t = data.GetDouble("SessionTime");
        _pitLoss.BeginTick(t);
        int myClass = data.GetInt("CarIdxClass", _playerCarIdx);
        double lapTime = ClassLap(myClass);

        double fuel = Read(() => (double)data.GetFloat("FuelLevel"), -1);
        double fuelPct = Read(() => (double)data.GetFloat("FuelLevelPct"), -1);
        int svFlags = Read(() => data.GetInt("PitSvFlags"), -1);
        double svFuel = Read(() => (double)data.GetFloat("PitSvFuel"), double.NaN);
        double repair = Read(() => (double)data.GetFloat("PitRepairLeft"), 0);
        bool tires = svFlags > 0 && (svFlags & PitSvTireFlags) != 0;
        double fuelToAdd = double.NaN;
        if (svFlags >= 0)
        {
            fuelToAdd = (svFlags & PitSvFuelFlag) == 0 ? 0 : svFuel;
            double capacity = fuel >= 0 && fuelPct > 0.05 ? fuel / fuelPct : double.NaN;
            if (fuelToAdd > 0 && capacity > 0) fuelToAdd = Math.Min(fuelToAdd, Math.Max(0, capacity - fuel));
        }

        // 1) learning pass (every car, own class lap) and field snapshot
        var raw = new List<(int Idx, int Lap, float Pct, bool Pit, int Cls)>();
        for (int idx = 0; idx < IRacingSdkConst.MaxNumCars; idx++)
        {
            if (!_rejoinDrivers.TryGetValue(idx, out var drv) || drv.Skip) continue;
            float pct = data.GetFloat("CarIdxLapDistPct", idx);
            bool pit = data.GetBool("CarIdxOnPitRoad", idx);
            int cls = data.GetInt("CarIdxClass", idx);
            bool me = idx == _playerCarIdx;
            _pitLoss.Observe(idx, pit, pct, t, ClassLap(cls), me, me ? fuel : -1, me && tires);
            if (pct >= 0) raw.Add((idx, data.GetInt("CarIdxLapCompleted", idx), pct, pit, cls));
        }

        if (!(lapTime > 1)) return PitRejoinPrediction.Unavailable("no reference lap time");
        double pitLimit = PitRejoinPredictor.ParseSpeedMs(Read(() => info?.WeekendInfo?.TrackPitSpeedLimit, null));
        var est = _pitLoss.Estimate(fuelToAdd, tires, repair, lapTime, _trackLengthMeters ?? double.NaN, pitLimit);
        if (!est.Available) return PitRejoinPrediction.Unavailable(est.Note);

        bool race = _isRaceSession;
        if (race && _sessionState != 4) return PitRejoinPrediction.Unavailable("waiting for the green flag");

        // 2) field with what is left of the stops in progress
        RejoinCar? player = null;
        var field = new List<RejoinCar>(raw.Count);
        foreach (var c in raw)
        {
            var drv = _rejoinDrivers[c.Idx];
            double carLap = ClassLap(c.Cls);
            double remaining = c.Pit && c.Idx != _playerCarIdx ? _pitLoss.RemainingLaneLoss(c.Idx, c.Pct, t, carLap > 1 ? carLap : lapTime, est.LaneLossSec) : 0;
            var car = new RejoinCar(c.Idx, drv.Number, drv.Name, c.Cls, c.Lap, c.Pct, c.Pit, remaining, drv.ClassName, drv.ClassColor,
                _classRankById.TryGetValue(c.Cls, out var rank) ? rank : 0, carLap > 1 ? carLap : 0);
            if (c.Idx == _playerCarIdx) player = car; else field.Add(car);
        }
        if (player is null) return PitRejoinPrediction.Unavailable("player not on track");

        double loss = est.TotalSec;
        if (player.OnPitRoad && _pitLoss.TryGetVisit(_playerCarIdx, out var enterT, out var enterPct, out _))
        {
            if (_rejoinFrozenEnterT != enterT || double.IsNaN(_rejoinFrozenLoss)) { _rejoinFrozenEnterT = enterT; _rejoinFrozenLoss = est.TotalSec; }
            loss = Math.Max(0, _rejoinFrozenLoss - PitLossModel.LostSoFar(enterT, enterPct, t, player.LapDistPct, lapTime));
        }
        else if (!player.OnPitRoad) _rejoinFrozenLoss = _rejoinFrozenEnterT = double.NaN;

        var prediction = PitRejoinPredictor.Predict(player, field, lapTime, loss, raceOrder: race);
        return prediction.Available
            ? prediction with
            {
                LaneLossSec = est.LaneLossSec, ServiceSec = est.ServiceSec, FuelToAddL = fuelToAdd,
                Estimated = est.Estimated, Note = est.Note,
            }
            : prediction;
    }

    private double ClassLap(int classId) =>
        _classLapTimeById.TryGetValue(classId, out var cl) && cl > 1 ? cl : _estimatedLapTime ?? 0;
}
