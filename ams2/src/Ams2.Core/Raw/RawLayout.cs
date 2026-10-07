using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ams2.Core.Raw;

// Espelho de SharedMemory.h (Project CARS 2 / AMS2). Layout v9; o AMS2 real reporta v14, com os mesmos offsets
// até SnowDensity (conferido contra o HUD em sessão real em 02/10/2026). Campos novos da v14 vêm depois e são ignorados.
// Fonte: viper4gh/CREST2 SharedMemory.h. bool (C++) = 1 byte; Sequential com alinhamento natural, igual ao MSVC.
public static class Const
{
    public const string MapName = "$pcars2$";
    public const int ExpectedVersion = 9;
    public const int MaxSupportedVersion = 14;
    public const int StringLen = 64;
    public const int MaxParticipants = 64;
}

[InlineArray(4)] public struct F4 { float _; }
[InlineArray(4)] public struct U4 { uint _; }
[InlineArray(3)] public struct F3 { float _; }
[InlineArray(2)] public struct F2 { float _; }
[InlineArray(64)] public struct F64 { float _; }
[InlineArray(64)] public struct U64 { uint _; }
[InlineArray(64)] public struct B64 { byte _; }
[InlineArray(64 * 3)] public struct F64x3 { float _; }
[InlineArray(64 * 64)] public struct Str64x64 { byte _; }
[InlineArray(4 * 40)] public struct Compound4 { byte _; }

[StructLayout(LayoutKind.Sequential)]
public struct ParticipantInfo
{
    public byte IsActive;
    public B64 Name;
    public F3 WorldPosition;
    public float CurrentLapDistance;
    public uint RacePosition;
    public uint LapsCompleted;
    public uint CurrentLap;
    public int CurrentSector;
}

[InlineArray(64)] public struct ParticipantArray { ParticipantInfo _; }

[StructLayout(LayoutKind.Sequential)]
public struct RawSharedMemory
{
    public uint Version, BuildVersionNumber, GameState, SessionState, RaceState;
    public int ViewedParticipantIndex, NumParticipants;
    public ParticipantArray Participants;

    public float UnfilteredThrottle, UnfilteredBrake, UnfilteredSteering, UnfilteredClutch;
    public B64 CarName, CarClassName;

    public uint LapsInEvent;
    public B64 TrackLocation, TrackVariation;
    public float TrackLength;

    public int NumSectors;
    public byte LapInvalidated;
    public float BestLapTime, LastLapTime, CurrentTime, SplitTimeAhead, SplitTimeBehind, SplitTime, EventTimeRemaining,
        PersonalFastestLapTime, WorldFastestLapTime,
        CurrentSector1Time, CurrentSector2Time, CurrentSector3Time,
        FastestSector1Time, FastestSector2Time, FastestSector3Time,
        PersonalFastestSector1Time, PersonalFastestSector2Time, PersonalFastestSector3Time,
        WorldFastestSector1Time, WorldFastestSector2Time, WorldFastestSector3Time;

    public uint HighestFlagColour, HighestFlagReason, PitMode, PitSchedule, CarFlags;
    public float OilTempCelsius, OilPressureKPa, WaterTempCelsius, WaterPressureKPa, FuelPressureKPa,
        FuelLevel, FuelCapacity, Speed, Rpm, MaxRpm, Brake, Throttle, Clutch, Steering;
    public int Gear, NumGears;
    public float OdometerKM;
    public byte AntiLockActive;
    public int LastOpponentCollisionIndex;
    public float LastOpponentCollisionMagnitude;
    public byte BoostActive;
    public float BoostAmount;

    public F3 Orientation, LocalVelocity, WorldVelocity, AngularVelocity, LocalAcceleration, WorldAcceleration, ExtentsCentre;

    public U4 TyreFlags, Terrain;
    public F4 TyreY, TyreRPS, TyreSlipSpeed, TyreTemp, TyreGrip, TyreHeightAboveGround, TyreLateralStiffness,
        TyreWear, BrakeDamage, SuspensionDamage, BrakeTempCelsius,
        TyreTreadTemp, TyreLayerTemp, TyreCarcassTemp, TyreRimTemp, TyreInternalAirTemp;

    public uint CrashState;
    public float AeroDamage, EngineDamage;

    public float AmbientTemperature, TrackTemperature, RainDensity, WindSpeed, WindDirectionX, WindDirectionY, CloudBrightness;

    public uint SequenceNumber;

    public F4 WheelLocalPositionY, SuspensionTravel, SuspensionVelocity, AirPressure;
    public float EngineSpeed, EngineTorque;
    public F2 Wings;
    public float HandBrake;

    public F64 CurrentSector1Times, CurrentSector2Times, CurrentSector3Times,
        FastestSector1Times, FastestSector2Times, FastestSector3Times, FastestLapTimes, LastLapTimes;
    public B64 LapsInvalidated;
    public U64 RaceStates, PitModes;
    public F64x3 Orientations;
    public F64 Speeds;
    public Str64x64 CarNames, CarClassNames;

    public int EnforcedPitStopLap;
    public B64 TranslatedTrackLocation, TranslatedTrackVariation;
    public float BrakeBias, TurboBoostPressure;
    public Compound4 TyreCompound;
    public U64 PitSchedules, HighestFlagColours, HighestFlagReasons, Nationalities;
    public float SnowDensity;
}
