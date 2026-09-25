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
- `CarIdxTrackSurface` = `3` → na pista. `-1` fora do mundo, `0` fora da pista, `1` box (parada), `2` pit lane.

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

| Nome | Tipo | Qtd | Unidade | Descrição | Usada |
|---|---|---|---|---|---|
| AirDensity | float | 1 | kg/m^3 | Density of air at start/finish line |  |
| AirPressure | float | 1 | Pa | Pressure of air at start/finish line |  |
| AirTemp | float | 1 | C | Temperature of air at start/finish line | sim |
| Brake | float | 1 | % | 0=brake released to 1=max pedal force | sim |
| BrakeABSactive | bool | 1 |  | true if abs is currently reducing brake force pressure |  |
| BrakeRaw | float | 1 | % | Raw brake input 0=brake released to 1=max pedal force |  |
| CamCameraNumber | int | 1 |  | Active camera number |  |
| CamCameraState | bitfield | 1 | irsdk_CameraState | State of camera system |  |
| CamCarIdx | int | 1 |  | Active camera's focus car index |  |
| CamGroupNumber | int | 1 |  | Active camera group number |  |
| CarDistAhead | float | 1 | m | Distance to first car in front of player in meters |  |
| CarDistBehind | float | 1 | m | Distance to first car behind player in meters |  |
| CarIdxBestLapNum | int | 72 |  | Cars best lap number |  |
| CarIdxBestLapTime | float | 72 | s | Cars best lap time | sim |
| CarIdxClass | int | 72 |  | Cars class id by car index | sim |
| CarIdxClassPosition | int | 72 |  | Cars class position in race by car index | sim |
| CarIdxEstTime | float | 72 | s | Estimated time to reach current location on track | sim |
| CarIdxF2Time | float | 72 | s | Race time behind leader or fastest lap time otherwise | sim |
| CarIdxFastRepairsUsed | int | 72 |  | How many fast repairs each car has used |  |
| CarIdxGear | int | 72 |  | -1=reverse  0=neutral  1..n=current gear by car index |  |
| CarIdxLap | int | 72 |  | Laps started by car index | sim |
| CarIdxLapCompleted | int | 72 |  | Laps completed by car index | sim |
| CarIdxLapDistPct | float | 72 | % | Percentage distance around lap by car index | sim |
| CarIdxLastLapTime | float | 72 | s | Cars last lap time | sim |
| CarIdxOnPitRoad | bool | 72 |  | On pit road between the cones by car index | sim |
| CarIdxP2P_Count | int | 72 |  | Push2Pass count of usage (or remaining in Race) | sim |
| CarIdxP2P_Status | bool | 72 |  | Push2Pass active or not | sim |
| CarIdxPaceFlags | bitfield | 72 | irsdk_PaceFlags | Pacing status flags for each car |  |
| CarIdxPaceLine | int | 72 |  | What line cars are pacing in  or -1 if not pacing |  |
| CarIdxPaceRow | int | 72 |  | What row cars are pacing in  or -1 if not pacing |  |
| CarIdxPosition | int | 72 |  | Cars position in race by car index | sim |
| CarIdxQualTireCompound | int | 72 |  | Cars Qual tire compound |  |
| CarIdxQualTireCompoundLocked | bool | 72 |  | Cars Qual tire compound is locked-in |  |
| CarIdxRPM | float | 72 | revs/min | Engine rpm by car index |  |
| CarIdxSessionFlags | bitfield | 72 | irsdk_Flags | Session flags for each player | sim |
| CarIdxSteer | float | 72 | rad | Steering wheel angle by car index |  |
| CarIdxTireCompound | int | 72 |  | Cars current tire compound | sim |
| CarIdxTrackSurface | int | 72 | irsdk_TrkLoc | Track surface type by car index |  |
| CarIdxTrackSurfaceMaterial | int | 72 | irsdk_TrkSurf | Track surface material type by car index |  |
| CarLeftRight | int | 1 | irsdk_CarLeftRight | Notify if car is to the left or right of driver | sim |
| ChanAvgLatency | float | 1 | s | Communications average latency |  |
| ChanClockSkew | float | 1 | s | Communications server clock skew |  |
| ChanLatency | float | 1 | s | Communications latency |  |
| ChanPartnerQuality | float | 1 | % | Partner communications quality |  |
| ChanQuality | float | 1 | % | Communications quality |  |
| Clutch | float | 1 | % | 0=disengaged to 1=fully engaged | sim |
| ClutchRaw | float | 1 | % | Raw clutch input 0=disengaged to 1=fully engaged |  |
| CpuUsageBG | float | 1 | % | Percent of available time bg thread took with a 1 sec avg |  |
| CpuUsageFG | float | 1 | % | Percent of available time fg thread took with a 1 sec avg |  |
| DCDriversSoFar | int | 1 |  | Number of team drivers who have run a stint |  |
| DCLapStatus | int | 1 |  | Status of driver change lap requirements |  |
| DisplayUnits | int | 1 |  | Default units for the user interface 0 = english 1 = metric |  |
| DriverMarker | bool | 1 |  | Driver activated flag |  |
| EnergyBatteryToMGU_KLap | float | 1 | J | Electrical energy from battery to MGU-K per lap |  |
| EnergyERSBattery | float | 1 | J | Engine ERS battery charge |  |
| EnergyERSBatteryPct | float | 1 | % | Engine ERS battery charge as a percent |  |
| EnergyMGU_KLapDeployPct | float | 1 | % | Electrical energy available to MGU-K per lap as a percent |  |
| Engine0_RPM | float | 1 | revs/min | Engine0Engine rpm |  |
| Engine1_RPM | float | 1 | revs/min | Engine1Engine rpm |  |
| EngineWarnings | bitfield | 1 | irsdk_EngineWarnings | Bitfield for warning lights |  |
| EnterExitReset | int | 1 |  | Indicate action the reset key will take 0 enter 1 exit 2 reset |  |
| FastRepairAvailable | int | 1 |  | How many fast repairs left  255 is unlimited |  |
| FastRepairUsed | int | 1 |  | How many fast repairs used so far |  |
| FogLevel | float | 1 | % | Fog level at start/finish line |  |
| FrameRate | float | 1 | fps | Average frames per second |  |
| FrontTireSetsAvailable | int | 1 |  | How many front tire sets are remaining  255 is unlimited |  |
| FrontTireSetsUsed | int | 1 |  | How many front tire sets used so far |  |
| FuelLevel | float | 1 | l | Liters of fuel remaining | sim |
| FuelLevelPct | float | 1 | % | Percent fuel remaining | sim |
| FuelPress | float | 1 | bar | Engine fuel pressure |  |
| FuelUsePerHour | float | 1 | kg/h | Engine fuel used instantaneous | sim |
| Gear | int | 1 |  | -1=reverse  0=neutral  1..n=current gear | sim |
| GpuUsage | float | 1 | % | Percent of available time gpu took with a 1 sec avg |  |
| HFshockDefl | float | 1 | m | HF shock deflection |  |
| HFshockDefl_ST | float | 6 | m | HF shock deflection at 360 Hz |  |
| HFshockVel | float | 1 | m/s | HF shock velocity |  |
| HFshockVel_ST | float | 6 | m/s | HF shock velocity at 360 Hz |  |
| HandbrakeRaw | float | 1 | % | Raw handbrake input 0=handbrake released to 1=max force |  |
| IsDiskLoggingActive | bool | 1 |  | 0=disk based telemetry file not being written  1=being written |  |
| IsDiskLoggingEnabled | bool | 1 |  | 0=disk based telemetry turned off  1=turned on |  |
| IsGarageVisible | bool | 1 |  | 1=Garage screen is visible |  |
| IsInGarage | bool | 1 |  | 1=Car in garage physics running |  |
| IsOnTrack | bool | 1 |  | 1=Car on track physics running with player in car | sim |
| IsOnTrackCar | bool | 1 |  | 1=Car on track physics running |  |
| IsReplayPlaying | bool | 1 |  | 0=replay not playing  1=replay playing |  |
| LFTiresAvailable | int | 1 |  | How many left front tires are remaining  255 is unlimited |  |
| LFTiresUsed | int | 1 |  | How many left front tires used so far |  |
| LFbrakeLinePress | float | 1 | bar | LF brake line pressure |  |
| LFcoldPressure | float | 1 | kPa | LF tire cold pressure  as set in the garage |  |
| LFodometer | float | 1 | m | LF distance tire traveled since being placed on car |  |
| LFshockDefl | float | 1 | m | LF shock deflection |  |
| LFshockDefl_ST | float | 6 | m | LF shock deflection at 360 Hz |  |
| LFshockVel | float | 1 | m/s | LF shock velocity |  |
| LFshockVel_ST | float | 6 | m/s | LF shock velocity at 360 Hz |  |
| LFtempCL | float | 1 | C | LF tire left carcass temperature |  |
| LFtempCM | float | 1 | C | LF tire middle carcass temperature |  |
| LFtempCR | float | 1 | C | LF tire right carcass temperature |  |
| LFwearL | float | 1 | % | LF tire left percent tread remaining |  |
| LFwearM | float | 1 | % | LF tire middle percent tread remaining |  |
| LFwearR | float | 1 | % | LF tire right percent tread remaining | sim |
| LRTiresAvailable | int | 1 |  | How many left rear tires are remaining  255 is unlimited |  |
| LRTiresUsed | int | 1 |  | How many left rear tires used so far |  |
| LRbrakeLinePress | float | 1 | bar | LR brake line pressure |  |
| LRcoldPressure | float | 1 | kPa | LR tire cold pressure  as set in the garage |  |
| LRodometer | float | 1 | m | LR distance tire traveled since being placed on car |  |
| LRshockDefl | float | 1 | m | LR shock deflection |  |
| LRshockDefl_ST | float | 6 | m | LR shock deflection at 360 Hz |  |
| LRshockVel | float | 1 | m/s | LR shock velocity |  |
| LRshockVel_ST | float | 6 | m/s | LR shock velocity at 360 Hz |  |
| LRtempCL | float | 1 | C | LR tire left carcass temperature |  |
| LRtempCM | float | 1 | C | LR tire middle carcass temperature |  |
| LRtempCR | float | 1 | C | LR tire right carcass temperature |  |
| LRwearL | float | 1 | % | LR tire left percent tread remaining |  |
| LRwearM | float | 1 | % | LR tire middle percent tread remaining |  |
| LRwearR | float | 1 | % | LR tire right percent tread remaining |  |
| Lap | int | 1 |  | Laps started count | sim |
| LapBestLap | int | 1 |  | Players best lap number |  |
| LapBestLapTime | float | 1 | s | Players best lap time |  |
| LapBestNLapLap | int | 1 |  | Player last lap in best N average lap time |  |
| LapBestNLapTime | float | 1 | s | Player best N average lap time |  |
| LapCompleted | int | 1 |  | Laps completed count |  |
| LapCurrentLapTime | float | 1 | s | Estimate of players current lap time as shown in F3 box |  |
| LapDeltaToBestLap | float | 1 | s | Delta time for best lap |  |
| LapDeltaToBestLap_DD | float | 1 | s/s | Rate of change of delta time for best lap |  |
| LapDeltaToBestLap_OK | bool | 1 |  | Delta time for best lap is valid |  |
| LapDeltaToOptimalLap | float | 1 | s | Delta time for optimal lap |  |
| LapDeltaToOptimalLap_DD | float | 1 | s/s | Rate of change of delta time for optimal lap |  |
| LapDeltaToOptimalLap_OK | bool | 1 |  | Delta time for optimal lap is valid |  |
| LapDeltaToSessionBestLap | float | 1 | s | Delta time for session best lap |  |
| LapDeltaToSessionBestLap_DD | float | 1 | s/s | Rate of change of delta time for session best lap |  |
| LapDeltaToSessionBestLap_OK | bool | 1 |  | Delta time for session best lap is valid |  |
| LapDeltaToSessionLastlLap | float | 1 | s | Delta time for session last lap |  |
| LapDeltaToSessionLastlLap_DD | float | 1 | s/s | Rate of change of delta time for session last lap |  |
| LapDeltaToSessionLastlLap_OK | bool | 1 |  | Delta time for session last lap is valid |  |
| LapDeltaToSessionOptimalLap | float | 1 | s | Delta time for session optimal lap |  |
| LapDeltaToSessionOptimalLap_DD | float | 1 | s/s | Rate of change of delta time for session optimal lap |  |
| LapDeltaToSessionOptimalLap_OK | bool | 1 |  | Delta time for session optimal lap is valid |  |
| LapDist | float | 1 | m | Meters traveled from S/F this lap |  |
| LapDistPct | float | 1 | % | Percentage distance around lap | sim |
| LapLasNLapSeq | int | 1 |  | Player num consecutive clean laps completed for N average |  |
| LapLastLapTime | float | 1 | s | Players last lap time | sim |
| LapLastNLapTime | float | 1 | s | Player last N average lap time |  |
| LatAccel | float | 1 | m/s^2 | Lateral acceleration (including gravity) |  |
| LatAccel_ST | float | 6 | m/s^2 | Lateral acceleration (including gravity) at 360 Hz |  |
| LeftTireSetsAvailable | int | 1 |  | How many left tire sets are remaining  255 is unlimited |  |
| LeftTireSetsUsed | int | 1 |  | How many left tire sets used so far |  |
| LoadNumTextures | bool | 1 |  | True if the car_num texture will be loaded |  |
| LongAccel | float | 1 | m/s^2 | Longitudinal acceleration (including gravity) |  |
| LongAccel_ST | float | 6 | m/s^2 | Longitudinal acceleration (including gravity) at 360 Hz |  |
| ManifoldPress | float | 1 | bar | Engine manifold pressure |  |
| ManualBoost | bool | 1 |  | Hybrid manual boost state |  |
| ManualNoBoost | bool | 1 |  | Hybrid manual no boost state |  |
| MemPageFaultSec | float | 1 |  | Memory page faults per second |  |
| MemSoftPageFaultSec | float | 1 |  | Memory soft page faults per second |  |
| OilLevel | float | 1 | l | Engine oil level |  |
| OilPress | float | 1 | bar | Engine oil pressure |  |
| OilTemp | float | 1 | C | Engine oil temperature |  |
| OkToReloadTextures | bool | 1 |  | True if it is ok to reload car textures at this time |  |
| OnPitRoad | bool | 1 |  | Is the player car on pit road between the cones | sim |
| P2P_Count | int | 1 |  | Push2Pass count of usage (or remaining in Race) on your car | sim |
| P2P_Status | bool | 1 |  | Push2Pass active or not on your car |  |
| PaceMode | int | 1 | irsdk_PaceMode | Are we pacing or not |  |
| PitOptRepairLeft | float | 1 | s | Time left for optional repairs if repairs are active |  |
| PitRepairLeft | float | 1 | s | Time left for mandatory pit repairs if repairs are active |  |
| PitSvFlags | bitfield | 1 | irsdk_PitSvFlags | Bitfield of pit service checkboxes | sim |
| PitSvFuel | float | 1 | l or kWh | Pit service fuel add amount | sim |
| PitSvLFP | float | 1 | kPa | Pit service left front tire pressure |  |
| PitSvLRP | float | 1 | kPa | Pit service left rear tire pressure |  |
| PitSvRFP | float | 1 | kPa | Pit service right front tire pressure |  |
| PitSvRRP | float | 1 | kPa | Pit service right rear tire pressure |  |
| PitSvTireCompound | int | 1 |  | Pit service pending tire compound |  |
| Pitch | float | 1 | rad | Pitch orientation |  |
| PitchRate | float | 1 | rad/s | Pitch rate |  |
| PitchRate_ST | float | 6 | rad/s | Pitch rate at 360 Hz |  |
| PitsOpen | bool | 1 |  | True if pit stop is allowed for the current player |  |
| PitstopActive | bool | 1 |  | Is the player getting pit stop service |  |
| PlayerCarClass | int | 1 |  | Player car class id |  |
| PlayerCarClassPosition | int | 1 |  | Players class position in race |  |
| PlayerCarDriverIncidentCount | int | 1 |  | Teams current drivers incident count for this session |  |
| PlayerCarDryTireSetLimit | int | 1 |  | Players dry tire set limit |  |
| PlayerCarIdx | int | 1 |  | Players carIdx | sim |
| PlayerCarInPitStall | bool | 1 |  | Players car is properly in their pitstall |  |
| PlayerCarMyIncidentCount | int | 1 |  | Players own incident count for this session | sim |
| PlayerCarPitSvStatus | int | 1 | irsdk_PitSvStatus | Players car pit service status bits |  |
| PlayerCarPosition | int | 1 |  | Players position in race |  |
| PlayerCarPowerAdjust | float | 1 | % | Players power adjust |  |
| PlayerCarSLBlinkRPM | float | 1 | revs/min | Shift light blink rpm |  |
| PlayerCarSLFirstRPM | float | 1 | revs/min | Shift light first light rpm |  |
| PlayerCarSLLastRPM | float | 1 | revs/min | Shift light last light rpm |  |
| PlayerCarSLShiftRPM | float | 1 | revs/min | Shift light shift rpm |  |
| PlayerCarTeamIncidentCount | int | 1 |  | Players team incident count for this session |  |
| PlayerCarTowTime | float | 1 | s | Players car is being towed if time is greater than zero | sim |
| PlayerCarWeightPenalty | float | 1 | kg | Players weight penalty |  |
| PlayerFastRepairsUsed | int | 1 |  | Players car number of fast repairs used |  |
| PlayerIncidents | int | 1 | irsdk_IncidentFlags | Log incidents that the player recieved |  |
| PlayerTireCompound | int | 1 |  | Players car current tire compound |  |
| PlayerTrackSurface | int | 1 | irsdk_TrkLoc | Players car track surface type | sim |
| PlayerTrackSurfaceMaterial | int | 1 | irsdk_TrkSurf | Players car track surface material type |  |
| PowerMGU_H | float | 1 | W | Engine MGU-H mechanical power |  |
| PowerMGU_K | float | 1 | W | Engine MGU-K mechanical power |  |
| Precipitation | float | 1 | % | Precipitation at start/finish line | sim |
| PushToPass | bool | 1 |  | Push to pass button state |  |
| PushToTalk | bool | 1 |  | Push to talk button state |  |
| RFTiresAvailable | int | 1 |  | How many right front tires are remaining  255 is unlimited |  |
| RFTiresUsed | int | 1 |  | How many right front tires used so far |  |
| RFbrakeLinePress | float | 1 | bar | RF brake line pressure |  |
| RFcoldPressure | float | 1 | kPa | RF tire cold pressure  as set in the garage |  |
| RFodometer | float | 1 | m | RF distance tire traveled since being placed on car |  |
| RFshockDefl | float | 1 | m | RF shock deflection |  |
| RFshockDefl_ST | float | 6 | m | RF shock deflection at 360 Hz |  |
| RFshockVel | float | 1 | m/s | RF shock velocity |  |
| RFshockVel_ST | float | 6 | m/s | RF shock velocity at 360 Hz |  |
| RFtempCL | float | 1 | C | RF tire left carcass temperature |  |
| RFtempCM | float | 1 | C | RF tire middle carcass temperature |  |
| RFtempCR | float | 1 | C | RF tire right carcass temperature |  |
| RFwearL | float | 1 | % | RF tire left percent tread remaining |  |
| RFwearM | float | 1 | % | RF tire middle percent tread remaining |  |
| RFwearR | float | 1 | % | RF tire right percent tread remaining |  |
| RPM | float | 1 | revs/min | Engine rpm | sim |
| RRTiresAvailable | int | 1 |  | How many right rear tires are remaining  255 is unlimited |  |
| RRTiresUsed | int | 1 |  | How many right rear tires used so far |  |
| RRbrakeLinePress | float | 1 | bar | RR brake line pressure |  |
| RRcoldPressure | float | 1 | kPa | RR tire cold pressure  as set in the garage |  |
| RRodometer | float | 1 | m | RR distance tire traveled since being placed on car |  |
| RRshockDefl | float | 1 | m | RR shock deflection |  |
| RRshockDefl_ST | float | 6 | m | RR shock deflection at 360 Hz |  |
| RRshockVel | float | 1 | m/s | RR shock velocity |  |
| RRshockVel_ST | float | 6 | m/s | RR shock velocity at 360 Hz |  |
| RRtempCL | float | 1 | C | RR tire left carcass temperature |  |
| RRtempCM | float | 1 | C | RR tire middle carcass temperature |  |
| RRtempCR | float | 1 | C | RR tire right carcass temperature |  |
| RRwearL | float | 1 | % | RR tire left percent tread remaining |  |
| RRwearM | float | 1 | % | RR tire middle percent tread remaining |  |
| RRwearR | float | 1 | % | RR tire right percent tread remaining |  |
| RaceLaps | int | 1 |  | Laps completed in race |  |
| RadioTransmitCarIdx | int | 1 |  | The car index of the current person speaking on the radio |  |
| RadioTransmitFrequencyIdx | int | 1 |  | The frequency index of the current person speaking on the radio |  |
| RadioTransmitRadioIdx | int | 1 |  | The radio index of the current person speaking on the radio |  |
| RearTireSetsAvailable | int | 1 |  | How many rear tire sets are remaining  255 is unlimited |  |
| RearTireSetsUsed | int | 1 |  | How many rear tire sets used so far |  |
| RelativeHumidity | float | 1 | % | Relative Humidity at start/finish line | sim |
| ReplayFrameNum | int | 1 |  | Integer replay frame number (60 per second) |  |
| ReplayFrameNumEnd | int | 1 |  | Integer replay frame number from end of tape |  |
| ReplayPlaySlowMotion | bool | 1 |  | 0=not slow motion  1=replay is in slow motion |  |
| ReplayPlaySpeed | int | 1 |  | Replay playback speed |  |
| ReplaySessionNum | int | 1 |  | Replay session number |  |
| ReplaySessionTime | double | 1 | s | Seconds since replay session start |  |
| RightTireSetsAvailable | int | 1 |  | How many right tire sets are remaining  255 is unlimited |  |
| RightTireSetsUsed | int | 1 |  | How many right tire sets used so far |  |
| Roll | float | 1 | rad | Roll orientation |  |
| RollRate | float | 1 | rad/s | Roll rate |  |
| RollRate_ST | float | 6 | rad/s | Roll rate at 360 Hz |  |
| SessionFlags | bitfield | 1 | irsdk_Flags | Session flags | sim |
| SessionJokerLapsRemain | int | 1 |  | Joker laps remaining to be taken |  |
| SessionLapsRemain | int | 1 |  | Old laps left till session ends use SessionLapsRemainEx |  |
| SessionLapsRemainEx | int | 1 |  | New improved laps left till session ends |  |
| SessionLapsTotal | int | 1 |  | Total number of laps in session |  |
| SessionNum | int | 1 |  | Session number | sim |
| SessionOnJokerLap | bool | 1 |  | Player is currently completing a joker lap |  |
| SessionState | int | 1 | irsdk_SessionState | Session state | sim |
| SessionTick | int | 1 |  | Current update number |  |
| SessionTime | double | 1 | s | Seconds since session start | sim |
| SessionTimeOfDay | float | 1 | s | Time of day in seconds |  |
| SessionTimeRemain | double | 1 | s | Seconds left till session ends | sim |
| SessionTimeTotal | double | 1 | s | Total number of seconds in session |  |
| SessionUniqueID | int | 1 |  | Session ID | sim |
| ShiftGrindRPM | float | 1 | RPM | RPM of shifter grinding noise |  |
| ShiftIndicatorPct | float | 1 | % | DEPRECATED use DriverCarSLBlinkRPM instead |  |
| ShiftPowerPct | float | 1 | % | Friction torque applied to gears when shifting or grinding |  |
| Shifter | int | 1 |  | Log inputs from the players shifter control |  |
| Skies | int | 1 |  | Skies (0=clear/1=p cloudy/2=m cloudy/3=overcast) |  |
| SolarAltitude | float | 1 | rad | Sun angle above horizon in radians |  |
| SolarAzimuth | float | 1 | rad | Sun angle clockwise from north in radians |  |
| Speed | float | 1 | m/s | GPS vehicle speed | sim |
| SteeringFFBEnabled | bool | 1 |  | Force feedback is enabled |  |
| SteeringWheelAngle | float | 1 | rad | Steering wheel angle | sim |
| SteeringWheelAngleMax | float | 1 | rad | Steering wheel max angle |  |
| SteeringWheelLimiter | float | 1 | % | Force feedback limiter strength limits impacts and oscillation |  |
| SteeringWheelMaxForceNm | float | 1 | N*m | Value of strength or max force slider in Nm for FFB |  |
| SteeringWheelPctDamper | float | 1 | % | Force feedback % max damping |  |
| SteeringWheelPctIntensity | float | 1 | % | Force feedback % max intensity |  |
| SteeringWheelPctSmoothing | float | 1 | % | Force feedback % max smoothing |  |
| SteeringWheelPctTorque | float | 1 | % | Force feedback % max torque on steering shaft unsigned |  |
| SteeringWheelPctTorqueSign | float | 1 | % | Force feedback % max torque on steering shaft signed |  |
| SteeringWheelPctTorqueSignStops | float | 1 | % | Force feedback % max torque on steering shaft signed stops |  |
| SteeringWheelPeakForceNm | float | 1 | N*m | Peak torque mapping to direct input units for FFB |  |
| SteeringWheelTorque | float | 1 | N*m | Output torque on steering shaft |  |
| SteeringWheelTorque_ST | float | 6 | N*m | Output torque on steering shaft at 360 Hz |  |
| SteeringWheelUseLinear | bool | 1 |  | True if steering wheel force is using linear mode |  |
| Throttle | float | 1 | % | 0=off throttle to 1=full throttle | sim |
| ThrottleRaw | float | 1 | % | Raw throttle input 0=off throttle to 1=full throttle |  |
| TireLF_RumblePitch | float | 1 | Hz | Players LF Tire Sound rumblestrip pitch |  |
| TireLR_RumblePitch | float | 1 | Hz | Players LR Tire Sound rumblestrip pitch |  |
| TireRF_RumblePitch | float | 1 | Hz | Players RF Tire Sound rumblestrip pitch |  |
| TireRR_RumblePitch | float | 1 | Hz | Players RR Tire Sound rumblestrip pitch |  |
| TireSetsAvailable | int | 1 |  | How many tire sets are remaining  255 is unlimited |  |
| TireSetsUsed | int | 1 |  | How many tire sets used so far |  |
| TorqueMGU_K | float | 1 | Nm | Engine MGU-K mechanical torque |  |
| TrackTemp | float | 1 | C | Deprecated  set to TrackTempCrew | sim |
| TrackTempCrew | float | 1 | C | Temperature of track measured by crew around track |  |
| TrackWetness | int | 1 | irsdk_TrackWetness | How wet is the average track surface | sim |
| VelocityX | float | 1 | m/s | X velocity |  |
| VelocityX_ST | float | 6 | m/s at 360 Hz | X velocity |  |
| VelocityY | float | 1 | m/s | Y velocity |  |
| VelocityY_ST | float | 6 | m/s at 360 Hz | Y velocity |  |
| VelocityZ | float | 1 | m/s | Z velocity |  |
| VelocityZ_ST | float | 6 | m/s at 360 Hz | Z velocity |  |
| VertAccel | float | 1 | m/s^2 | Vertical acceleration (including gravity) |  |
| VertAccel_ST | float | 6 | m/s^2 | Vertical acceleration (including gravity) at 360 Hz |  |
| VidCapActive | bool | 1 |  | True if video currently being captured |  |
| VidCapEnabled | bool | 1 |  | True if video capture system is enabled |  |
| Voltage | float | 1 | V | Engine voltage |  |
| WaterLevel | float | 1 | l | Engine coolant level |  |
| WaterTemp | float | 1 | C | Engine coolant temp |  |
| WeatherDeclaredWet | bool | 1 |  | The steward says rain tires can be used | sim |
| WindDir | float | 1 | rad | Wind direction at start/finish line | sim |
| WindVel | float | 1 | m/s | Wind velocity at start/finish line | sim |
| Yaw | float | 1 | rad | Yaw orientation |  |
| YawNorth | float | 1 | rad | Yaw orientation relative to north |  |
| YawRate | float | 1 | rad/s | Yaw rate |  |
| YawRate_ST | float | 6 | rad/s | Yaw rate at 360 Hz |  |
| dcABS | float | 1 |  | In car abs adjustment |  |
| dcAntiRollFront | float | 1 |  | In car front anti roll bar adjustment |  |
| dcAntiRollRear | float | 1 |  | In car rear anti roll bar adjustment |  |
| dcBrakeBias | float | 1 |  | In car brake bias adjustment | sim |
| dcBrakeMisc | float | 1 |  | In car brake misc adjustment |  |
| dcHeadlightFlash | bool | 1 |  | In car headlight flash control active |  |
| dcLowFuelAccept | bool | 1 |  | In car low fuel accept |  |
| dcPitSpeedLimiterToggle | bool | 1 |  | Track if pit speed limiter system is enabled |  |
| dcStarter | bool | 1 |  | In car trigger car starter |  |
| dcToggleWindshieldWipers | bool | 1 |  | In car turn wipers on or off |  |
| dcTractionControl | float | 1 |  | In car traction control adjustment |  |
| dcTractionControl2 | float | 1 |  | In car traction control 2 adjustment |  |
| dcTractionControlToggle | bool | 1 |  | In car traction control active |  |
| dcTriggerWindshieldWipers | bool | 1 |  | In car momentarily turn on wipers |  |
| dpFastRepair | float | 1 |  | Pitstop fast repair set |  |
| dpFuelAddKg | float | 1 | kg | Pitstop fuel add amount |  |
| dpFuelAutoFillActive | float | 1 |  | Pitstop auto fill fuel next stop flag |  |
| dpFuelAutoFillEnabled | float | 1 |  | Pitstop auto fill fuel system enabled |  |
| dpFuelFill | float | 1 |  | Pitstop fuel fill flag |  |
| dpLFTireChange | float | 1 |  | Pitstop lf tire change request |  |
| dpLFTireColdPress | float | 1 | Pa | Pitstop lf tire cold pressure adjustment |  |
| dpLRTireChange | float | 1 |  | Pitstop lr tire change request |  |
| dpLRTireColdPress | float | 1 | Pa | Pitstop lr tire cold pressure adjustment |  |
| dpRFTireChange | float | 1 |  | Pitstop rf tire change request |  |
| dpRFTireColdPress | float | 1 | Pa | Pitstop rf cold tire pressure adjustment |  |
| dpRRTireChange | float | 1 |  | Pitstop rr tire change request |  |
| dpRRTireColdPress | float | 1 | Pa | Pitstop rr cold tire pressure adjustment |  |
| dpWindshieldTearoff | float | 1 |  | Pitstop windshield tearoff |  |

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