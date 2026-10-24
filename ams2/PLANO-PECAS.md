# Plano: widgets em peças (Exibição)

Objetivo: em todos os temas, cada widget ou peça tem sua própria **Exibição**: quando aparece, por quanto tempo e em que forma.
O modelo está em `src/Ams2.Shared/Profiles/ShowSettings.cs`: `ShowSettings`, `ShowPlan` e `ShowCatalog`.
O perfil que não tem bloco `show` desenha exatamente como antes.

## Fase 1 (feita)
- `WidgetSettings.Show`:
  - gatilho: padrão / sempre / por evento / periódico;
  - duração e fades;
  - forma estática ou dinâmica, com linhas e quantidade de pilotos ao redor;
  - durações secundárias (`Timings`);
  - peças (`Pieces`).
  - JSON retrocompatível; normalização com limites; o que for igual ao padrão sai do JSON.
- Os valores que estavam fixos no código viraram os padrões do `ShowCatalog`. Exemplos:
  - `showFor`;
  - `SplitHold`;
  - `PitResultHold`;
  - `LapsToGoHold`;
  - `FastestLapDetector.DefaultHold`;
  - `modeSeconds` e `pageSeconds`;
  - `skySeconds` e `splitSeconds`;
  - `gapHoldSeconds`.
  As opções antigas continuam valendo como padrão do perfil.
- Gatilho genérico:
  - é aplicado na janela (`WidgetWindow`): alfa da janela × opacidade;
  - o conteúdo é forçado a "sempre visível" (`ShowPlan.ContentSettings`);
  - os eventos vêm de `Ams2.Core.Calc.ShowEventTracker`.
- Control Center: seção EXIBIÇÃO por widget, com o botão "Restaurar padrão do tema".

## Fase 2 (início feito: 1993 e 1999)
- Race Board 1993: peças com liga/desliga e tempo próprio.
  - Peças: `pit`, `fastest`, `gap`, `lapsToGo`, `caption`.
  - A prioridade continua a da TV: pit > fastest > gap > laps to go > legenda.
- Qualy Board 1993: peças `result`, `fastest`, `split`, `live`, `caption`.
- Qualy Board 1999: peças `result` e `split`.
- Listas do 1993 (`standings` e `qualitower`): forma estática (topo) ou dinâmica (ao redor do jogador).

Falta na fase 2:
1. Peças como **janelas independentes**: posição e escala próprias por peça.
   - Hoje as peças vivem dentro do board, que é uma janela de 1920x300.
   - Caminho: um `WidgetDef` por peça (`board.pit`, `board.gap`...), com a mesma classe de desenho e um `PieceFilter`.
   - O board vira um preset que liga as peças nas posições atuais.
2. Gatilho **por peça** (evento/periódico). Hoje a peça só tem liga/desliga e duração; o gatilho é do widget inteiro.
3. Board 2018 (`BoardWidget`: torre/setor/voltas/legenda), em peças.
   - Durações hoje em `Broadcast18BoardPresentation`.
   - `QualiBoardTiming.TitleSeconds/PageSeconds` continuam fixos.
4. Race Board 1999 (`Broadcast99RacePresentation`): `fastest` ainda usa `FastestLapDetector.DefaultHold` fixo.

## Fase 3: GPxSAT (1999 Classic, Digital, Digital 2004, Digital 2010)
O `GpxSatWidget` é uma camada de tela inteira com slots (`main`, `top`). Cada modo (`Widgets/GpxSat/Modes/*.cs`) devolve as telas por prioridade em `Dispatch` (`StaticGpxModeBase.Dispatch`). Por isso a GPxSAT não aceita gatilho genérico (`ShowDef.Triggers = false`); só os tempos `sky`, `split` e `page` vieram para a Exibição.

Divisão proposta em peças. Cada peça vira uma entrada de `ShowDef.Pieces` do `gpxsat`, com liga/desliga, duração e, depois, gatilho próprio:

| Peça | Tela GPxSAT | Origem no código | Modos |
|---|---|---|---|
| `pitTimer` | Pit timer (focado no pit lane; prioridade 120) | `PitTimer` | todos |
| `sky` | Mensagens sky: fastest lap, out of the race, in the pits, race won, black flag, riding with | `Sky`, `Fastest`, `OutOfRace`, `InPits`, `Penalty`, `Winner`, `Riding` | todos |
| `clock` | Tempo restante / contador de voltas (slot `top` no 2004) | `Clock` | todos |
| `gapAhead` / `gapBehind` | Gap a cada split (à frente, atrás) | `Gap` (janela `Split.Ahead/Behind`) | todos |
| `speedTrap` | Speed trap | `Trap` | Classic, Digital, 2004 (não no 2010) |
| `raceStandings` | Posições paginadas na volta do líder (8/6/8 por página) | `LapStandings`, `Standings("str"/"stp")` | todos |
| `directorsCut` | Director's cut | `DirectorsCut` | todos |
| `qualiSplit` | Parcial/resultado da volta na classificação | `QualiSplitWindow`, `Split` | todos |
| `qualiStandings` | Tabela da classificação | `Standings("stq")` | todos |
| `qualiNow` | Cartão do piloto ao vivo | `QualiNow`, `Card` | todos |
| `bottomTower` / `leftTower` | Torres do 2010 (5 embaixo, 10 à esquerda) | `Digital2010Mode` | só o 2010 |

Passos:
1. Em `StaticGpxModeBase.Dispatch` e `Digital2010Mode`, consultar `ShowPlan.PieceEnabled`/`PieceDuration` antes de cada tela. Os padrões de hoje entram no `ShowCatalog` (`gpxsat` por tema). Primeiro sem mudar a ordem de prioridade.
2. Expor no Control Center (a seção EXIBIÇÃO já desenha as peças do catálogo).
3. Gatilho por peça (fase 2, item 2) também aqui: por exemplo, `raceStandings` periódico, `gapAhead` só no evento "leaderLap".
4. Validar cada modo contra os prints do GPxPatch (`reference/gpxpatch-spec.md`) com `--png` e com os testes de integração da GPxSAT.

Fica de fora: `GpxSatWidget.ActiveHoldSeconds` (1,5 s). É o ritmo da animação/vblank da camada, não um tempo de exibição.
