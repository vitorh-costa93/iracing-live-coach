# Tema F1 2018 — widgets próprios e configuração própria

Base: `reference/f1-2018-analysis.md` + folhas/recortes `reference/f1-2018-*.jpg` (vídeo GP da Itália 2021, gráfico de TV 2018–2021).
Hoje o 2018 é um "reskin" da estrutura dos outros temas. Objetivo: no 2018, widgets, modos e opções do Control Center **específicos**
do gráfico de TV; os temas 1998 e 2004 não mudam (nem seus perfis).

## 1. O que o AMS2 permite (dados) e o que não
| Gráfico da TV | Fonte no AMS2 | Decisão |
|---|---|---|
| Gap / intervalo / GAINED-LOST / PIT STOPS / fastest lap | posições, `LapDistance`, `BestLapTime`, `PitState`; grid de largada = posições capturadas ao iniciar a corrida | sim |
| Cabeçalho `LAP n/N`, bandeira xadrez | `CurrentLap`, `LapsInEvent`, `RaceState` | sim |
| `YELLOW FLAG / SECTOR n`, `SAFETY CAR` | `FlagColour`, `Sector`, `GameState` | sim (SC só se o jogo expõe; senão só amarela) |
| `STARTED / NOW`, legenda de piloto, resultado, `WINNER` | grid capturado + posição atual + `RaceState.Finished` | sim |
| `PIT LANE` + `STOP TIME` | `PitState` + cronômetro (já existe `PitTimerWidget`) | sim |
| `LIVE SPEED` | velocidade do jogador | sim |
| `RACE START 0-200km/h` | tempo do jogador de 0 a 200 km/h na largada (+ melhor anterior guardado) | sim, só jogador |
| Mini-mapa com carros | `PosX/PosZ` de todos; traçado = trilha da volta do jogador | fase 2 (opcional) |
| Rádio (áudio), retratos, logos, bandeiras de país | não existem | fora; só tique/cor da equipe e texto |

## 2. Infraestrutura (feita primeiro, vale para os demais passos)
1. **Opções por tema** (Shared): `OptionDef(Id, Label, Kind{Choice|Toggle|Number}, Choices, Default)`; `WidgetDef.Options` por tema
   (`WidgetCatalog.OptionsFor(themeId, widgetId)`); `WidgetSettings.Options: Dictionary<string,string>?` (nulo = padrão, `Normalized()` descarta
   chaves desconhecidas e valores inválidos); `WidgetPatch.Options` no IPC. Perfis antigos continuam válidos.
2. **Widgets exclusivos de tema**: `WidgetDef.Themes` (nulo = todos). O Control Center só lista os widgets do tema ativo; `ProfileFactory` só cria os do tema.
3. **Control Center**: bloco "Opções do tema" no `WidgetVm`, gerado das `OptionDef` (ComboBox / CheckBox / número), enviado por `WidgetPatch.Options`.
4. **Host**: `IWidget.Configure(settings)` já recebe `WidgetSettings`; widgets 2018 leem `settings.Option("mode")`. `WidgetRegistry` ganha os ids novos.
5. Testes: Shared (normalização, round-trip JSON, migração), Integration (`--png` por widget/modo, tamanhos de projeto em `WidgetLayout`, sem sobreposição).

## 3. Widgets do 2018
Ordem de implementação = ordem da tabela (um subagente Opus por widget; cada um com testes, prévia `--png` e CC).

| # | Widget (id) | Novo? | Aparência (ref.) | Opções do Control Center (2018) |
|---|---|---|---|---|
| 1 | **Torre** (`standings`) | refeito | cabeçalho `LAP n/N` integrado (o `lapcounter` sai do perfil padrão), filete vermelho, linhas com caixa branca, sigla, coluna de gap clara; **cabeçalhos de estado**: amarelo (`YELLOW FLAG` + `SECTOR n`), `SAFETY CAR`, bandeira xadrez no fim; estados `IN PIT`/`PIT EXIT` ciano, `OUT` cinza; marcador roxo da melhor volta; no fim as 3 primeiras viram faixas altas (caixa roxa no 1º) | `mode`: Gap / Interval / Gained-Lost / Pit stops / Best lap / Auto (alterna a cada N s); `modeSeconds` (5–30); `rows` (topo + ao redor do jogador, já existe); `battle`: bloco "BATTLE FOR Nth" quando o jogador está a < 1 s de alguém (liga/desliga); `fullNames` em SC (liga/desliga); `outBlock` (mostra pilotos fora) |
| 2 | **Legenda** (`drivercaption`) | refeito | placa preta, tique da equipe, nome regular + SOBRENOME negrito, número itálico; variante **STARTED / NOW**; pilha de **resultado** (caixa branca grande) no fim | `variant`: Piloto / Started-Now / Resultado / Auto; `team` / `tyre` (já existem); `showFor` (3–15 s) |
| 3 | **Vencedor** (`winner`) | refeito | banner superior `WINNER · Nome SOBRENOME · equipe` e **pódio** 2º/1º/3º (cartões com cor da equipe e número, sem retrato) | `style`: Banner / Pódio / Ambos; `always` |
| 4 | **Pit Lane** (`pittimer`) | refeito | `PIT LANE` + posição + sobrenome + `STOP TIME` ciano com colchetes; durante o pit `PIT 23.8` | `showPitTime` (tempo na pit lane), `showPosition`, `always` |
| 5 | **Live Speed** (`livespeed`) | **novo** | placa `LIVE SPEED`, nome, `330 KM/H / 205 MPH` em vermelho | `units`: Ambos / km/h / mph; `showName`; `always` |
| 6 | **Race Start** (`racestart`) | **novo** | placa `RACE START 0-200km/h`: jogador e melhor anterior com tempo em segundos | `target` (100/200 km/h), `showBest`, `showFor` |
| 7 | **Race Control** (`racecontrol`) | **novo** | caixa `SAFETY CAR / INCIDENT`, `YELLOW FLAG` e barra `SLOW STOP -11.1s` (parada lenta acima do limite) | `slowStopLimit` (3–30 s), `showFlags`, `showSlowStop`, `showFor` (3–15 s). **Feito**: sem `SAFETY CAR` (o AMS2 não distingue de amarela); lenta = acima do limite e acima da média das até 3 paradas anteriores do jogador (sem histórico, o limite); valor = tempo perdido para essa referência (`SlowStopDetector`) |
| — | Inputs, Fuel, Tyres, Weather, Relative, Radar, Board, Pit Stops | mantidos | seguem o reskin atual (a TV não os tem); Board e Relative saem do perfil padrão do 2018 | — |

Fora desta rodada: mini-mapa com carros (fase 2, depende da trilha de pista); rádio (sem áudio).

## 4. Layout padrão do 2018 (tela 1920×1080)
Torre em (50, 40); `racecontrol` acima à direita da torre; `livespeed` e `racestart` à direita (≈ x 1600, y 400/560); `pittimer` centro-direita;
`drivercaption`/`winner` embaixo à esquerda; Inputs/Fuel/Tyres/Weather como hoje. Tamanhos de projeto registrados em `WidgetLayout.DesignSizes` e conferidos por `--dump-sizes`.

## 5. Critério de aceite
`dotnet build` + `dotnet test` (ams2/tests) verdes; `--png` de cada widget e modo conferido contra o recorte de referência (um recorte por widget);
Control Center mostra só opções do tema ativo; perfis 1998/2004 e perfis 2018 antigos carregam sem mudança; `publish-ams2.ps1` rodado; nada de V3 alterado.
