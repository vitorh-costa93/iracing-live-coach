# GPxSAT – modo "Digital 2004": especificação de layout (engenharia reversa estática)

Fonte: GPxSAT descompactado (base 0x9c00000; buffer `sat_rel.bin` = VA 0x9c01000), desmontado em `sat5.asm`
e decompilado linearmente com `sim.py`/`runall.py` (saídas `o_<VA>.txt` neste diretório).
Para localizar a evidência, procure o VA citado em `sat5.asm` (a linha é a do VA) ou em `o_<VA>.txt`.
Confiança: **C** = certo (lido no asm), **P** = provável (inferido pelo fluxo e pelas strings), **H** = hipótese.

Referência: 1024x768, X0 = 0, W = 1024, SCR_T = 0, SCR_B = 768. **FH** = altura da linha devolvida por
FONT.vt+0xc (valor real não confirmado; **H: ~17 px** em 768 de altura). Valores em px abaixo assumem FH = 17,
W/5 = 204.8.

---
## 1. API de desenho e escala

| Item | Detalhe | Conf. | Evidência |
|---|---|---|---|
| Modo D2004 | índice de modo `[0x9c3510c]` = 2; construtor do objeto D2004 | C | 0x9c270b0 |
| Textura | `PDX.vt+0x2c(&rect{L,T,R,B}, tex, src=0, alpha, 0, 0)`; no D2004, src é sempre 0 (textura inteira esticada no rect) | C | todas as chamadas DRAW em o_9c0e0c5…o_9c1a41f |
| Alpha | 0xff; exceção: standings usam `ROT%(col)*255/100` | C | 0x9c13a52 (`call [edx+0x10]` → imul 0xff → idiv 0x64) |
| Texto | `FONT.vt+0x18(&rect, hAlign 0=esq/1=centro/2=dir, vAlign=1 (centro), ARGB, str)` | C | idem |
| Recuo do texto | FH>>2 (FH/4) somado em L (alinhamento à esquerda) ou subtraído de R (à direita) | C | idem |
| FONT_BIG | segunda fonte, usada só nos números das caixas de posição 1.5FH×2FH | C | 0x9c10897, 0x9c111e7 |
| Escala | todas as medidas derivam de W (largura da tela) e FH; não há coordenadas absolutas | C | — |

**Cores (ARGB):**
- `0xff010101`: texto preto (sobre branco/amarelo).
- `0xfffefefe`: texto branco.
- `0xffd4d0c8`: nome curto da sessão no time left.
- As caixas são texturas sólidas, sem tinta por ARGB: white, blue, black, yellow, green, red, positions_button, leader_button, digital_flag e siemens_2004.tga (C quanto ao uso; H quanto aos nomes exatos dos arquivos, exceto siemens_2004.tga e digital_flag).

## 2. Animador (ANIM = this+0x39d) – vale para todas as telas

C (asm de 0x9c0164a):

- **Métodos:** vt0 Update(nLinhas, rotate, keyA, keyB); vt4 FADE%(i); vt8 ROTtri(i); vt10 ROT%(i); vt14 RESTART; vt18 FADEOUT; vt1c FADEOUTDONE; vt20 SETTLED(i); vt24 FADEDONE.
- **Update:** retorna 1 se keyA mudou (então `G_9c31455 = 1` e o Siemens é exibido), 2 se só keyB (a página) mudou (o Siemens não reaparece), 0 caso contrário.
- **Fase 1, fade/wipe:** 10 passos × `FadingSpeed` ms. FADE% vai de 0 a 100, e a largura do rect é `L + (R−L)·FADE%/100` (wipe da esquerda para a direita), em branco.
- **Fase 2, rotação sequencial:** uma linha por vez. Na linha atual, rot% = passo·10 (0..90); nas outras, 100. ROTtri = r>50 ? 100−r : r, e o desenho usa `T += tri·FH/100; B −= tri·FH/100`, ou seja, um flip vertical: a altura encolhe até 0 e volta.
- **Antes de assentar** a caixa é desenhada em **white** e sem texto. Com `FADEDONE && SETTLED(i)`, troca para a cor final e o texto aparece.
- **Standings:** o índice do animador é a coluna (0 = posição, 1 = nome, 2 = gap, 3 = Siemens). Em vez de flip, a altura cresce de cima para baixo (`B = T + ROT%·FH/100`), com alpha = ROT%·255/100. Cada coluna só começa depois que a anterior assenta.
- **Siemens:** a penúltima linha assentada libera a última, que gira em branco. Ao assentar, mostra `siemens_2004.tga` por 6000 ms contados a partir do último quadro branco. Só com `ShowSiemensLogo` e se `G_9c31455`.
- **Saída:** FADEOUT/RESTART via flag this+0x3a5 (usado no split de quali).

## 3. Despacho, teclas e prioridades

Teclas (C, 0x9c274f0): e84 ToggleOnOff, e88 ToggleSpeedSplit, e8c SAT_Standings, e90 TowerStandings, e94 TowerLeft, e98 ScrollNext, e9c TimeLeft, ea0 DirectorsCut, ea4 InfoScreen, ea8 TrackMap, eac MicroTime.

- **ShowMode `[0x9c35108]`** (mod 3): 0 = tudo; 1 = só sky messages + time left; 2 = nada (0x9c25f59 retorna 0). C.
- **SpeedSplit `[0x9c3511c]`:**
  - corrida (mod 3): 0 gap à frente (0x9c14804), 1 gap atrás (0x9c15ab4), 2 speed trap (0x9c16dc6);
  - fora da corrida (mod 2): 0 split de quali (0x9c13c6d → 0x9c11ed2/0x9c111e7), 1 speed trap. C.
- **Standings:**
  - corrida `[0x9c35110]` (mod 3): 0 desligado, 1 standings (0x9c18ac9), 2 pit stops (0x9c180ab); pressionar zera o DirectorsCut. C.
  - fora da corrida `[0x9c35118]` (mod 2, padrão 1): standings de quali 0x9c1324d, só se 0x9c26059 for verdadeiro (P: há mais de 1 tempo).

**Corrida (0x9c0df02, C):**
1. Lap counter (se ShowTimeLeft).
2. Se ShowMode == 0, pela ordem de prioridade:
   1. carro em foco com `vt0x110` (drive-through) → 0x9c19b0a;
   2. `vt+0xc0` (P: carro em foco nos boxes) → 0x9c19615;
   3. DirectorsCut → 0x9c1a41f;
   4. standings 1/2;
   5. switch do SpeedSplit.

**Fora da corrida (0x9c0dffc, C):** se ShowMode == 0, standings (se ligado e houver tempos), senão SpeedSplit. Depois vem o time left.

**Sky messages (0x9c0e07d / 0x9c25e6d):** hooks no código do GP3 (this+0x365..0x379, definidos em 0x9c229c8) ligam as flags 0x9c35124..29. O mapeamento é P:

| Flag | Tela | Função |
|---|---|---|
| 35124 | volta mais rápida | 0x9c0e0c5 |
| 35125 | fora da corrida | 0x9c0e9c4 |
| 35126 | nos boxes | 0x9c0ef10 |
| 35127 | vencedor | 0x9c0f455 |
| 35128 | riding with | 0x9c10897 |
| 35129 | drive-through (zerada depois) | 0x9c19b0a |

O time left/lap counter continua sendo desenhado junto com elas.

## 4. Telas

Convenções:
- L0 = X0 + W/5 = 204.8.
- **C1** = coluna 1: [L0, L0+W/5) = 204.8..409.6.
- **C2** = coluna 2, largura W/5−FH (187.8): 409.6..597.4.
- **C3** = coluna 3: começa em 597.4, largura W/5 (até 802.2).
- Linhas com altura FH; "−nFH" = T = SCR_B − n·FH.
- Texto: preto sobre branco/amarelo, branco sobre azul/preto/verde/vermelho.

### 4.1 Time left (0x9c0db6b) e lap counter (0x9c0dda0) – C
- Caixa principal com largura W/7.5 (136.5), centrada em CX = W/2, T = SCR_T + 2FH (34), altura FH. É amarela, ou branca se ainda não assentou. Mostra "h:mm:ss"/"m:ss" (0x9c28f97) ou o número da volta, em texto preto, centralizado.
- À esquerda, colada, uma caixa preta de largura 2FH com o nome curto da sessão em 0xffd4d0c8.

### 4.2 Volta mais rápida (0x9c0e0c5; 4 linhas; key 2/carro) – C (layout), P (gatilho)
- C1: −5FH branco, nome do piloto (preto, esquerda); −4FH azul, equipe (branco, esquerda).
- C2: −5FH azul "Fastest Lap" (string 0x12, esquerda); −4FH preto, tempo da volta (`car.vt0x118`, formato 0x9c28ec9, direita).

### 4.3 Fora da corrida (0x9c0e9c4; 2 linhas; key 0xb) – C
- C1: −4FH branco, nome.
- C2: −4FH preto "Out of the race" (string 7, direita).

### 4.4 Nos boxes (0x9c0ef10; key 5) – C
Layout igual ao 4.3, com "In the pits" (0xb). Se o carro for o em foco, desvia para 4.5.

### 4.5 Timer de boxes do carro em foco (0x9c19615; 2 linhas; key 8) – C
- C1: −4FH branco, nome.
- C2: −4FH preto, timer `TimingInfo[car·0x1e+0x24]` no formato de décimos 0x9c2905f ("m:ss.t"/"s.t"; "0.0" se vazio), direita.

### 4.6 Drive-through (0x9c19b0a; 4 linhas; key 7) – C
- C1: −4FH branco, nome; −3FH azul, equipe.
- C2: −4FH preto "Drive Through" (0x26, esquerda); −3FH preto "Penalty" (0x29, esquerda).

### 4.7 Vencedor (0x9c0f455; 8 linhas; key 3) – C (layout), H (sobreposição)
- Siemens (linha 7): L0, −5FH, W/5 × FH.
- Bandeira: quadrado 2FH×2FH em L0, T = −4FH (branco → digital_flag).
- Coluna em L0+2FH, largura W/5:
  - −5FH vermelho "Winner" (0x11);
  - −4FH branco, nome (preto);
  - −3FH azul, equipe; na ponta direita, um quadrado FH com o logo (0x9c23f05 → 0x9c26666).
- Coluna 3: L = fim anterior + FH, largura (W−FH)/5, preto, texto branco à direita. A borda esquerda anima por FADE(4).
  - tempo de corrida (0x9c28ec9);
  - distância "%.3f %s" = 4.8768·voltas·comprimento/(1609 ou 1000) (H quanto à fórmula exata);
  - velocidade média "%.3f %s" = dist·3600/(t/1000).

### 4.8 Riding with (0x9c10897; 4 linhas; key 0xc) – C
- Siemens: −5FH, L0, W/5 (linhas 2/3).
- C1: −4FH branco, nome; −3FH azul, equipe.
- Caixa de posição em L = fim de C1, largura 1.5FH, altura 2FH (−4FH..−2FH): positions_button, ou leader_button se P1. Número em FONT_BIG, centralizado, branco.
- Posição = `vt0x90` na corrida, senão a posição de quali (0x9c2924e).

### 4.9 Cartão genérico (0x9c111e7(key, nome, equipe, txt3, txt4, pos); 6 linhas) – C
- C1: branco, nome; azul, equipe.
- C2: preto, txt3 (direita, branco); amarelo, txt4 (direita, preto).
- Caixa de posição 1.5FH×2FH com FONT_BIG.
- Usado pelo split de quali fora da volta cronometrada:
  - txt3 = melhor tempo;
  - txt4 = "+ gap"/"- gap" ou voltas ("%d %s" Laps completed / "%d/%d %s").

### 4.10 Split de quali ao vivo (0x9c11ed2; 9 linhas; key 4) – C (layout), H (linha a14)
- Siemens: −6FH (linhas 7/8).
- C1:
  - −5FH branco, nome do carro em foco (preto, direita);
  - −4FH preto, com L recuado FH, tempo atual/no split (branco, direita);
  - linha opcional a14 (H).
- C2:
  - −5FH azul, rótulo do split "Intermediate 1"/"Intermediate 2"/"Finish Line" (centralizado);
  - −4FH, delta "+/-": amarelo com texto preto se mais lento, verde com texto branco se mais rápido.
- C3: caixa de posição 1.5FH×2FH, OU referência (branco W/5 com o nome; preto com largura −FH com o tempo de referência; linha extra).
- Splits: índice `G_9c33d30` (0..2), segmentos em 0x9c34230/0x9c3422c, passo 0xc.

### 4.11 Standings: quali 0x9c1324d (key 9), corrida 0x9c18ac9 (key 0xa), pit stops 0x9c180ab (key 0x12) – C
- CarsPerPage (this+0xc) = 8: duas colunas de 4. A página fica em this+0x10.
- Linha i: y = SCR_B − 6FH + (i%4)·FH. x base = X0 + W/5 + (i/4)·(6FH + W/5).
- Caixas da linha:
  - posição FH×FH, azul (vermelha se P1), "%d" centralizado branco;
  - nome W/5 branco, preto à esquerda (recuo FH/4);
  - gap 4FH preto, branco à direita.
- Siemens em (X0+FH+W/5, SCR_B−7FH, W/5, FH), só na primeira exibição, por 6 s.
- Animação por coluna (seção 2). Limite de carros: 22 (quali) ou MaxCarsRace.
- Texto do gap:
  - quali: líder = melhor tempo; demais "+ %s"; carro sem tempo fica sem a caixa de posição;
  - corrida: líder "Lap %d"; demais "+ %d Lap"/"+ %d Laps" ou o gap em tempo (sem "+"); fora = "  Out" (0x19), sem caixa de posição;
  - pit stops: "%d Stop"/"%d Stops" (strings 4/5).
- Exemplo em px: linha 0 em y = 666; coluna 2 começa em x = 204.8 + 306.8 = 511.6.

### 4.12 Gap carro à frente (0x9c14804; 6 linhas; key 0xe) – C
- Só aparece dentro da janela do split.
- Faixa em −4FH, da esquerda para a direita:
  1. caixa FH com P−1 (azul, vermelha se P1);
  2. nome do carro à frente, W/5 branco, texto à direita;
  3. gap, W/5−FH preto, centralizado: "+ N Lap(s)" ou "%s %s" (sinal + tempo);
  4. nome do carro em foco, W/5 branco, à esquerda;
  5. caixa FH com P.
- Abaixo do gap (−3FH), uma linha amarela com "(±x)" em preto (variação).
- Se o carro em foco for P1: só [nome][P1].

### 4.13 Gap carro atrás (0x9c15ab4; key 0xf) – C (layout), H (condição da linha amarela)
- Espelho do 4.12: [P][nome em foco, à direita][gap preto: "0.0"/timer/"%s %s"][linha amarela abaixo com "(±x)"][nome do carro atrás][P+1].
- Só se pos < número de carros.

### 4.14 Speed trap (0x9c16dc6; 8 linhas; key 0xd) – C
- C1:
  - −5FH branco, nome (direita);
  - −4FH preto (recuo FH), velocidade atual;
  - −3FH preto, melhor/segunda velocidade (se > 0).
- C2: −5FH azul "Top Speeds" (0x23); −4FH azul "Current Speed" (0x24).
- C3: branco W/5, nome do mais rápido no trap (esquerda); preto (largura −FH), top speed (esquerda, branco).
- Siemens: −6FH.
- Velocidade = (raw>>16)·`G_9c34a88`·3.6, texto "%.1f %s" (unidade em 0x9c34a74). Há 3 traps em 0x9c34250 + i·0xd4, com janela de 2·SpeedTrapWidth (`G_9c344dc`).

### 4.15 Director's cut (0x9c1a41f) – C/P
- Escolhe entre gap à frente e gap atrás pelo menor gap.
- rand%10 < 3 → speed trap (stickiness por 0x9c336b4/5).
- No split 2 com o flag 0x9c31457: standings, trocando de página a cada 6 s; rand%5 == 0 → pit stops, senão normal.

## 5. Tabelas de dados (C)
- **Nome do piloto:** 0x9c34a9c + idx·0x18.
- **Equipe:** 0x9c34fdc + ((((idx+1)&0x3f)−1)>>1)·0x1b.
- **Carro em foco:** `g_pGPx.vt0xcc()` (câmera). `G_9c33d2e` = índice, `G_9c33d2c` = posição (vt0x90), `G_9c33d28` = posição na pista.
- **Formatadores de tempo:**
  - 0x9c28ec9: "%lu:%02lu:%02lu.%03lu" / "%lu:%02lu.%03lu" / "%lu.%03lu"; inválido = "-:--.---";
  - 0x9c28f97: "h:mm:ss" / "m:ss";
  - 0x9c2905f: décimos.
- **Strings (GetString n):** 4/5 Stop(s), 7 Out of the race, 0xb In the pits, 0x11 Winner, 0x12 Fastest Lap, 0x19 Out, 0x23 Top Speeds, 0x24 Current Speed, 0x26 Drive Through, 0x29 Penalty.
- **Keys do animador:** 2 FL, 3 Winner, 4 split quali, 5 pits, 7 DT, 8 pit timer, 9 stand. quali, 0xa stand. corrida, 0xb out, 0xc riding, 0xd trap, 0xe gap à frente, 0xf gap atrás, 0x12 pit stops.

## 6. Lacunas
- Valor real de FH (retorno de FONT.vt+0xc); os px acima assumem 17. Os tamanhos e arquivos das fontes não foram confirmados.
- Semântica exata de car.vt4, vt0x110 e vt0x54 (pits/DT inferidos pelas strings).
- Fórmula exata da distância e da velocidade média do Winner, e os valores das unidades (BSS, só em runtime).
- Condição da linha amarela no gap atrás; significado da linha a14/a10 no split de quali.
- Ordem Z exata Siemens/Winner (−5FH sobreposto).
- O SpeedSplit automático 0x9c205e7 parece ser do D2010 (não confirmado). 0x9c1aaaa..0x9c1ae67, 0x9c1a8d3, 0x9c200e7 e 0x9c1fb5f foram atribuídos ao D2010.
- Tower (e90/e94), track map, microtime e info screen são tratados na base comum, fora das funções do D2004: não decodificados aqui.
- Os nomes dos arquivos .tga além de siemens_2004/digital_flag não foram verificados contra a lista de recursos.
- O mapeamento flag→tela das sky messages é P (pelo conteúdo de cada função).
