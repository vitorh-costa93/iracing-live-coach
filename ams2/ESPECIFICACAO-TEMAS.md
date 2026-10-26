# Especificação de widgets por tema (AMS2) - 26/10/2026

Documento de alinhamento. Nada aqui altera código. Serve para o usuário validar antes de implementar.
Legenda de origem: **GPx** = GPxPatch/GPxSAT (spec em `reference/gpxpatch-spec.md` e `reference/gpxsat/*.md`);
**Prints** = desenvolvido a partir dos prints do usuário (`reference/1999-behavior.md`, `1998-behavior-review.md`,
`2004-behavior-review.md`, `f1-2012-analysis.md`, `quali-analysis.md`, `1993/README.md`, `reference/f1-2000s-speedo-dial.png`).
Forma: **estática** = montada como nos prints (aparece, fica congelada, some); **dinâmica** = atualiza ao vivo (estilo Relative/Standings do iRacing).

## 1. O que o usuário pediu mais de uma vez

| Tema | Frase (fonte) |
|---|---|
| Standings estática ao cruzar a linha | 23/10 (sessão 235bdbf5): "a classificação ela é mostrada, eu lembro, no GP4, somente quando cruzava a linha de chegada. E ficava estático". 26/10 (73ba6482): "já havia dito mais de uma vez ... tem que ser estático quando o líder passa na linha de chegada, o mesmo vale para o de 1999 e 2012". |
| Rotação de widgets (mescla) | 19/10 (b9c08968): "não entendi muito bem qual que é a dinâmica ali dos widgets, qual que é a rotatividade". 22/10: "não está sendo feito da forma que gostaria a rotação ... Só fica mostrando delta ... e volta mais rápida e eu sei que tem muito mais que isso". 24/10: "quero o Race Board e o Qualy Board e essa rotação de widgets, mas estabelecer quais, quando, como e por quanto tempo". 26/10 item 2: mescla GPx + desenvolvido. |
| Legenda do piloto entre eventos | 13/10 e 14/10 (1993/1999: legenda mantém foto/nome/equipe/posição entre eventos, `docs/CODEX_CONTINUIDADE.md:247`). 23/10: "intervalos onde não mostrava nenhum widget, ou ficava só a legenda ... no de 2004, 2008". 26/10 item 6. |
| Velocímetro 2004 = o dos prints | 22/10: "O velocímetro correto é o que a gente tem, por exemplo, no 2004 ... aquela reconstrução perfeita ... Aquele lá é o correto para 2004". 02/10: "velocímetro da imagem 1 com os input graphs embutidos". 26/10 item 1. (16/10 dizia "dentro do gpx patch também tem os velocímetros da época"; as de 22/10 e 26/10 são mais recentes e explícitas e prevalecem.) |
| Tipografia | 26/10 item 8: a do 1999 vem do GPxPatch e não muda. 22/10: a do 2012 está "mal feita comparada com o GPX Patch". |
| Personalização | 24/10: para cada widget definir quais, quando, como, por quanto tempo, forma estática ou dinâmica, em todos os temas (inclusive 2018 e 1993). |

## 2. Princípios comuns (proposta)

1. **Race Board** = uma única "pista" de telas em rotação por prioridade (evento vence ocioso). Entre eventos, sempre a **legenda do piloto** (nunca slot vazio).
2. **Qualy Board** = máquina de estados por volta do jogador (out lap, relógio, aproximação, intermediária, resultado) mais tabela só na volta de saída.
3. **Standings da corrida** = estática, snapshot no instante em que o líder cruza a linha; valores (gaps) congelados na janela. Sem atualização ao vivo.
4. Dados AMS2 inexistentes (não inventar): Q1/Q2/Q3, tipo de penalidade, causa de abandono.
5. Tipografia: 1999 (GPx) intocada; cada tema usa as fontes do respectivo GPx (Mañana, Futura, Digital Standard, DIN).

## 3. Por tema

Prioridade padrão do Race Board (de `1999-behavior.md`, adotada como base mesclada): pit parado > volta mais rápida (7 s) > tabela de início de volta (8 por página, 4 s) > gap periódico (a cada 3ª volta, vizinho físico, 6 s) > voltas restantes (a cada 5ª volta, 6 s) > legenda.

### 3.1 f1-1993 (Broadcast93, próprio)
Widgets: board, qualiboard, standings, qualitower, radar, inputs (graph). Origem: **Prints** (ref01..09 em `reference/1993/`, requisitos de 13/10).
- Race Board (imagens 1, 2, 3, 5): gap a cada 3 voltas (menor gap analisado no início da volta, vizinho físico, 6 s, estático); fastest só quando houver (7 s); pit stop (3 s de resultado); LAPS TO GO a cada 5 voltas (6 s); legenda entre eventos (nome, equipe, a bordo). Estática.
- Qualy Board: 3 estados - pós-bandeirada (posição em parênteses azul + delta, 6 s), volta mais rápida (7 s), padrão (img 3, volta ao vivo / FIRST LAP OUT OF PITS). Tabela só na volta de saída.
- Race Standings: sempre visível, top 6 padrão, estática. Qualy Standings discreta, top N selecionável.
- Só esses widgets + radar + input graph. Faixas Olivetti/TAG Heuer com fidelidade; nº amarelo = nº do carro da pintura.

### 3.2 f1-1998 "F1 1999" (GPxSAT Classic)
Origem: **GPx** (telas e tipografia, congeladas) + **Prints** (regras de 1999-behavior.md / 1998-behavior-review.md).
- Widgets: gpxsat (Classic), inputs (velocímetro dos prints), radar, inputs graph; board/qualiboard Broadcast99 hoje desligados pelo gpxsat.
- Race Board mesclado: prioridade acima. Torre CLASSIFICATION em 2 colunas de 4 (caixas amarelas, diferenças amarelas sem sinal) **estática no cruzamento do líder** (4 s por página; 2 páginas). Setor: vizinho físico travado, congela no 2º cruzamento. Sky messages do GPx (fastest, abandono, boxes, vencedor) continuam.
- Qualy Board: out lap "FIRST LAP OUT OF PITS"; relógio; aproximação 5 s; intermediária 3 s (TAG Heuer); resultado 6 s; torre CLASSIFICATION só na volta de saída.
- Tipografia: não mudar.

### 3.3 f1-1999-digital (GPxSAT Digital)
Igual ao 3.2 em regras de rotação; telas e fontes do **GPx** Digital. Tipografia 1999 intocada. Widgets: gpxsat, inputs, radar, gpxgauges (opcional).

### 3.4 f1-2004 (Digital 2004)
- Velocímetro: **Prints** (`inputs`: tacômetro 6-20, Gear/Throttle/Brake, barra de velocidade em arco, `reference/f1-2000s-speedo-dial.png`, commit 879ddf4). GPxCInfo (gpxgauges) só opcional, desligado por padrão.
- Standings: torre 2004 (2 colunas, montagem progressiva, nomes pretos em célula branca, gaps sem "+", contador = total - voltas do líder). **Estática no cruzamento do líder**, valores congelados. Origem: Prints + GPx.
- Legenda: placa completa (nome, equipe, bandeira, posição, pneu) **recorrente entre eventos** + variante simples (faixa branca) para eventos curtos.
- Race Board: prioridade base + telas GPx (sky, split, pit timer). Qualy Board: tabela de melhores voltas só na volta de saída; Quali Lap (posição separada, verde/laranja); relógio, OUT LAP/NO TIME/IN PIT.

### 3.5 f1-2012 (Digital 2010)
- Origem: **GPx** (torres de siglas, caixa do piloto, pit timers, WINNER) + **Prints** (`f1-2012-analysis.md`: pílula Lap, ticker de 5 células, legenda com bandeira + pneu, battle DRS/KERS, pit lane).
- Standings/torre: estática no cruzamento do líder. Legenda com bandeira + pneu recorrente (`f1-2012-driver-caption-flag.jpg`).
- Velocímetro: ambíguo (ver perguntas). Tipografia: refazer a partir do GPx (queixa de 22/10).
- Não feitos ainda: dial de bordo, Fastest Lap, BATTLE FOR.

### 3.6 f1-2018 (Modern2018, próprio)
Origem: **Prints** (BoardWidget S18, poscaption com bandeira, battle, standings com modo automático). Race Board: torre da linha > gap no setor > comparativo de voltas (a cada 3) > legenda. Legenda `poscaption` ao mudar de posição + recorrente entre eventos. Qualy: qualilap com painel de setor, qualitower dinâmica. Sem mudanças pedidas além de personalização e legenda.

## 4. Esperado x atual

| Tema | Esperado | Atual (arquivo:linha) |
|---|---|---|
| 2004 velocímetro | `inputs` ligado, gpxgauges desligado | `WidgetLayout.cs` `GpxSatReplaces` (~39-46) inclui `inputs` para f1-2004/2012; migração v9 em `Profile.cs` (~398-401, `CurrentSchemaVersion` 333) liga gpxgauges e desliga `inputs`. |
| 1999/Digital/2004/2012 standings estática | Snapshot no cruzamento | `StaticGpxModeBase.cs` `Rows()` (173-181) lê `c.Model.Standings`/`GapToLeader` a cada quadro; `Digital2004Mode.cs` `RaceStandings` (400-406) e `QualiStandings` (422-429) ao vivo. A janela por evento já existe (`LapStandings`: `StaticGpxModeBase.cs:100-105`, `GpxRotation.EventPage` em `Ams2.Core/Calc/GpxSat/GpxRotation.cs:18-23`), só os valores não congelam. |
| 2004 tabela da quali | A confirmar (só ao cruzar vs contínua) | `Digital2004Mode.cs` `OtherScreens` (95-107): com `QualiStandings` e mais de um tempo, contínua. `qualiStandings` default true (`WidgetCatalog.cs:449-483`). |
| Rotação mesclada | Prioridade base + GPx | `StaticGpxModeBase.Dispatch` (60-90) e `Digital2004Mode.RaceScreens` (61-83) só despacham telas GPx; regras de gap/remaining/fastest/legenda dos prints existem em `ShowCatalog` (`ShowSettings.cs:542-594`) mas os boards ficam desligados pelo gpxsat (`GpxSatReplaces`). `gpxsat` tem `Triggers=false` (`ShowSettings.cs:534-541`). |
| Legenda recorrente | Entre eventos, todos os temas | `drivercaption` substituído em 2004/2012 pelo gpxsat (`GpxSatReplaces`); só aparece em Sky/Card. Evento padrão: Position, PlayerLap (`ShowSettings.cs:474-481`). |
| Qualy Board | Máquina de estados dos prints | 1999/1993 existem como peças (`ShowSettings.cs:581-603`), desligados nos temas com gpxsat. |
| 1999 tipografia | Intocada | Não alterar Mañana/GPx. |

## 5. Perguntas em aberto

1. "Classificação" (item 4) = standings da corrida ou também a tabela da quali (`stq`, hoje contínua)?
2. Duração da janela estática: manter 2 x pageSeconds (12 s por página) ou 4 s por página como nos prints?
3. Velocímetro do 2012: dos prints do 2004 ou o dial de bordo (`f1-2012-onboard-dial.jpg`, não feito)?
4. Velocímetro do 1999: manter `inputs` (hoje mantido) ou GPxCInfo?
5. No 1999/2004/2012, gap a cada 3 voltas e voltas restantes a cada 5 valem em cima das telas do GPxSAT?
6. Legenda: fixa nos intervalos ou periódica (ex.: 6 s a cada N s)?
7. Quais extras do GPxSAT manter por tema (microtime, trackmap, ATL, progressão)?
8. Itens 3 (UI de seleção) e 5 (nome da pintura) ficam fora desta especificação? (Item 5 está com outro agente.)

## 6. Plano de implementação (passos pequenos, por prioridade)

1. Velocímetro 2004: tirar `inputs` de `GpxSatReplaces` (f1-2004), migração schema 10 religando `inputs` e desligando gpxgauges.
   **Feito (26/10):** `WidgetLayout.GpxSatReplaces` sem `inputs` no 2004 e `HiddenByDefault[f1-2004] = gpxgauges`; `Profile.CurrentSchemaVersion = 10`. Regra da migração (`Profile.Uses2004StockGpxGauges`): só perfis 2004 de esquema 8/9 no par padrão anterior (`inputs` desligado e `gpxgauges` ligado/ausente) trocam; qualquer outra combinação é escolha do usuário e fica. 2012 e 1999 intocados.
2. Standings estáticas: snapshot dos valores no cruzamento do líder (`StaticGpxModeBase.Rows`, `Digital2004Mode.RaceStandings`, Digital2010); teste de unidade do snapshot.
3. Legenda recorrente (slot main) em todos os temas, bandeira/equipe/posição/pneu conforme tema.
4. Race Board mesclado: gatilhos dos prints (gap 3, remaining 5, fastest 7 s, pit, tabela de início de volta) + telas GPx, com ordem/duração em `ShowCatalog`.
5. Qualy Board mesclado: out lap, relógio, aproximação, intermediária, resultado; tabela só na volta de saída; 3 estados do 1993.
6. Tipografia do 2012 (usar a do GPx); 1999 congelada.
7. Tarefas separadas: UI de seleção amigável (item 3) e nome pela pintura (item 5).
8. Após cada passo: `dotnet build`, `dotnet test`, publicar, conferir por recorte.
