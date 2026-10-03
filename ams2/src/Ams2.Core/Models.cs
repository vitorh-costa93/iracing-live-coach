namespace Ams2.Core;

/// <summary>Modelo neutro: widgets só conhecem estes records, nunca a estrutura crua.</summary>
public sealed record CarSnapshot(
    int Index,
    string Name,
    string CarName,
    string ClassName,
    int Position,
    int ClassPosition,
    int LapsCompleted,
    int CurrentLap,
    double LapDistance,
    int Sector,
    double BestLapTime,   // s; <= 0 = sem tempo
    double LastLapTime,   // s; <= 0 = sem tempo
    double SpeedMps,
    PitState PitState,
    RaceState RaceState,
    bool LapInvalid,
    bool IsPlayer,
    string Nationality = "")   // ISO 3166-1 alpha-2 minusculo ("br", "gb"); "" = desconhecida, sem bandeira
{
    /// <summary>Progresso total na corrida em metros (voltas completas + distância na volta).</summary>
    public double TotalDistance(double trackLength) => LapsCompleted * trackLength + LapDistance;
    public bool InPitLane => PitState is PitState.DrivingIntoPits or PitState.InPit or PitState.DrivingOutOfPits;
    public bool InGarage => PitState is PitState.InGarage or PitState.DrivingOutOfGarage;
}

public enum PitState { None, DrivingIntoPits, InPit, DrivingOutOfPits, InGarage, DrivingOutOfGarage }
public enum RaceState { Invalid, NotStarted, Racing, Finished, Disqualified, Retired, Dnf }
public enum SessionKind { Invalid, Practice, Test, Qualify, FormationLap, Race, TimeAttack }

public sealed record WheelSnapshot(double TempC, double Wear, double AirPressure, double BrakeTempC, string Compound);

public sealed record InputsSnapshot(double Throttle, double Brake, double Clutch, double Steering,
    double UnfilteredThrottle, double UnfilteredBrake, double UnfilteredClutch, double UnfilteredSteering);

public sealed record PlayerSnapshot(
    int Index,
    InputsSnapshot Inputs,
    int Gear,
    double Rpm,
    double MaxRpm,
    double SpeedMps,
    double FuelLiters,
    double FuelCapacity,
    // ordem: FL, FR, RL, RR. Wear: 0 = novo ... 1 = gasto (a confirmar em sessão real).
    IReadOnlyList<WheelSnapshot> Wheels,
    double BrakeBias);

public sealed record WeatherSnapshot(double AmbientC, double TrackC, double RainDensity, double WindSpeed,
    double WindDirX, double WindDirY, double CloudBrightness, double SnowDensity);

public sealed record SessionSnapshot(
    uint Version,
    uint Sequence,
    bool InSession,       // jogo rodando uma sessão (não menu/pausa/replay)
    SessionKind Kind,
    string Track,
    string TrackVariation,
    double TrackLength,
    int LapsInEvent,      // 0 = por tempo
    double? TimeRemainingSeconds,
    uint FlagColour,
    WeatherSnapshot Weather,
    IReadOnlyList<CarSnapshot> Cars,
    PlayerSnapshot? Player)
{
    public CarSnapshot? PlayerCar => Player is null ? null : Cars.FirstOrDefault(c => c.Index == Player.Index);
}
