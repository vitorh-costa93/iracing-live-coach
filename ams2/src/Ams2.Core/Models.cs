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
    string TyreSupplier = "",   // "M" Michelin, "B" Bridgestone, "" desconhecido (sufixo "(M)"/"(B)" do nome do carro)
    string OriginalName = "",   // so no carro do jogador: nome que o jogo deu (perfil), preservado quando Name e substituido por um nome de exibicao
    double PosX = 0, double PosY = 0, double PosZ = 0,   // WorldPosition (m); Y = altura
    double Yaw = 0,                                       // Orientations[i][1] (rad). Rumo de avanco = Yaw + pi (confirmado em sessao real, ver radar-notes.md)
    // mCurrentSector{1,2,3}Times / mFastestSector{1,2,3}Times por carro (s; <= 0 = sem dado). Nao conferidos em sessao real: o QualiLapTracker
    // so os usa quando sao plausiveis e cai para o tempo derivado da troca de Sector quando faltam.
    double CurSector1 = 0, double CurSector2 = 0, double CurSector3 = 0,
    double BestSector1 = 0, double BestSector2 = 0, double BestSector3 = 0,
    string TeamName = "",   // so no carro do jogador: equipe da pintura escolhida no Control Center ("" = deduzir do nome do carro)
    string Country = "")    // so no carro do jogador: pais do piloto da pintura escolhida (sigla do jogo, ex. "FIN"; "" = desconhecido)
{
    /// <summary>Tempo do setor <paramref name="k"/> (0..2) da volta corrente, como a memoria informa (<= 0 = sem dado).</summary>
    public double CurSector(int k) => k switch { 0 => CurSector1, 1 => CurSector2, 2 => CurSector3, _ => 0 };
    /// <summary>Melhor tempo do setor <paramref name="k"/> (0..2) informado pela memoria (<= 0 = sem dado).</summary>
    public double BestSector(int k) => k switch { 0 => BestSector1, 1 => BestSector2, 2 => BestSector3, _ => 0 };
    /// <summary>true se o jogo informou posicao (tudo zero = sem pose, p.ex. carro ainda nao posicionado).</summary>
    public bool HasPose => PosX != 0 || PosY != 0 || PosZ != 0 || Yaw != 0;
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
    PlayerSnapshot? Player,
    uint GameState = 2)   // $pcars2$ GameState cru: 1 menu, 2 jogando, 3 carregando, 4 pausa/classificacao dentro da sessao, 5/6 replay/outros
{
    public CarSnapshot? PlayerCar => Player is null ? null : Cars.FirstOrDefault(c => c.Index == Player.Index);
}
