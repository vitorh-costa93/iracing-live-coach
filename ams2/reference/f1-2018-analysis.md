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

## Não analisado
Painel de telemetria do cockpit (≈ 610 s, direita) e gráficos de rádio/ultrapassagem: o navegador embutido ficou instável (painel oculto, `seeked` travando); não são widgets do app.
