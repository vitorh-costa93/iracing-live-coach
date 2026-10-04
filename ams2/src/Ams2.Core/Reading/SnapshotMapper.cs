using System.Runtime.CompilerServices;
using System.Text;
using Ams2.Core.Raw;

namespace Ams2.Core.Reading;

/// <summary>Converte a estrutura crua do $pcars2$ para o modelo neutro.</summary>
public static class SnapshotMapper
{
    public static SessionSnapshot Map(in RawSharedMemory raw)
    {
        int n = Math.Clamp(raw.NumParticipants, 0, Const.MaxParticipants);
        int playerIdx = raw.ViewedParticipantIndex;
        double trackLength = raw.TrackLength > 0 ? raw.TrackLength : 0;

        var cars = new List<CarSnapshot>(n);
        for (int i = 0; i < n; i++)
        {
            ref readonly var p = ref raw.Participants[i];
            if (p.IsActive == 0) continue;
            cars.Add(new CarSnapshot(
                Index: i,
                Name: Text(raw.Participants[i].Name, 0, Const.StringLen),
                CarName: Text(raw.CarNames, i * Const.StringLen, Const.StringLen),
                ClassName: Text(raw.CarClassNames, i * Const.StringLen, Const.StringLen),
                Position: (int)p.RacePosition,
                ClassPosition: 0,
                LapsCompleted: (int)p.LapsCompleted,
                CurrentLap: (int)p.CurrentLap,
                LapDistance: p.CurrentLapDistance,
                Sector: p.CurrentSector,
                BestLapTime: raw.FastestLapTimes[i],
                LastLapTime: raw.LastLapTimes[i],
                SpeedMps: raw.Speeds[i],
                PitState: MapPit(raw.PitModes[i]),
                RaceState: MapRace(raw.RaceStates[i]),
                LapInvalid: raw.LapsInvalidated[i] != 0,
                IsPlayer: i == playerIdx,
                TyreSupplier: SupplierFromCarName(Text(raw.CarNames, i * Const.StringLen, Const.StringLen)),
                PosX: p.WorldPosition[0], PosY: p.WorldPosition[1], PosZ: p.WorldPosition[2],
                Yaw: raw.Orientations[i * 3 + 1],
                CurSector1: raw.CurrentSector1Times[i], CurSector2: raw.CurrentSector2Times[i], CurSector3: raw.CurrentSector3Times[i],
                BestSector1: raw.FastestSector1Times[i], BestSector2: raw.FastestSector2Times[i], BestSector3: raw.FastestSector3Times[i]));
        }

        cars = AssignClassPositions(cars);
        bool playerKnown = cars.Any(c => c.Index == playerIdx);

        var wheels = new WheelSnapshot[4];
        for (int w = 0; w < 4; w++)
            wheels[w] = new WheelSnapshot(raw.TyreTemp[w], raw.TyreWear[w], raw.AirPressure[w], raw.BrakeTempCelsius[w],
                Text(raw.TyreCompound, w * 40, 40));

        PlayerSnapshot? player = playerKnown
            ? new PlayerSnapshot(playerIdx,
                new InputsSnapshot(raw.Throttle, raw.Brake, raw.Clutch, raw.Steering,
                    raw.UnfilteredThrottle, raw.UnfilteredBrake, raw.UnfilteredClutch, raw.UnfilteredSteering),
                raw.Gear, raw.Rpm, raw.MaxRpm, raw.Speed,
                FuelLiters: raw.FuelLevel * raw.FuelCapacity, FuelCapacity: raw.FuelCapacity,
                wheels, raw.BrakeBias)
            : null;

        // mEventTimeRemaining é documentado em milissegundos (UNSET = -1). Confirmar em sessão real.
        double? remaining = raw.EventTimeRemaining >= 0 ? raw.EventTimeRemaining / 1000.0 : null;

        return new SessionSnapshot(
            raw.Version, raw.SequenceNumber,
            InSession: raw.GameState == 2,
            Kind: raw.SessionState <= 6 ? (SessionKind)raw.SessionState : SessionKind.Invalid,
            Track: Text(raw.TranslatedTrackLocation, 0, Const.StringLen) is { Length: > 0 } t ? t : Text(raw.TrackLocation, 0, Const.StringLen),
            TrackVariation: Text(raw.TranslatedTrackVariation, 0, Const.StringLen) is { Length: > 0 } v ? v : Text(raw.TrackVariation, 0, Const.StringLen),
            trackLength, (int)raw.LapsInEvent, remaining, raw.HighestFlagColour,
            new WeatherSnapshot(raw.AmbientTemperature, raw.TrackTemperature, raw.RainDensity, raw.WindSpeed,
                raw.WindDirectionX, raw.WindDirectionY, raw.CloudBrightness, raw.SnowDensity),
            cars, player, raw.GameState);
    }

    /// <summary>Não há ID de classe: a posição na classe vem da ordem de posição geral dentro do mesmo nome de classe.</summary>
    static List<CarSnapshot> AssignClassPositions(List<CarSnapshot> cars)
    {
        var rank = new Dictionary<int, int>();
        foreach (var g in cars.GroupBy(c => c.ClassName))
        {
            int pos = 1;
            foreach (var c in g.OrderBy(c => c.Position == 0 ? int.MaxValue : c.Position)) rank[c.Index] = pos++;
        }
        return cars.Select(c => c with { ClassPosition = rank[c.Index] }).ToList();
    }

    /// <summary>Sufixo do nome do carro: "(M)" = Michelin, "(B)" = Bridgestone. Sem sufixo (ex.: carro do jogador) = desconhecido:
    /// nenhum outro campo (TyreCompound = "Slick Macio", classe) traz o fornecedor.</summary>
    public static string SupplierFromCarName(string carName)
    {
        var n = carName.TrimEnd();
        if (n.EndsWith("(M)", StringComparison.Ordinal)) return "M";
        if (n.EndsWith("(B)", StringComparison.Ordinal)) return "B";
        return "";
    }

    static PitState MapPit(uint v) => v <= 5 ? (PitState)v : PitState.None;
    static RaceState MapRace(uint v) => v <= 6 ? (RaceState)v : RaceState.Invalid;

    static string Text<T>(in T buf, int offset, int len) where T : struct
    {
        var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(
            System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in buf), 1)).Slice(offset, len);
        int z = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(z < 0 ? bytes : bytes[..z]).Trim();
    }
}
