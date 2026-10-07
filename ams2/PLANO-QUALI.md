# Widgets de classificação (todos os temas)

Base: `reference/quali-analysis.md` (vídeos 1998, 2004 e 2018). Meta: em sessão de **classificação** o overlay muda de comportamento como na TV, e o usuário controla
isso no Control Center como no iRacing (cada widget escolhe em que sessões aparece).

## 1. Visibilidade por tipo de sessão (infra)
- `WidgetSettings.Sessions` (nulo = padrão do widget): conjunto de `practice`, `qualify`, `race` em que o widget aparece. Padrões em `WidgetDef.DefaultSessions`:
  widgets de corrida (standings, board, relative, pitstops, drivercaption, winner, lapcounter, racecontrol, racestart) = `race` (+ `practice` onde já faziam sentido hoje: relative, fuel, tyres, weather, inputs, radar, livespeed, pittimer = todas).
  Widgets novos de classificação = `qualify`. Perfis antigos sem o campo continuam como antes (todos os existentes visíveis em todas as sessões, exceto os novos).
- Fonte: `SessionSnapshot.Kind` (Practice/Test/Qualify/FormationLap/Race/TimeAttack). Mapeamento: Practice/Test/TimeAttack = `practice`; Qualify = `qualify`; FormationLap/Race = `race`.
- Control Center: grupo "Mostrar em" com 3 caixas (Treino, Classificação, Corrida) por widget; IPC em `WidgetPatch.Sessions`.
- Host: `WidgetWindow` já tem a regra de visibilidade (`PlayerDriving`); acrescentar o filtro de sessão.

## 2. Widgets novos (ativos em classificação), com visual por tema
| Id | O que mostra | 1998 | 2004 | 2018 |
|---|---|---|---|---|
| `qualitower` | tabela de melhores voltas: 1º com tempo, demais com diferença; `OUT LAP`/`NO TIME`/`IN PIT`; relógio da sessão no cabeçalho | duas colunas de caixas amarelas (`R SCHUMACHER 1:15.259`, `0.036`) | torre de siglas com o tempo do líder numa caixa preta + caixa do relógio `Q1 | 9:34` | torre `Q1` + relógio, bloco `ELIMINATION ZONE`, modo `FASTEST TYRE`, cartão `DRIVER AT RISK`, bandeiras xadrez |
| `qualilap` | volta em andamento do piloto em foco (jogador): tempo corrente, diferença para o melhor/líder, setores S1/S2/S3 e resultado ao cruzar a linha | tempo corrente com sombra + `FINISH LINE` | barra branca/preta/laranja com posição e `+0.471` | placa de volta com barra S1 S2 S3 |
| `qualiresult` | lista de classificação ao fim da sessão (e eliminados no 2018) | lista em colunas | lista de siglas | `ELIMINATED` + tabela final |

Opções (Control Center, por tema): `qualitower` — linhas visíveis (topo + ao redor do jogador), `eliminationFrom` (posição de corte, 0 = desligado), `showTyre` (2018), `mode` (2018: time | fastesttyre), `showClock`; `qualilap` — comparar com (melhor pessoal | líder), `showSectors`, `showFor` (resultado após a volta);
`qualiresult` — `showFor`, `rows`.

## 3. Dados
`SessionSnapshot.Kind/TimeRemainingSeconds`, `CarSnapshot.BestLapTime/LastLapTime/Sector/PitState/CurrentLap/LapDistance`. Setores: o AMS2 expõe o setor atual e tempos de setor por carro? — se não houver
tempos de setor na memória, calcular no `QualiLapTracker` (Ams2.Core/Calc) a partir da troca de `Sector`. Melhor geral = menor tempo de cada setor entre todos.

## 4. Ordem de implementação (um subagente Opus por etapa, testes + prévias `--png` + Control Center)
1. Infra de sessões (campo, CC, host) + `QualiLapTracker`/`QualiTable` no Core (com testes e `FakeRawSource` `AMS2_FAKE_QUALI=1`).
2. `qualitower` (2018 primeiro, depois 2004 e 1998).
3. `qualilap`.
4. `qualiresult`.
5. Publicar e validar no jogo em uma sessão de classificação.
