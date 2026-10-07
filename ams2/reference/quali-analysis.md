# Classificação — comportamento esperado do overlay por tema

Fontes (vídeos indicados pelo usuário, quadros capturados no Chrome em tela cheia, 1920×1080):
- **2018** — https://youtu.be/z_Bhlxr-CZA (GP de Abu Dhabi 2021, melhores momentos da classificação): `quali-2018-sheet-1.jpg`, `quali-2018-sheet-2.jpg` e recortes `quali-2018-*.jpg`.
- **2004** — https://youtu.be/AZY3_fHe624 (volta de classificação de Q1, Japão 2008, câmera onboard): `quali-2004-sheet.jpg`, `quali-2004-tower-clock.jpg`, `quali-2004-lap-bar.jpg`.
- **1998** — https://youtu.be/gUDs85zz1WY (volta de classificação, Mônaco 2003): `quali-1998-sheet.jpg`, `quali-1998-classification-list.jpg`, `quali-1998-finish-comparison.jpg`.

## O que muda de corrida para classificação (comum aos temas)
1. A torre deixa de ser "posição + gap para o líder na pista" e vira **tabela de melhores voltas**: o 1º mostra o tempo, os demais `+diferença` para o 1º.
2. O contador `LAP n/N` é trocado pelo **relógio da sessão** (tempo restante), com o nome da fase (`Q1`/`Q2`/`Q3`).
3. Estados por piloto no lugar do tempo: **`OUT LAP`** (volta de saída), **`NO TIME`** (ainda sem tempo), **`IN PIT`**.
4. Aparece uma **placa de volta em andamento** (piloto em foco): tempo corrente, comparação com o melhor/líder (`+0.287`), barras de setores S1/S2/S3 (verde = melhor pessoal, roxo = melhor geral, amarelo = pior).
5. Ao cruzar a linha: tempo final, posição obtida e diferença (`1:18.917 · +0.685 · 7`).
6. Fim da sessão: bandeira xadrez no cabeçalho; lista de classificação final; no 2018 também bloco de eliminados.

## 2018 (TV 2018–2021)
- **Torre de classificação** (`quali-2018-tower-q1.jpg`): cabeçalho preto `Q1` + relógio `7:10`; linhas = caixa de posição + sigla + logo + tempo (1º) ou `+gap`; `OUT LAP` no lugar do tempo; **bloco `ELIMINATION ZONE`** (título branco, filete vermelho) com os pilotos do fim do grid; em Q3 só 10 linhas, `NO TIME` para quem ainda não marcou.
- **Q2/Q3 com bandeirada** (`quali-2018-tower-fastesttyre.jpg`): mini bandeira xadrez à esquerda de quem já terminou a volta; cabeçalho `Q2` sobre xadrez com `0:00` vermelho; **modo `FASTEST TYRE`** (título com filete vermelho, coluna de composto S/M/H em círculo, tempos em décimos) e legenda `SOFT / MEDIUM / HARD`.
- **`DRIVER AT RISK`** (`quali-2018-sheet-1.jpg`, quadros 2 e 4): cartão abaixo da zona (nome + retrato + tempo `1:24.225`) para o piloto que está no ponto de corte.
- **`ELIMINATED`** (`quali-2018-eliminated.jpg`): cabeçalho preto `ELIMINATED`, uma faixa por piloto (caixa de posição vermelha, nome, bandeira) com retrato e `+gap` grande à direita.
- **Placa de volta** (`quali-2018-lap-panel.jpg`): linha preta com posição, sobrenome, número em itálico, pneu; abaixo tempo corrente grande (`1:18.5`), nome do líder + tempo dele à direita e **barra S1 S2 S3** colorida.
- **Painel de setor** (`quali-2018-sector-panel.jpg`): `SECTOR 2 / VERSTAPPEN / 35.643` com a linha do tempo roxa quando é o melhor geral.
- **Classificação final** (`quali-2018-sheet-2.jpg`, último quadro): tela cheia `FASTEST / CLASSIFICATION` com tabela posição, piloto, equipe, tempo e pneu — não é widget do overlay (versão simplificada: lista final).
- Tempos em verde/roxo (`1:24.043` verde = melhora pessoal) e posição em vermelho quando está na zona de eliminação.

## 2004 (TV 2004–2008)
- Torre mínima no canto superior esquerdo: `1 HAM 1:18.232` com o tempo do líder numa caixa preta e **só siglas** abaixo (`11 PIQ … 20 ALO`) — mostra o fundo do grid (pilotos em risco), números vermelhos para os que estão na zona de eliminação.
- Caixa do relógio no topo: `Q1 | 9:34` (rótulo escuro + relógio em caixa branca).
- Barra de volta embaixo: nome (`F Alonso`) em caixa branca, abaixo parcial do setor em caixa preta (`21.7`, `50.469`) e, quando há comparação, caixa laranja com a posição vermelha + `+0.471`; ao fim `1:18.917 · +0.685` com o nº da posição ao lado.
- Duas barras pequenas (adversário `H Kovalainen 1:16.1` / `1 1:18.232`) comparando com o melhor.

## 1998 (TV 1998–2001)
- **Lista de classificação** (`quali-1998-classification-list.jpg`): ao fim da volta, duas colunas de 3 com caixa amarela de posição + nome em caixa alta; 1º com tempo `1:15.259`, os demais `0.036` (sem "+").
- **Tempo corrente** na base: `M SCHUMACHER 8.6` (parcial ao vivo) com sombra; no setor final compara dois pilotos (`R SCHUMACHER 1:15.259 | M SCHUMACHER 1:08.6`) e velocidade (`268.7 Km/h`), com o rótulo `FINISH LINE`.
- Painel de velocidades máximas (`TOP SPEEDS 291.2 / 288.0 Km/h`) — opcional.

## Sem equivalente no AMS2
Retratos, logos, bandeiras de países e câmera onboard. A fase Q1/Q2/Q3 só existe se a sessão for configurada assim; o AMS2 em geral tem uma sessão de classificação única (usar `QUALIFYING` e deixar a zona de eliminação como opção do usuário).
