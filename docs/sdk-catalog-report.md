# Catálogo do SDK do iRacing (corrida real em Road Atlanta, 60 Hz)

O catálogo lista **349 variáveis de telemetria** (47 são arrays por carro/roda) e **293 chaves de SessionInfo** em 8 seções. Fonte: `sdk_catalog_race.json`.
O Live Coach V3 lê **54** variáveis pelo nome; as outras **295** estão sem uso hoje (a contagem é por nome literal no código, então é aproximada).

# Guia de exemplos: o que cada informação do SDK significa

Valores tirados de `sdk_catalog_race.json` (corrida em Road Atlanta, 60 Hz). Os exemplos de bitfield foram decodificados; as descrições vêm do próprio catálogo do SDK (campo `desc`).

## Como ler um array por carro
Variáveis `CarIdx*` têm 72 posições, uma por carro. O índice é o `CarIdx` do piloto na sessão (não o número do carro, e o do jogador muda entre sessões).
- `CarIdxLapDistPct` = `[0.0358, 0.991, 0.0019, 0.9841, ...]`: o carro 0 está a 3,6% da volta; o 1 está a 99,1%, logo atrás da linha; o 2 acabou de cruzar.
- `CarIdxEstTime` = `[1.87, 67.97, 0.11, 67.56, ...]`: segundos que o carro levaria (na volta de referência da classe) para chegar onde está. Com isso dá para estimar o gap entre dois carros mesmo sem histórico de voltas.

## Bitfields decodificados
- `SessionFlags` = `268697636` → **green | blue | servicible | startHidden**. É o estado global (bandeira verde, aviso de largada etc.).
- `CarIdxSessionFlags` = `262144` = `0x40000` → só "servicible": o carro está normal. Se aparecer `0x80000` (furled) é aviso de Slow Down; `0x10000/0x20000/0x100000` (black, disqualify, repair) é ida obrigatória ao box. É o que alimenta as badges do Relative.
- `PitSvFlags` = `16` = `0x10` → o box tem "abastecer" marcado. Por isso o Fuel at End soma o `PitSvFuel`.
- `EngineWarnings` = `0` → nenhuma luz acesa (bits para água, óleo, pressão de óleo, limitador etc.).
- `CarLeftRight` = `1` → livre. `2` carro à esquerda, `3` à direita, `4` dos dois lados, `5` dois à esquerda, `6` dois à direita.
- `CarIdxTrackSurface` = `3` → na pista. `-1` fora do mundo, `0` fora da pista, `1` parado no box (pit stall), `2` aproximando-se dos boxes.

## Exemplos de informação para o piloto
| Informação possível | Variáveis | Exemplo com o valor gravado |
|---|---|---|
| Delta ao vivo | `LapDeltaToBestLap`, `LapCurrentLapTime` | +1,78 s contra a melhor volta, volta atual em 7,46 s |
| Combustível | `FuelLevel`, `FuelUsePerHour`, `PitSvFuel` | 57,5 L; consumo instantâneo 112 kg/h; 34 L planejados no box |
| Temperatura de pneus | `LFtempCL` (e as outras 11 de carcaça) | carcaça esquerda dianteira a 78,3 °C |
| Desgaste de pneu | `LFwearR` | 98,6% de banda restante |
| Clima e pista | `TrackTempCrew`, `TrackWetness`, `Precipitation`, `WindDir` | pista a 26,7 °C, umidade nível 1, sem chuva, vento a 2,86 rad |
| Quem fala no rádio | `RadioTransmitCarIdx` | `-1` = ninguém; se for 5, mostrar o nome do carro 5 |
| ABS atuando | `BrakeABSactive` | `false` na frente reta |
| Incidentes | `PlayerCarMyIncidentCount` | 0 |
| Distribuição de freio | `dcBrakeBias` | 47,0 |
| Reboque | `PlayerCarTowTime` | 0 = sem reboque; maior que 0 = sendo rebocado |

Observação: `LFpressure` não existe neste catálogo (a pressão de pneu só é publicada no box), então uma tela de pressão exigiria outra variável ou o SessionInfo.

## Variáveis por família (usadas / total)

| Família | Usadas | Total | Exemplos não usados |
|---|---|---|---|
| Outros | 4 | 66 | ManualBoost, ManualNoBoost, MemPageFaultSec, MemSoftPageFaultSec, PaceMode |
| CarIdx | 15 | 27 | CarIdxTrackSurface, CarIdxTrackSurfaceMaterial, CarIdxBestLapNum, CarIdxQualTireCompound, CarIdxQualTireCompoundLocked |
| Lap | 3 | 27 | LapCompleted, LapDist, LapBestLap, LapBestLapTime, LapCurrentLapTime |
| Player | 4 | 22 | PlayerCarPosition, PlayerCarClassPosition, PlayerCarClass, PlayerTrackSurfaceMaterial, PlayerCarTeamIncidentCount |
| Steering | 1 | 15 | SteeringFFBEnabled, SteeringWheelAngleMax, SteeringWheelPctTorque, SteeringWheelPctTorqueSign, SteeringWheelPctTorqueSignStops |
| LF | 1 | 15 | LFTiresUsed, LFTiresAvailable, LFbrakeLinePress, LFcoldPressure, LFodometer |
| RF | 0 | 15 | RFTiresUsed, RFTiresAvailable, RFbrakeLinePress, RFcoldPressure, RFodometer |
| LR | 0 | 15 | LRTiresUsed, LRTiresAvailable, LRbrakeLinePress, LRcoldPressure, LRodometer |
| RR | 0 | 15 | RRTiresUsed, RRTiresAvailable, RRbrakeLinePress, RRcoldPressure, RRodometer |
| Session | 6 | 14 | SessionTick, SessionLapsRemain, SessionLapsRemainEx, SessionTimeTotal, SessionLapsTotal |
| Pit | 2 | 14 | Pitch, PitsOpen, PitRepairLeft, PitOptRepairLeft, PitstopActive |
| Replay | 0 | 6 | ReplayFrameNum, ReplayFrameNumEnd, ReplayPlaySpeed, ReplayPlaySlowMotion, ReplaySessionTime |
| Tire | 0 | 6 | TireSetsUsed, TireSetsAvailable, TireLF_RumblePitch, TireRF_RumblePitch, TireLR_RumblePitch |
| Vel | 0 | 6 | VelocityZ_ST, VelocityY_ST, VelocityX_ST, VelocityZ, VelocityY |
| Is | 0 | 5 | IsReplayPlaying, IsDiskLoggingEnabled, IsDiskLoggingActive, IsInGarage, IsGarageVisible |
| Chan | 0 | 4 | ChanAvgLatency, ChanLatency, ChanPartnerQuality, ChanClockSkew |
| Yaw | 0 | 4 | Yaw, YawNorth, YawRate_ST, YawRate |
| Cam | 0 | 4 | CamCarIdx, CamCameraNumber, CamGroupNumber, CamCameraState |
| Shift | 0 | 4 | ShiftPowerPct, ShiftGrindRPM, Shifter, ShiftIndicatorPct |
| Fuel | 3 | 4 | FuelPress |
| Radio | 0 | 3 | RadioTransmitCarIdx, RadioTransmitRadioIdx, RadioTransmitFrequencyIdx |
| Brake | 1 | 3 | BrakeRaw, BrakeABSactive |
| Car | 1 | 3 | CarDistAhead, CarDistBehind |
| Roll | 0 | 3 | Roll, RollRate_ST, RollRate |
| Track | 2 | 3 | TrackTempCrew |
| Air | 1 | 3 | AirDensity, AirPressure |
| Engine | 0 | 3 | EngineWarnings, Engine0_RPM, Engine1_RPM |
| Oil | 0 | 3 | OilTemp, OilPress, OilLevel |
| Push | 0 | 2 | PushToTalk, PushToPass |
| IsOn | 1 | 2 | IsOnTrackCar |
| Cpu | 0 | 2 | CpuUsageFG, CpuUsageBG |
| Throttle | 1 | 2 | ThrottleRaw |
| Clutch | 1 | 2 | ClutchRaw |
| Wind | 2 | 2 |  |
| Solar | 0 | 2 | SolarAltitude, SolarAzimuth |
| Fast | 0 | 2 | FastRepairUsed, FastRepairAvailable |
| Lat | 0 | 2 | LatAccel_ST, LatAccel |
| Lon | 0 | 2 | LongAccel_ST, LongAccel |
| Water | 0 | 2 | WaterTemp, WaterLevel |
| Dis | 0 | 1 | DisplayUnits |
| Driver | 0 | 1 | DriverMarker |
| Frame | 0 | 1 | FrameRate |
| Gpu | 0 | 1 | GpuUsage |
| ChanQuality | 0 | 1 | ChanQuality |
| Gear | 1 | 1 |  |
| Speed | 1 | 1 |  |
| Enter | 0 | 1 | EnterExitReset |
| Skies | 0 | 1 | Skies |
| Rel | 1 | 1 |  |
| Fog | 0 | 1 | FogLevel |
| Precip | 1 | 1 |  |
| Weather | 1 | 1 |  |
| Voltage | 0 | 1 | Voltage |
| Manifold | 0 | 1 | ManifoldPress |

## Todas as variáveis

| Nome | Tipo | Qtd | Unidade | Descrição | Exemplo (valor gravado na corrida) | Usada |
|---|---|---|---|---|---|---|
| AirDensity | float | 1 | kg/m^3 | Density of air at start/finish line | 1.1402 kg/m^3 |  |
| AirPressure | float | 1 | Pa | Pressure of air at start/finish line | 98123.5 Pa |  |
| AirTemp | float | 1 | C | Temperature of air at start/finish line | 24.1286 C | sim |
| Brake | float | 1 | % | 0=brake released to 1=max pedal force | 0 % | sim |
| BrakeABSactive | bool | 1 |  | true if abs is currently reducing brake force pressure | False |  |
| BrakeRaw | float | 1 | % | Raw brake input 0=brake released to 1=max pedal force | 0 % |  |
| CamCameraNumber | int | 1 |  | Active camera number | 1 |  |
| CamCameraState | bitfield | 1 | irsdk_CameraState | State of camera system | 80 (0x50) = auto shot selection + key acceleration |  |
| CamCarIdx | int | 1 |  | Active camera's focus car index | 0 |  |
| CamGroupNumber | int | 1 |  | Active camera group number | 9 |  |
| CarDistAhead | float | 1 | m | Distance to first car in front of player in meters | 7.3524 m |  |
| CarDistBehind | float | 1 | m | Distance to first car behind player in meters | 11.4388 m |  |
| CarIdxBestLapNum | int | 72 |  | Cars best lap number | carros 0-3: [-1, -1, -1, -1] |  |
| CarIdxBestLapTime | float | 72 | s | Cars best lap time | carros 0-3: [-1, -1, -1, -1] s | sim |
| CarIdxClass | int | 72 |  | Cars class id by car index | carros 0-3: [4029, 2523, 2523, 2523] | sim |
| CarIdxClassPosition | int | 72 |  | Cars class position in race by car index | carros 0-3: [5, 8, 3, 13] | sim |
| CarIdxEstTime | float | 72 | s | Estimated time to reach current location on track | carros 0-3: [1.8738, 67.9659, 0.1132, 67.5569] s | sim |
| CarIdxF2Time | float | 72 | s | Race time behind leader or fastest lap time otherwise | carros 0-3: [0.004, 0.021, 0.016, 0.026] s | sim |
| CarIdxFastRepairsUsed | int | 72 |  | How many fast repairs each car has used | carros 0-3: [0, 0, 0, 0] |  |
| CarIdxGear | int | 72 |  | -1=reverse  0=neutral  1..n=current gear by car index | carros 0-3: [4, 2, 2, 1] |  |
| CarIdxLap | int | 72 |  | Laps started by car index | carros 0-3: [1, 0, 1, 0] | sim |
| CarIdxLapCompleted | int | 72 |  | Laps completed by car index | carros 0-3: [0, -1, 0, -1] | sim |
| CarIdxLapDistPct | float | 72 | % | Percentage distance around lap by car index | carros 0-3: [0.0358, 0.991, 0.0019, 0.9841] % | sim |
| CarIdxLastLapTime | float | 72 | s | Cars last lap time | carros 0-3: [-1, -1, -1, -1] s | sim |
| CarIdxOnPitRoad | bool | 72 |  | On pit road between the cones by car index | carros 0-3: [False, False, False, False] | sim |
| CarIdxP2P_Count | int | 72 |  | Push2Pass count of usage (or remaining in Race) | carros 0-3: [-1, -1, -1, -1] | sim |
| CarIdxP2P_Status | bool | 72 |  | Push2Pass active or not | carros 0-3: [False, False, False, False] | sim |
| CarIdxPaceFlags | bitfield | 72 | irsdk_PaceFlags | Pacing status flags for each car | carro do jogador: 0 (0x0) = nenhum bit |  |
| CarIdxPaceLine | int | 72 |  | What line cars are pacing in  or -1 if not pacing | carros 0-3: [-1, -1, -1, -1] |  |
| CarIdxPaceRow | int | 72 |  | What row cars are pacing in  or -1 if not pacing | carros 0-3: [-1, -1, -1, -1] |  |
| CarIdxPosition | int | 72 |  | Cars position in race by car index | carros 0-3: [5, 22, 17, 27] | sim |
| CarIdxQualTireCompound | int | 72 |  | Cars Qual tire compound | carros 0-3: [0, 0, 0, -1] |  |
| CarIdxQualTireCompoundLocked | bool | 72 |  | Cars Qual tire compound is locked-in | carros 0-3: [False, False, False, False] |  |
| CarIdxRPM | float | 72 | revs/min | Engine rpm by car index | carros 0-3: [7621.1, 6167.2, 7153.6, 7299.94] revs/min |  |
| CarIdxSessionFlags | bitfield | 72 | irsdk_Flags | Session flags for each player | carro do jogador: 262144 (0x40000) = servicible | sim |
| CarIdxSteer | float | 72 | rad | Steering wheel angle by car index | carros 0-3: [0.0639, 0.0374, 0.0239, -0.5523] rad |  |
| CarIdxTireCompound | int | 72 |  | Cars current tire compound | carros 0-3: [0, 0, 0, 0] | sim |
| CarIdxTrackSurface | int | 72 | irsdk_TrkLoc | Track surface type by car index | carro do jogador: 3 = OnTrack |  |
| CarIdxTrackSurfaceMaterial | int | 72 | irsdk_TrkSurf | Track surface material type by car index | carros 0-3: [1, 1, 1, 1] |  |
| CarLeftRight | int | 1 | irsdk_CarLeftRight | Notify if car is to the left or right of driver | 1 = Clear | sim |
| ChanAvgLatency | float | 1 | s | Communications average latency | 0.0333 s |  |
| ChanClockSkew | float | 1 | s | Communications server clock skew | 0 s |  |
| ChanLatency | float | 1 | s | Communications latency | 0.0333 s |  |
| ChanPartnerQuality | float | 1 | % | Partner communications quality | 1 % |  |
| ChanQuality | float | 1 | % | Communications quality | 1.0001 % |  |
| Clutch | float | 1 | % | 0=disengaged to 1=fully engaged | 1 % | sim |
| ClutchRaw | float | 1 | % | Raw clutch input 0=disengaged to 1=fully engaged | 1 % |  |
| CpuUsageBG | float | 1 | % | Percent of available time bg thread took with a 1 sec avg | 0.759 % |  |
| CpuUsageFG | float | 1 | % | Percent of available time fg thread took with a 1 sec avg | 0.6478 % |  |
| DCDriversSoFar | int | 1 |  | Number of team drivers who have run a stint | 1 |  |
| DCLapStatus | int | 1 |  | Status of driver change lap requirements | 2 |  |
| DisplayUnits | int | 1 |  | Default units for the user interface 0 = english 1 = metric | 1 |  |
| DriverMarker | bool | 1 |  | Driver activated flag | False |  |
| EnergyBatteryToMGU_KLap | float | 1 | J | Electrical energy from battery to MGU-K per lap | 160206 J |  |
| EnergyERSBattery | float | 1 | J | Engine ERS battery charge | 4.6384e+06 J |  |
| EnergyERSBatteryPct | float | 1 | % | Engine ERS battery charge as a percent | 0.9544 % |  |
| EnergyMGU_KLapDeployPct | float | 1 | % | Electrical energy available to MGU-K per lap as a percent | 0 % |  |
| Engine0_RPM | float | 1 | revs/min | Engine0Engine rpm | 7621.11 revs/min |  |
| Engine1_RPM | float | 1 | revs/min | Engine1Engine rpm | 7621.11 revs/min |  |
| EngineWarnings | bitfield | 1 | irsdk_EngineWarnings | Bitfield for warning lights | 0 (0x0) = nenhum bit |  |
| EnterExitReset | int | 1 |  | Indicate action the reset key will take 0 enter 1 exit 2 reset | 2 |  |
| FastRepairAvailable | int | 1 |  | How many fast repairs left  255 is unlimited | 0 |  |
| FastRepairUsed | int | 1 |  | How many fast repairs used so far | 0 |  |
| FogLevel | float | 1 | % | Fog level at start/finish line | 0 % |  |
| FrameRate | float | 1 | fps | Average frames per second | 90.5028 fps |  |
| FrontTireSetsAvailable | int | 1 |  | How many front tire sets are remaining  255 is unlimited | 1 |  |
| FrontTireSetsUsed | int | 1 |  | How many front tire sets used so far | 1 |  |
| FuelLevel | float | 1 | l | Liters of fuel remaining | 57.5142 l | sim |
| FuelLevelPct | float | 1 | % | Percent fuel remaining | 0.6462 % | sim |
| FuelPress | float | 1 | bar | Engine fuel pressure | 5.83 bar |  |
| FuelUsePerHour | float | 1 | kg/h | Engine fuel used instantaneous | 112.177 kg/h | sim |
| Gear | int | 1 |  | -1=reverse  0=neutral  1..n=current gear | 4 | sim |
| GpuUsage | float | 1 | % | Percent of available time gpu took with a 1 sec avg | 0.4401 % |  |
| HFshockDefl | float | 1 | m | HF shock deflection | 0.0465 m |  |
| HFshockDefl_ST | float | 6 | m | HF shock deflection at 360 Hz | carros 0-3: [0.0458, 0.046, 0.0462, 0.0463] m |  |
| HFshockVel | float | 1 | m/s | HF shock velocity | 0.0346 m/s |  |
| HFshockVel_ST | float | 6 | m/s | HF shock velocity at 360 Hz | carros 0-3: [0.0695, 0.0427, 0.0213, 0.0051] m/s |  |
| HandbrakeRaw | float | 1 | % | Raw handbrake input 0=handbrake released to 1=max force | 0 % |  |
| IsDiskLoggingActive | bool | 1 |  | 0=disk based telemetry file not being written  1=being written | True |  |
| IsDiskLoggingEnabled | bool | 1 |  | 0=disk based telemetry turned off  1=turned on | True |  |
| IsGarageVisible | bool | 1 |  | 1=Garage screen is visible | False |  |
| IsInGarage | bool | 1 |  | 1=Car in garage physics running | False |  |
| IsOnTrack | bool | 1 |  | 1=Car on track physics running with player in car | True | sim |
| IsOnTrackCar | bool | 1 |  | 1=Car on track physics running | True |  |
| IsReplayPlaying | bool | 1 |  | 0=replay not playing  1=replay playing | False |  |
| LFTiresAvailable | int | 1 |  | How many left front tires are remaining  255 is unlimited | 1 |  |
| LFTiresUsed | int | 1 |  | How many left front tires used so far | 1 |  |
| LFbrakeLinePress | float | 1 | bar | LF brake line pressure | 0 bar |  |
| LFcoldPressure | float | 1 | kPa | LF tire cold pressure  as set in the garage | 151.606 kPa |  |
| LFodometer | float | 1 | m | LF distance tire traveled since being placed on car | 100 m |  |
| LFshockDefl | float | 1 | m | LF shock deflection | 0.0158 m |  |
| LFshockDefl_ST | float | 6 | m | LF shock deflection at 360 Hz | carros 0-3: [0.0156, 0.0157, 0.0157, 0.0157] m |  |
| LFshockVel | float | 1 | m/s | LF shock velocity | 0.0296 m/s |  |
| LFshockVel_ST | float | 6 | m/s | LF shock velocity at 360 Hz | carros 0-3: [0.0299, 0.0197, 0.0105, 0.0084] m/s |  |
| LFtempCL | float | 1 | C | LF tire left carcass temperature | 78.3269 C |  |
| LFtempCM | float | 1 | C | LF tire middle carcass temperature | 83.6539 C |  |
| LFtempCR | float | 1 | C | LF tire right carcass temperature | 86.3208 C |  |
| LFwearL | float | 1 | % | LF tire left percent tread remaining | 0.9902 % |  |
| LFwearM | float | 1 | % | LF tire middle percent tread remaining | 0.9858 % |  |
| LFwearR | float | 1 | % | LF tire right percent tread remaining | 0.9862 % | sim |
| LRTiresAvailable | int | 1 |  | How many left rear tires are remaining  255 is unlimited | 1 |  |
| LRTiresUsed | int | 1 |  | How many left rear tires used so far | 1 |  |
| LRbrakeLinePress | float | 1 | bar | LR brake line pressure | 0 bar |  |
| LRcoldPressure | float | 1 | kPa | LR tire cold pressure  as set in the garage | 151.606 kPa |  |
| LRodometer | float | 1 | m | LR distance tire traveled since being placed on car | 100 m |  |
| LRshockDefl | float | 1 | m | LR shock deflection | 0.0302 m |  |
| LRshockDefl_ST | float | 6 | m | LR shock deflection at 360 Hz | carros 0-3: [0.0299, 0.0299, 0.03, 0.03] m |  |
| LRshockVel | float | 1 | m/s | LR shock velocity | 0.0237 m/s |  |
| LRshockVel_ST | float | 6 | m/s | LR shock velocity at 360 Hz | carros 0-3: [0.0051, 0.007, 0.0162, 0.0258] m/s |  |
| LRtempCL | float | 1 | C | LR tire left carcass temperature | 81.8933 C |  |
| LRtempCM | float | 1 | C | LR tire middle carcass temperature | 86.2442 C |  |
| LRtempCR | float | 1 | C | LR tire right carcass temperature | 87.7234 C |  |
| LRwearL | float | 1 | % | LR tire left percent tread remaining | 0.983 % |  |
| LRwearM | float | 1 | % | LR tire middle percent tread remaining | 0.9787 % |  |
| LRwearR | float | 1 | % | LR tire right percent tread remaining | 0.9793 % |  |
| Lap | int | 1 |  | Laps started count | 1 | sim |
| LapBestLap | int | 1 |  | Players best lap number | 0 |  |
| LapBestLapTime | float | 1 | s | Players best lap time | 0 s |  |
| LapBestNLapLap | int | 1 |  | Player last lap in best N average lap time | 0 |  |
| LapBestNLapTime | float | 1 | s | Player best N average lap time | 0 s |  |
| LapCompleted | int | 1 |  | Laps completed count | 0 |  |
| LapCurrentLapTime | float | 1 | s | Estimate of players current lap time as shown in F3 box | 7.4626 s |  |
| LapDeltaToBestLap | float | 1 | s | Delta time for best lap | 1.7787 s |  |
| LapDeltaToBestLap_DD | float | 1 | s/s | Rate of change of delta time for best lap | 0.3194 s/s |  |
| LapDeltaToBestLap_OK | bool | 1 |  | Delta time for best lap is valid | True |  |
| LapDeltaToOptimalLap | float | 1 | s | Delta time for optimal lap | 1.7805 s |  |
| LapDeltaToOptimalLap_DD | float | 1 | s/s | Rate of change of delta time for optimal lap | 0.3195 s/s |  |
| LapDeltaToOptimalLap_OK | bool | 1 |  | Delta time for optimal lap is valid | True |  |
| LapDeltaToSessionBestLap | float | 1 | s | Delta time for session best lap | 0 s |  |
| LapDeltaToSessionBestLap_DD | float | 1 | s/s | Rate of change of delta time for session best lap | 0 s/s |  |
| LapDeltaToSessionBestLap_OK | bool | 1 |  | Delta time for session best lap is valid | False |  |
| LapDeltaToSessionLastlLap | float | 1 | s | Delta time for session last lap | 0 s |  |
| LapDeltaToSessionLastlLap_DD | float | 1 | s/s | Rate of change of delta time for session last lap | 0 s/s |  |
| LapDeltaToSessionLastlLap_OK | bool | 1 |  | Delta time for session last lap is valid | False |  |
| LapDeltaToSessionOptimalLap | float | 1 | s | Delta time for session optimal lap | 0 s |  |
| LapDeltaToSessionOptimalLap_DD | float | 1 | s/s | Rate of change of delta time for session optimal lap | 0 s/s |  |
| LapDeltaToSessionOptimalLap_OK | bool | 1 |  | Delta time for session optimal lap is valid | False |  |
| LapDist | float | 1 | m | Meters traveled from S/F this lap | 145.209 m |  |
| LapDistPct | float | 1 | % | Percentage distance around lap | 0.0358 % | sim |
| LapLasNLapSeq | int | 1 |  | Player num consecutive clean laps completed for N average | 0 |  |
| LapLastLapTime | float | 1 | s | Players last lap time | 0 s | sim |
| LapLastNLapTime | float | 1 | s | Player last N average lap time | 0 s |  |
| LatAccel | float | 1 | m/s^2 | Lateral acceleration (including gravity) | 3.4043 m/s^2 |  |
| LatAccel_ST | float | 6 | m/s^2 | Lateral acceleration (including gravity) at 360 Hz | carros 0-3: [3.624, 3.5692, 3.5567, 3.542] m/s^2 |  |
| LeftTireSetsAvailable | int | 1 |  | How many left tire sets are remaining  255 is unlimited | 1 |  |
| LeftTireSetsUsed | int | 1 |  | How many left tire sets used so far | 1 |  |
| LoadNumTextures | bool | 1 |  | True if the car_num texture will be loaded | False |  |
| LongAccel | float | 1 | m/s^2 | Longitudinal acceleration (including gravity) | 5.6845 m/s^2 |  |
| LongAccel_ST | float | 6 | m/s^2 | Longitudinal acceleration (including gravity) at 360 Hz | carros 0-3: [5.5564, 5.5619, 5.582, 5.618] m/s^2 |  |
| ManifoldPress | float | 1 | bar | Engine manifold pressure | 0.9816 bar |  |
| ManualBoost | bool | 1 |  | Hybrid manual boost state | False |  |
| ManualNoBoost | bool | 1 |  | Hybrid manual no boost state | False |  |
| MemPageFaultSec | float | 1 |  | Memory page faults per second | 0 |  |
| MemSoftPageFaultSec | float | 1 |  | Memory soft page faults per second | 0 |  |
| OilLevel | float | 1 | l | Engine oil level | 12.68 l |  |
| OilPress | float | 1 | bar | Engine oil pressure | 5.24 bar |  |
| OilTemp | float | 1 | C | Engine oil temperature | 84.6924 C |  |
| OkToReloadTextures | bool | 1 |  | True if it is ok to reload car textures at this time | True |  |
| OnPitRoad | bool | 1 |  | Is the player car on pit road between the cones | False | sim |
| P2P_Count | int | 1 |  | Push2Pass count of usage (or remaining in Race) on your car | 0 | sim |
| P2P_Status | bool | 1 |  | Push2Pass active or not on your car | False |  |
| PaceMode | int | 1 | irsdk_PaceMode | Are we pacing or not | 1 = DoubleFileStart |  |
| PitOptRepairLeft | float | 1 | s | Time left for optional repairs if repairs are active | 0 s |  |
| PitRepairLeft | float | 1 | s | Time left for mandatory pit repairs if repairs are active | 0 s |  |
| PitSvFlags | bitfield | 1 | irsdk_PitSvFlags | Bitfield of pit service checkboxes | 16 (0x10) = fuel | sim |
| PitSvFuel | float | 1 | l or kWh | Pit service fuel add amount | 34 l or kWh | sim |
| PitSvLFP | float | 1 | kPa | Pit service left front tire pressure | 151.606 kPa |  |
| PitSvLRP | float | 1 | kPa | Pit service left rear tire pressure | 151.606 kPa |  |
| PitSvRFP | float | 1 | kPa | Pit service right front tire pressure | 151.606 kPa |  |
| PitSvRRP | float | 1 | kPa | Pit service right rear tire pressure | 151.606 kPa |  |
| PitSvTireCompound | int | 1 |  | Pit service pending tire compound | 0 |  |
| Pitch | float | 1 | rad | Pitch orientation | 0.0125 rad |  |
| PitchRate | float | 1 | rad/s | Pitch rate | -0.0567 rad/s |  |
| PitchRate_ST | float | 6 | rad/s | Pitch rate at 360 Hz | carros 0-3: [-0.0419, -0.0469, -0.0509, -0.0536] rad/s |  |
| PitsOpen | bool | 1 |  | True if pit stop is allowed for the current player | True |  |
| PitstopActive | bool | 1 |  | Is the player getting pit stop service | False |  |
| PlayerCarClass | int | 1 |  | Player car class id | 4029 |  |
| PlayerCarClassPosition | int | 1 |  | Players class position in race | 5 |  |
| PlayerCarDriverIncidentCount | int | 1 |  | Teams current drivers incident count for this session | 0 |  |
| PlayerCarDryTireSetLimit | int | 1 |  | Players dry tire set limit | 0 |  |
| PlayerCarIdx | int | 1 |  | Players carIdx | 0 | sim |
| PlayerCarInPitStall | bool | 1 |  | Players car is properly in their pitstall | False |  |
| PlayerCarMyIncidentCount | int | 1 |  | Players own incident count for this session | 0 | sim |
| PlayerCarPitSvStatus | int | 1 | irsdk_PitSvStatus | Players car pit service status bits | 0 = None |  |
| PlayerCarPosition | int | 1 |  | Players position in race | 5 |  |
| PlayerCarPowerAdjust | float | 1 | % | Players power adjust | 0 % |  |
| PlayerCarSLBlinkRPM | float | 1 | revs/min | Shift light blink rpm | 8725 revs/min |  |
| PlayerCarSLFirstRPM | float | 1 | revs/min | Shift light first light rpm | 8300 revs/min |  |
| PlayerCarSLLastRPM | float | 1 | revs/min | Shift light last light rpm | 8650 revs/min |  |
| PlayerCarSLShiftRPM | float | 1 | revs/min | Shift light shift rpm | 8550 revs/min |  |
| PlayerCarTeamIncidentCount | int | 1 |  | Players team incident count for this session | 0 |  |
| PlayerCarTowTime | float | 1 | s | Players car is being towed if time is greater than zero | 0 s | sim |
| PlayerCarWeightPenalty | float | 1 | kg | Players weight penalty | 0 kg |  |
| PlayerFastRepairsUsed | int | 1 |  | Players car number of fast repairs used | 0 |  |
| PlayerIncidents | int | 1 | irsdk_IncidentFlags | Log incidents that the player recieved | 0 |  |
| PlayerTireCompound | int | 1 |  | Players car current tire compound | 0 |  |
| PlayerTrackSurface | int | 1 | irsdk_TrkLoc | Players car track surface type | 3 = OnTrack | sim |
| PlayerTrackSurfaceMaterial | int | 1 | irsdk_TrkSurf | Players car track surface material type | 1 |  |
| PowerMGU_H | float | 1 | W | Engine MGU-H mechanical power | -0 W |  |
| PowerMGU_K | float | 1 | W | Engine MGU-K mechanical power | 40617 W |  |
| Precipitation | float | 1 | % | Precipitation at start/finish line | 0 % | sim |
| PushToPass | bool | 1 |  | Push to pass button state | False |  |
| PushToTalk | bool | 1 |  | Push to talk button state | False |  |
| RFTiresAvailable | int | 1 |  | How many right front tires are remaining  255 is unlimited | 1 |  |
| RFTiresUsed | int | 1 |  | How many right front tires used so far | 1 |  |
| RFbrakeLinePress | float | 1 | bar | RF brake line pressure | 0 bar |  |
| RFcoldPressure | float | 1 | kPa | RF tire cold pressure  as set in the garage | 151.606 kPa |  |
| RFodometer | float | 1 | m | RF distance tire traveled since being placed on car | 100 m |  |
| RFshockDefl | float | 1 | m | RF shock deflection | 0.0177 m |  |
| RFshockDefl_ST | float | 6 | m | RF shock deflection at 360 Hz | carros 0-3: [0.0174, 0.0175, 0.0175, 0.0176] m |  |
| RFshockVel | float | 1 | m/s | RF shock velocity | -0.0047 m/s |  |
| RFshockVel_ST | float | 6 | m/s | RF shock velocity at 360 Hz | carros 0-3: [0.0201, 0.011, 0.0048, -0.0047] m/s |  |
| RFtempCL | float | 1 | C | RF tire left carcass temperature | 78.2775 C |  |
| RFtempCM | float | 1 | C | RF tire middle carcass temperature | 75.7167 C |  |
| RFtempCR | float | 1 | C | RF tire right carcass temperature | 66.3126 C |  |
| RFwearL | float | 1 | % | RF tire left percent tread remaining | 0.9887 % |  |
| RFwearM | float | 1 | % | RF tire middle percent tread remaining | 0.9882 % |  |
| RFwearR | float | 1 | % | RF tire right percent tread remaining | 0.9955 % |  |
| RPM | float | 1 | revs/min | Engine rpm | 7621.1 revs/min | sim |
| RRTiresAvailable | int | 1 |  | How many right rear tires are remaining  255 is unlimited | 1 |  |
| RRTiresUsed | int | 1 |  | How many right rear tires used so far | 1 |  |
| RRbrakeLinePress | float | 1 | bar | RR brake line pressure | 0 bar |  |
| RRcoldPressure | float | 1 | kPa | RR tire cold pressure  as set in the garage | 151.606 kPa |  |
| RRodometer | float | 1 | m | RR distance tire traveled since being placed on car | 100 m |  |
| RRshockDefl | float | 1 | m | RR shock deflection | 0.0318 m |  |
| RRshockDefl_ST | float | 6 | m | RR shock deflection at 360 Hz | carros 0-3: [0.0318, 0.0318, 0.0317, 0.0317] m |  |
| RRshockVel | float | 1 | m/s | RR shock velocity | 0.0192 m/s |  |
| RRshockVel_ST | float | 6 | m/s | RR shock velocity at 360 Hz | carros 0-3: [-0.0128, -0.0056, 0.0034, 0.0108] m/s |  |
| RRtempCL | float | 1 | C | RR tire left carcass temperature | 82.3492 C |  |
| RRtempCM | float | 1 | C | RR tire middle carcass temperature | 80.2213 C |  |
| RRtempCR | float | 1 | C | RR tire right carcass temperature | 70.8238 C |  |
| RRwearL | float | 1 | % | RR tire left percent tread remaining | 0.9818 % |  |
| RRwearM | float | 1 | % | RR tire middle percent tread remaining | 0.9814 % |  |
| RRwearR | float | 1 | % | RR tire right percent tread remaining | 0.9929 % |  |
| RaceLaps | int | 1 |  | Laps completed in race | 1 |  |
| RadioTransmitCarIdx | int | 1 |  | The car index of the current person speaking on the radio | -1 |  |
| RadioTransmitFrequencyIdx | int | 1 |  | The frequency index of the current person speaking on the radio | 1 |  |
| RadioTransmitRadioIdx | int | 1 |  | The radio index of the current person speaking on the radio | 0 |  |
| RearTireSetsAvailable | int | 1 |  | How many rear tire sets are remaining  255 is unlimited | 1 |  |
| RearTireSetsUsed | int | 1 |  | How many rear tire sets used so far | 1 |  |
| RelativeHumidity | float | 1 | % | Relative Humidity at start/finish line | 0.7319 % | sim |
| ReplayFrameNum | int | 1 |  | Integer replay frame number (60 per second) | 0 |  |
| ReplayFrameNumEnd | int | 1 |  | Integer replay frame number from end of tape | 28086 |  |
| ReplayPlaySlowMotion | bool | 1 |  | 0=not slow motion  1=replay is in slow motion | False |  |
| ReplayPlaySpeed | int | 1 |  | Replay playback speed | 1 |  |
| ReplaySessionNum | int | 1 |  | Replay session number | -1 |  |
| ReplaySessionTime | double | 1 | s | Seconds since replay session start | 0 s |  |
| RightTireSetsAvailable | int | 1 |  | How many right tire sets are remaining  255 is unlimited | 1 |  |
| RightTireSetsUsed | int | 1 |  | How many right tire sets used so far | 1 |  |
| Roll | float | 1 | rad | Roll orientation | 0.0069 rad |  |
| RollRate | float | 1 | rad/s | Roll rate | 0.0124 rad/s |  |
| RollRate_ST | float | 6 | rad/s | Roll rate at 360 Hz | carros 0-3: [-0.0017, 0.0001, 0.0018, 0.0028] rad/s |  |
| SessionFlags | bitfield | 1 | irsdk_Flags | Session flags | 268697636 (0x10040024) = green + blue + servicible + startHidden | sim |
| SessionJokerLapsRemain | int | 1 |  | Joker laps remaining to be taken | 0 |  |
| SessionLapsRemain | int | 1 |  | Old laps left till session ends use SessionLapsRemainEx | 32767 |  |
| SessionLapsRemainEx | int | 1 |  | New improved laps left till session ends | 32767 |  |
| SessionLapsTotal | int | 1 |  | Total number of laps in session | 32767 |  |
| SessionNum | int | 1 |  | Session number | 2 | sim |
| SessionOnJokerLap | bool | 1 |  | Player is currently completing a joker lap | False |  |
| SessionState | int | 1 | irsdk_SessionState | Session state | 4 = Racing | sim |
| SessionTick | int | 1 |  | Current update number | 40078 |  |
| SessionTime | double | 1 | s | Seconds since session start | 87.2833 s | sim |
| SessionTimeOfDay | float | 1 | s | Time of day in seconds | 63867 s |  |
| SessionTimeRemain | double | 1 | s | Seconds left till session ends | 2692.52 s | sim |
| SessionTimeTotal | double | 1 | s | Total number of seconds in session | 2700 s |  |
| SessionUniqueID | int | 1 |  | Session ID | 3 | sim |
| ShiftGrindRPM | float | 1 | RPM | RPM of shifter grinding noise | 0 RPM |  |
| ShiftIndicatorPct | float | 1 | % | DEPRECATED use DriverCarSLBlinkRPM instead | 0 % |  |
| ShiftPowerPct | float | 1 | % | Friction torque applied to gears when shifting or grinding | 0 % |  |
| Shifter | int | 1 |  | Log inputs from the players shifter control | 0 |  |
| Skies | int | 1 |  | Skies (0=clear/1=p cloudy/2=m cloudy/3=overcast) | 1 |  |
| SolarAltitude | float | 1 | rad | Sun angle above horizon in radians | 0.3483 rad |  |
| SolarAzimuth | float | 1 | rad | Sun angle clockwise from north in radians | 4.4263 rad |  |
| Speed | float | 1 | m/s | GPS vehicle speed | 52.8334 m/s | sim |
| SteeringFFBEnabled | bool | 1 |  | Force feedback is enabled | True |  |
| SteeringWheelAngle | float | 1 | rad | Steering wheel angle | 0.0639 rad | sim |
| SteeringWheelAngleMax | float | 1 | rad | Steering wheel max angle | 9.4255 rad |  |
| SteeringWheelLimiter | float | 1 | % | Force feedback limiter strength limits impacts and oscillation | 0 % |  |
| SteeringWheelMaxForceNm | float | 1 | N*m | Value of strength or max force slider in Nm for FFB | 33.2546 N*m |  |
| SteeringWheelPctDamper | float | 1 | % | Force feedback % max damping | 0 % |  |
| SteeringWheelPctIntensity | float | 1 | % | Force feedback % max intensity | 1 % |  |
| SteeringWheelPctSmoothing | float | 1 | % | Force feedback % max smoothing | 0 % |  |
| SteeringWheelPctTorque | float | 1 | % | Force feedback % max torque on steering shaft unsigned | 0.1174 % |  |
| SteeringWheelPctTorqueSign | float | 1 | % | Force feedback % max torque on steering shaft signed | -0.1174 % |  |
| SteeringWheelPctTorqueSignStops | float | 1 | % | Force feedback % max torque on steering shaft signed stops | -0.1174 % |  |
| SteeringWheelPeakForceNm | float | 1 | N*m | Peak torque mapping to direct input units for FFB | -1 N*m |  |
| SteeringWheelTorque | float | 1 | N*m | Output torque on steering shaft | -3.9047 N*m |  |
| SteeringWheelTorque_ST | float | 6 | N*m | Output torque on steering shaft at 360 Hz | carros 0-3: [-3.8967, -3.8869, -3.9816, -4.015] N*m |  |
| SteeringWheelUseLinear | bool | 1 |  | True if steering wheel force is using linear mode | True |  |
| Throttle | float | 1 | % | 0=off throttle to 1=full throttle | 1 % | sim |
| ThrottleRaw | float | 1 | % | Raw throttle input 0=off throttle to 1=full throttle | 1 % |  |
| TireLF_RumblePitch | float | 1 | Hz | Players LF Tire Sound rumblestrip pitch | 0 Hz |  |
| TireLR_RumblePitch | float | 1 | Hz | Players LR Tire Sound rumblestrip pitch | 0 Hz |  |
| TireRF_RumblePitch | float | 1 | Hz | Players RF Tire Sound rumblestrip pitch | 0 Hz |  |
| TireRR_RumblePitch | float | 1 | Hz | Players RR Tire Sound rumblestrip pitch | 0 Hz |  |
| TireSetsAvailable | int | 1 |  | How many tire sets are remaining  255 is unlimited | 1 |  |
| TireSetsUsed | int | 1 |  | How many tire sets used so far | 1 |  |
| TorqueMGU_K | float | 1 | Nm | Engine MGU-K mechanical torque | 50.8934 Nm |  |
| TrackTemp | float | 1 | C | Deprecated  set to TrackTempCrew | 26.6667 C | sim |
| TrackTempCrew | float | 1 | C | Temperature of track measured by crew around track | 26.6667 C |  |
| TrackWetness | int | 1 | irsdk_TrackWetness | How wet is the average track surface | 1 = Dry | sim |
| VelocityX | float | 1 | m/s | X velocity | 52.8333 m/s |  |
| VelocityX_ST | float | 6 | m/s at 360 Hz | X velocity | carros 0-3: [52.7536, 52.7693, 52.7852, 52.8011] m/s at 360 Hz |  |
| VelocityY | float | 1 | m/s | Y velocity | -0.0797 m/s |  |
| VelocityY_ST | float | 6 | m/s at 360 Hz | Y velocity | carros 0-3: [-0.0708, -0.0723, -0.0739, -0.0756] m/s at 360 Hz |  |
| VelocityZ | float | 1 | m/s | Z velocity | 0.0488 m/s |  |
| VelocityZ_ST | float | 6 | m/s at 360 Hz | Z velocity | carros 0-3: [0.0676, 0.0648, 0.0611, 0.057] m/s at 360 Hz |  |
| VertAccel | float | 1 | m/s^2 | Vertical acceleration (including gravity) | 11.3071 m/s^2 |  |
| VertAccel_ST | float | 6 | m/s^2 | Vertical acceleration (including gravity) at 360 Hz | carros 0-3: [11.2555, 11.1391, 11.0165, 11.0499] m/s^2 |  |
| VidCapActive | bool | 1 |  | True if video currently being captured | False |  |
| VidCapEnabled | bool | 1 |  | True if video capture system is enabled | False |  |
| Voltage | float | 1 | V | Engine voltage | 13.4 V |  |
| WaterLevel | float | 1 | l | Engine coolant level | 7 l |  |
| WaterTemp | float | 1 | C | Engine coolant temp | 89.3046 C |  |
| WeatherDeclaredWet | bool | 1 |  | The steward says rain tires can be used | False | sim |
| WindDir | float | 1 | rad | Wind direction at start/finish line | 2.8629 rad | sim |
| WindVel | float | 1 | m/s | Wind velocity at start/finish line | 4.4698 m/s | sim |
| Yaw | float | 1 | rad | Yaw orientation | -1.5528 rad |  |
| YawNorth | float | 1 | rad | Yaw orientation relative to north | 2.1131 rad |  |
| YawRate | float | 1 | rad/s | Yaw rate | 0.0781 rad/s |  |
| YawRate_ST | float | 6 | rad/s | Yaw rate at 360 Hz | carros 0-3: [0.0758, 0.0766, 0.0772, 0.0777] rad/s |  |
| dcABS | float | 1 |  | In car abs adjustment | 0 |  |
| dcAntiRollFront | float | 1 |  | In car front anti roll bar adjustment | 1 |  |
| dcAntiRollRear | float | 1 |  | In car rear anti roll bar adjustment | 1 |  |
| dcBrakeBias | float | 1 |  | In car brake bias adjustment | 47 | sim |
| dcBrakeMisc | float | 1 |  | In car brake misc adjustment | 0 |  |
| dcHeadlightFlash | bool | 1 |  | In car headlight flash control active | False |  |
| dcLowFuelAccept | bool | 1 |  | In car low fuel accept | False |  |
| dcPitSpeedLimiterToggle | bool | 1 |  | Track if pit speed limiter system is enabled | False |  |
| dcStarter | bool | 1 |  | In car trigger car starter | False |  |
| dcToggleWindshieldWipers | bool | 1 |  | In car turn wipers on or off | False |  |
| dcTractionControl | float | 1 |  | In car traction control adjustment | 4 |  |
| dcTractionControl2 | float | 1 |  | In car traction control 2 adjustment | 4 |  |
| dcTractionControlToggle | bool | 1 |  | In car traction control active | False |  |
| dcTriggerWindshieldWipers | bool | 1 |  | In car momentarily turn on wipers | False |  |
| dpFastRepair | float | 1 |  | Pitstop fast repair set | 0 |  |
| dpFuelAddKg | float | 1 | kg | Pitstop fuel add amount | 34 kg |  |
| dpFuelAutoFillActive | float | 1 |  | Pitstop auto fill fuel next stop flag | 1 |  |
| dpFuelAutoFillEnabled | float | 1 |  | Pitstop auto fill fuel system enabled | 1 |  |
| dpFuelFill | float | 1 |  | Pitstop fuel fill flag | 1 |  |
| dpLFTireChange | float | 1 |  | Pitstop lf tire change request | 0 |  |
| dpLFTireColdPress | float | 1 | Pa | Pitstop lf tire cold pressure adjustment | 151.606 Pa |  |
| dpLRTireChange | float | 1 |  | Pitstop lr tire change request | 0 |  |
| dpLRTireColdPress | float | 1 | Pa | Pitstop lr tire cold pressure adjustment | 151.606 Pa |  |
| dpRFTireChange | float | 1 |  | Pitstop rf tire change request | 0 |  |
| dpRFTireColdPress | float | 1 | Pa | Pitstop rf cold tire pressure adjustment | 151.606 Pa |  |
| dpRRTireChange | float | 1 |  | Pitstop rr tire change request | 0 |  |
| dpRRTireColdPress | float | 1 | Pa | Pitstop rr cold tire pressure adjustment | 151.606 Pa |  |
| dpWindshieldTearoff | float | 1 |  | Pitstop windshield tearoff | 0 |  |

## SessionInfo (seções)

| Seção | Chaves |
|---|---|
| WeekendInfo | 62 |
| SessionInfo | 2 |
| QualifyResultsInfo | 1 |
| CameraInfo | 1 |
| RadioInfo | 2 |
| DriverInfo | 35 |
| SplitTimeInfo | 1 |
| CarSetup | 4 |