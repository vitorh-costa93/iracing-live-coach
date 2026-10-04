# Tema "2018" — análise do vídeo de referência

Fonte: https://youtu.be/gPkIfNtro7I ("Extended Race Highlights | 2021 Italian Grand Prix"). O vídeo é de 2021, mas o gráfico de TV
é a família F1 2018–2021 (a mesma do tema "2018", que substitui o f1-2010s). Quadros extraídos no navegador embutido com
`canvas.drawImage(<video>)` em 1080p; as medidas abaixo são aproximadas em pixels do vídeo 1920×1080 (±2 px).

Arquivos desta pasta (`f1-2018-*`):

| Arquivo | Conteúdo |
|---|---|
| `f1-2018-contact-sheet-1.jpg`, `-2.jpg` | visão geral do vídeo: posição de cada gráfico na tela |
| `f1-2018-tower-battle.jpg` | torre em corrida (top 11 + bloco "BATTLE FOR 12th" + restante), cabeçalho `LAP 8/53` |
| `f1-2018-tower-pitstops.jpg` | torre no modo `PIT STOPS` (coluna da direita = nº de paradas), pilotos fora da corrida em bloco cinza |
| `f1-2018-tower-ending-sc.jpg` | modo `ENDING`/Safety Car: cabeçalho amarelo, nomes completos em vez de siglas |
| `f1-2018-lap-gainedlost.jpg` | modo `GAINED/LOST` (setas verde/amarela e variação de posições) |
| `f1-2018-safetycar-box-and-pitlane.jpg`, `f1-2018-pitlane-stoptime.jpg` | caixa de Safety Car e gráfico `PIT LANE` com tempo de parada |
| `f1-2018-driver-caption.jpg` | legenda de piloto (nome, número, equipe) e variante `STARTED / NOW` |

## Linguagem visual geral
- Painéis **pretos translúcidos** (≈ #05060A a ~88 % de opacidade), cantos retos com **canto superior direito/esquerdo levemente
  arredondado (≈ 6 px)** só no cabeçalho; filetes finos entre linhas; **sem gradiente, sem sombra de texto**.
- Texto **branco**, caixa alta, sans geométrica larga e firme (a original é a Formula1 Display, proprietária — usar a fonte livre mais próxima).
- Acento = **vermelho F1 (#E10600)** em filetes; **roxo (#A020C8)** = melhor volta; **amarelo (#F5D20A)** = Safety Car/bandeira; **ciano (#35E6DC)** = tempos de pit.
- Caixa de posição: **quadrado branco arredondado (≈ 30×30, raio ≈ 4)** com número preto em negrito. Verde (#25C23A) = ganhou posições, vermelho (#D8262C) = perdeu.
- Marca da F1 (logo) fixa no canto inferior esquerdo do vídeo; não replicar.

## Torre (Standings) — x ≈ 80..340, topo y ≈ 46
- **Cabeçalho** ≈ 262×67: `LAP` branco, largo, com tracking, linha fina abaixo e `8 / 53` (numerais finos, ≈ 30 px). No Safety Car/ENDING o cabeçalho cresce
  (≈ 262×150): `LAP | 30/53` amarelo, ícones (bandeiras xadrez, carro de segurança, FIA) e a palavra de estado (`ENDING`, ≈ 40 px, amarelo).
- Filete **vermelho** de ≈ 3 px logo abaixo do cabeçalho; título do modo (`PIT STOPS`, `GAINED/LOST`, `BATTLE FOR 12th`) centralizado, ≈ 20 px.
- **Linha** (passo ≈ 37 px, altura ≈ 34): caixa de posição (x≈83), **sigla de 3 letras** (x≈125, ≈ 24 px, negrito), logo da equipe (≈ 22 px, x≈ 235) e **coluna de gap**
  à direita (x≈ 270..340) em fundo um pouco mais claro que o painel (#14151C), texto branco regular `+0.910`, `Leader` para o 1º, `+1:04.505` acima de 1 min.
- Marcador de volta mais rápida: quadrado **roxo** com cronômetro, preso à esquerda da linha do piloto (fora do painel).
- Variantes: o 1º lugar mostra `Leader`; no modo Safety Car/ENDING a sigla vira **nome completo em caixa alta** e a coluna de gap some.
- **Bloco "Battle for Nth"**: dois pilotos em faixas altas (≈ 95 px cada) com foto + logo; aqui sem foto: usar a faixa com nome e posição.
- Pilotos que abandonaram: bloco final **cinza** translúcido (#3A3A3F a 70 %) com texto cinza claro, sem caixa de posição branca.

## Caixa de Safety Car (alto à esquerda, ≈ 270×140)
Parte de cima preta com `SAFETY CAR` amarelo; parte de baixo **amarela** com ícones pretos (bandeiras, carro, FIA) e `INCIDENT` preto em negrito.

## Pit lane / tempo de parada (centro-direita, ≈ 280×95 por piloto)
Cabeçalho preto `PIT LANE` branco centralizado; faixa preta com caixa de posição branca + **tique da cor da equipe** + sobrenome em negrito; corpo cinza-azulado escuro (#2E3A3C)
com `STOP TIME` ciano à esquerda e o tempo grande (≈ 52 px) em **ciano com colchetes de canto**; durante a parada aparece `PIT 23.8` (tempo na pit lane, branco) no lugar do rótulo.

## Legenda de piloto (parte baixa, esquerda, ≈ 520×70)
Placa preta translúcida; **tique vertical na cor da equipe** à esquerda; nome em duas pesos — nome próprio regular, **SOBRENOME em negrito**; número do carro em itálico na cor da equipe; logo da equipe à direita;
segunda linha pequena com o nome da equipe. Variante com `STARTED 11th` / `NOW 12th` (ordinais grandes com sufixo sobrescrito), separados por filete vertical.

## Legenda de resultado (fim da corrida, `f1-2018-result-caption.jpg`)
Mesma placa preta da legenda de piloto, porém com **caixa de posição branca grande (≈ 70×70)** à esquerda, tique da cor da equipe, `Nome SOBRENOME` (nome regular, sobrenome negrito, ≈ 30 px), número em itálico na cor da equipe e equipe em cinza claro abaixo; o lado direito termina em recorte diagonal com a bandeira/cor. Aparece empilhada (3º, 4º…) na parte baixa, ≈ 760 px de largura; acima da primeira vai um selo verde de patrocinador (não replicar).

## Vencedor / pódio (t≈1160 s do vídeo, só visto na folha de contatos)
Três retratos verticais lado a lado (2º, **1º ao centro e maior**, 3º) com `WINNER` em destaque acima do 1º e `2nd`/`3rd` acima dos outros, sobrenome embaixo de cada retrato, fundo escuro translúcido. Sem fotos no AMS2: usar blocos com posição, sobrenome e cor da equipe.

## Painéis extras (varredura completa do vídeo, 2ª passada)
Folhas: `f1-2018-scan-race-t100-850.jpg` (quadros em t = 100, 175, 250, 325 / 400, 475, 550, 625 / 700, 775, 850 s, em ordem de leitura),
`f1-2018-scan-finish-t925-1190.jpg` (925, 1000, 1075, 1120 / 1140, 1160, 1180, 1190 s) e dois mosaicos de recortes 1:1–2×:
`f1-2018-crops-livespeed-racestart-radio-caption.jpg` e `f1-2018-crops-pitmap-winner-finishtower.jpg`. `f1-2018-tower-yellowflag.jpg` = bandeira amarela (t≈610 s).

- **Painel de telemetria `LIVE SPEED`** (t≈325 s, direita do quadro, ≈ 170×75 px na tela de 1920): filete vermelho no topo, `LIVE SPEED` branco à esquerda + ícone de velocímetro à direita,
  nome do piloto centralizado (`HAMILTON`, caixa alta, negrito leve), embaixo dois números **vermelhos (#E6242B) grandes** (`330` KM/H e `205` MPH, separados por uma barra diagonal fina),
  rótulos `KM/H`/`MPH` em azul-acinzentado (#8FB0BD). Fundo preto ≈ 90 %, canto inferior direito arredondado.
- **`RACE START 0-200km/h`** (t≈175 s): mesma placa; título branco em duas linhas; para cada piloto uma faixa preta com **tique da cor da equipe** + sobrenome em negrito e abaixo faixa cinza-escura
  com o tempo grande (`4.6` + `s` pequeno) – comparação entre dois pilotos empilhados.
- **Rádio** (t≈100 s, `HAMILTON`): placa com equipe no topo (`Mercedes`), **forma de onda ciano (#35E6C8)** em torno da foto, tique + nome em negrito, caixa tracejada ciano com ícone de capacete e a frase em itálico entre aspas.
  No fim do vídeo reaparece compacta (`McLaren / 1 NORRIS` + forma de onda). Exige áudio do rádio: **não existe no AMS2** (só legenda estática possível).
- **Legenda `STARTED / NOW`** (t≈175 s): linha superior preta com caixa de posição branca (do grid ATUAL), tique da equipe, `Daniel RICCIARDO`, número em itálico dourado e logo; linha inferior cinza-escura translúcida
  com `STARTED 2nd | NOW 1st` (rótulos pequenos, ordinais ≈ 3× maiores com sufixo sobrescrito, filete vertical no meio).
- **Pit lane com mini-mapa** (t≈475 s): `PIT LANE` + `5 | VERSTAPPEN` + `STOP TIME 11.1` (ciano, colchetes de canto) acompanhado de um **mapa da pista** (traçado branco com linha preta, números de curva, bolinhas coloridas = carros por equipe, bandeira xadrez na linha de chegada).
- **Torre — modo `Interval`** (t≈475 s): cabeçalho de coluna `Interval` no lugar de `Leader`; valores = diferença para o carro da frente (`+0.368`); estado `IN PIT` ciano no lugar do gap; linha de piloto em destaque (RICCIARDO) em faixa preta com nome completo e caixa branca (jogador/foco).
- **Bandeira amarela** (t≈610 s): cabeçalho `YELLOW FLAG` (amarelo, fundo preto) + faixa amarela com ícone de bandeira e `SECTOR 1` (preto); `PIT EXIT`/`IN PIT` ciano nas linhas; pilotos fora = `OUT` cinza no rodapé.
- **Safety Car** (t≈625 s): caixa `SAFETY CAR` preta com bandeiras amarelas; torre com **nomes completos** (`LECLERC, PEREZ…`); legendas de envolvidos embaixo (`Lewis HAMILTON 44 | Mercedes`, `Max VERSTAPPEN 33 | Red Bull Racing`), uma em cada canto, com logo da equipe.
- **`SLOW STOP -11.1s`** (t≈550 s): barra preta fina embaixo do vídeo de repetição com o texto `VERSTAPPEN SLOW STOP -11.1s`.
- **Fim da corrida** (t≈1075 s): cabeçalho `LAP | 53 / 53` sobre **bandeira xadrez**; as três primeiras linhas viram **faixas altas com retrato** (nome completo da sigla `RICCIARDO`, caixa de posição **roxa** para o 1º, brancas para os demais, mini bandeira xadrez à esquerda); da 4ª em diante linhas normais com `+3.174`.
  Banner superior `WINNER | Daniel RICCIARDO · McLaren` (≈ 780×90, fundo xadrez recortado + placa preta arredondada e logo da equipe à direita).
- **Pódio** (t≈1140 s): três cartões verticais (2º, 1º maior, 3º) com `2ND`/`WINNER`/`3RD`, retrato, bandeira do país, nome + sobrenome em negrito e equipe; sem retrato no AMS2: usar cor da equipe/número.

## Sem equivalente no AMS2
Rádio (áudio), retratos de pilotos, logos de equipe e bandeiras de países não existem na memória compartilhada; usar tique/cor da equipe e texto.
