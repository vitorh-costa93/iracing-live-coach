# GPxSAT – modo "Digital" (estilo 1999): especificação de layout (engenharia reversa estática)

Fonte: GPxSAT descompactado (base 0x9c00000), desmontado em `sat5.asm` (classe Digital ≈ linhas 6584–13531),
decompilado com `sim.py` via `rundg.py` (saídas `dg/o_<VA>.txt`). Texturas convertidas por `tga2png.py` (`dg/tyres.png`).
Confiança: **C** = certo (lido no asm), **P** = provável, **H** = hipótese. Formato e convenções iguais a `spec_digital2004.md`;
o que não é citado aqui é idêntico ao D2004 (teclas, ShowMode, SpeedSplit, standings toggles, formatos de tempo).

Referência: 1024x768. Geometria base (0x9c262da, C): `W = min(SCR_R, H·1024/768)` (salvo this+0x33),
`X0 = (SCR_R−W)/2` (pillarbox), `X1 = X0+W`, `CX = (X0+X1)/2`. Em 1024x768: X0=0, W=1024, CX=512, X1=1024.
**FH** = FONT.vt+0xc. A fonte é `futura` (fonts.ini: Futura-Condensed-Bold, `gfx\fonts\f0000819.ttf`, Height=18,
Quality=1), criada com escala 1.0 em 0x9c2384b → **H: FH = 18 px** em 768. Todas as divisões são inteiras
(W/3=341, W/4=256, W/10=102, W>>1=512). Px abaixo: FH=18, 2FH=36, 3FH=54.

**Notação de linha:** `−nFH` = faixa com `T = SCR_B − n·FH`, `B = T + FH`. Em 768: −4FH=696..714, −3FH=714..732,
−2FH=732..750, −FH=750..768. O layout padrão é uma **faixa de 2 linhas** (−3FH e −2FH) no rodapé.

---
## 1. API de desenho e escala

| Item | Detalhe | Conf. | Evidência |
|---|---|---|---|
| Classe | vtable 0x9c2f438; construtor 0x9c27020: CarsPerPage (this+0xc)=6, OPT_TIMELEFT=1, DriverNameFormat (this+0x39d)=0 | C | 0x9c27020 |
| Sem animador | this+0x39d é um byte de opção, não ANIM. Nenhum fade/flip/wipe: tudo é estático, desenhado a cada quadro | C | 0x9c27020, ausência de chamadas ANIM nas telas |
| Caixa sólida | `PDX.vt+0x3c(&rect, c1, c2, c3, c4)`: preenchimento por cor de vértice, as 4 sempre iguais (sem gradiente) | C | todas as telas |
| Caixa translúcida 1x | `sub_9c26546(rect)` = `PDX.vt+0x38(rect, AlphaValue, 0)`; `[Misc] AlphaValue` padrão 0x40 | C | 0x9c26546 |
| Caixa translúcida 2x | `sub_9c26565(rect)` = idem com alpha `min(2·AlphaValue, 255)` (0x80) | C | 0x9c26565 |
| Cor da translúcida | preto | H | vt0x38 não recebe cor |
| Texto | `FONT.vt+0x18(&rect, hAlign 0/1/2, vAlign, ARGB, str)`; vAlign=1 (centro), exceto standings (0 = topo) | C | — |
| Helper de texto | 0x9c07aab: `L += FH/3; R −= FH/3`, FONT.TEXT, restaura o rect. Recuo = **FH/3 = 6 px** (D2004: FH/4) | C | 0x9c07aab |
| Tile de posição | `sub_9c265de(rect, n)`: `digital_yellow.tga` 512x256, grade 8x4 de 64x64; src = `{(n%8)·64, (n/8)·64, +64, +64}`; n 0-based (n=0 → "1"); números 1..22 pretos sobre amarelo; desenhado num quadrado FH×FH | C | 0x9c265de, textura |
| Logo de pneu | `sub_9c26666(rect, idx)`, tabela 0x9c3515c; idx = `sub_9c23f05(car)`, −1 = não desenha | C | 0x9c26666, 0x9c23f05 |
| Nome do piloto | 0x9c0d705: `DriverNameFormat ? GetDriverName : GetShortDriverName(idx+1, 1)` | C | 0x9c0d705 |

**Cores (ARGB, C):**

| Cor | ARGB | Uso |
|---|---|---|
| Azul | `0xff1675a5` | caixas de nome |
| Cinza | `0xffd4d0c8` | caixas de equipe / rótulos |
| Amarelo | `0xfff7dc42` | caixas de rótulo da tela (Fastest Lap, In the pits…) e **texto** amarelo (gap, laps, distância, Jump Start) |
| Vermelho | `0xfffe0000` | pit timer, penalidade |
| Texto preto | `0xff010101` | sobre cinza/amarelo |
| Texto branco | `0xfffefefe` | sobre azul/vermelho/translúcido |
| Amarelo puro | `0xffffff00` | top speed no speed trap |

**Texturas (todas em `gfx\`):**

| Arquivo | Tamanho | Conteúdo | Uso |
|---|---|---|---|
| `digital_yellow.tga` (ini `Yellow`) | 512x256 | sprite de posições 1..22 | tiles de posição |
| `digital_flag.tga` (ini `Flag`) | 128x128 | bandeira quadriculada com alpha | Winner |
| `big1.tga` | 128x128 | "1" grande sobre amarelo | Pole position |
| `digital_a/b/c/d/e/f/g/m/p.tga` | 64x64 | letra da marca de pneu: A branco/preto, B branco/vermelho, C preto/laranja, D preto/amarelo, E branco/azul, F branco/vermelho, G branco/dourado, M branco/azul, P vermelho/amarelo | logo de pneu; carga em 0x9c2397c via `[Tyres] SatType%02d` e `[Digital] Type1=digital_m.tga, Type2=digital_b.tga` |

## 2. Animador

Não existe (C). Diferente do D2004, não há fade, flip, Siemens, FadingSpeed nem RESTART. A única temporização é a
paginação das standings no Director's Cut (6000 ms) e a janela de split/speed trap (igual ao D2004).

## 3. Despacho e prioridades

Teclas, ShowMode, SpeedSplit e toggles de standings: **iguais ao D2004** (C, base compartilhada).

**Corrida (vt0x90 = 0x9c07561, C):**
1. Se `PLAYER_CAR.vt4 && !vt0x110` (carro do jogador nos boxes, sem penalidade) → **só** o pit timer 0x9c0cc7c; retorna.
2. Lap counter (se OPT_TIMELEFT).
3. Flag `0x9c35129` → drive-through/jump start 0x9c0cdfd; zera a flag; retorna.
4. Se ShowMode == 0:
   1. DirectorsCut (byte 0x9c34a99) → 0x9c0d281;
   2. senão standings `[0x9c35110]`: 1 → 0x9c0c4d4 (corrida), 2 → 0x9c0be70 (pit stops);
   3. senão SpeedSplit `[0x9c3511c]`: 0 → gap à frente 0x9c0aa64, 1 → gap atrás 0x9c0b19f, 2 → speed trap 0x9c0b8cb.

**Fora da corrida (vt0x94 = 0x9c0765a, C):** se ShowMode == 0: standings `[0x9c35118]` == 1 e 0x9c26059 → quali
standings 0x9c099da; senão SpeedSplit 0 → split de quali 0x9c0a070, 1 → speed trap. Depois time left (se OPT_TIMELEFT).

**Sky messages (vt0x88 = 0x9c076c9, C):** base 0x9c25e6d (escolhe a mensagem e chama vt0x98..0xa8), depois, se
OPT_TIMELEFT, lap counter (corrida) ou time left. Telas de sky: riding with 0x9c08a05, in pits 0x9c07e70, fastest lap
0x9c07705, out of race 0x9c07b21, winner 0x9c08166 (prioridade/tempo de exibição: base, igual ao D2004).

## 4. Telas

Salvo indicação, texto via helper (recuo FH/3, vAlign centro). "Equipe" = linha cinza X0..CX em −3FH com texto da equipe
preto à esquerda a partir de X0+2FH (36) e logo de pneu em X0+FH..X0+2FH (18..36), FH×FH. Px em [colchetes].

### 4.1 Time left (0x9c087d0) e lap counter (0x9c088f1) – C
- **Sem caixa.** Rect = (SCR_L, FH, SCR_R, 2FH) [0,18,1024,36], hAlign centro, vAlign centro.
- Sombra: primeiro em preto `0xff010101` no rect; depois em branco `0xfffefefe` com o rect deslocado −1,−1
  (sombra preta a +1,+1).
- Time left: `h:mm:ss` / `m:ss` (0x9c28f97); acabou → "Session over" (0x1a). Sem nome curto de sessão (≠ D2004).
- Lap counter `"%d/%d"`: `LapsLeftMode` (this+0x27) == 0 → voltas restantes (`lap == 0 ? total : total − lap + 1`);
  ≠ 0 → volta atual.

### 4.2 Fastest lap (0x9c07705) – C
| Caixa | Rect | Cor | Texto |
|---|---|---|---|
| Equipe | X0..CX, −3FH [0..512, 714..732] | cinza | equipe preto esq.; logo pneu |
| Nome | X0+3FH..X0+W/3, −2FH [54..341] | azul | nome branco esq. |
| Tempo | X0+W/3..CX, −2FH [341..512] | transl. 1x | tempo (0x9c28ec9) branco dir. |
| Rótulo | CX..X1−W/10, −3FH [512..922] | amarelo | "Fastest Lap" preto esq.; "Lap %d" preto dir. |

### 4.3 Out of race (0x9c07b21) – C
- Equipe (como acima); nome azul X0+3FH..CX em −2FH [54..512].
- Amarelo CX..X1−W/10 em −3FH: `_strupr("Out of the race: <motivo>")` preto esq. (tudo em MAIÚSCULAS).
- Motivo: string GPx 0x609+vt0x6c se vt0x60; senão "Accident" (9) se `vt0x98 & 0xffff`; senão "Out of fuel" (0xa).

### 4.4 In pits (0x9c07e70) – C
- Carro em foco → pit timer 0x9c0cc7c (4.5).
- Outro carro: nome azul X0+3FH..CX em −2FH; tile (pos−1) em X0+2FH..X0+3FH, −2FH [36..54]; equipe em −3FH;
  amarelo CX..X1−W/10 em −3FH com "In the pits" (0xb) preto esq.

### 4.5 Pit timer do carro em foco (0x9c0cc7c) – C
- Vermelho X0+3FH..X0+W/3 em −3FH [54..341, 714..732]: "Pit stop" (3) branco esq.; timer (0x9c2905f, TimingInfo[car·0x1e+0x24]) branco dir.
- Azul X0+3FH..X0+W/3 em −2FH: nome branco esq.

### 4.6 Race winner (0x9c08166) – C
| Elemento | Rect | Cor | Texto |
|---|---|---|---|
| Bandeira | X0+2FH..X0+5FH, SCR_B−6FH..SCR_B−3FH [36..90, 660..714] (3FH²) | digital_flag.tga | — |
| Nome | X0+3FH..X0+W/3, −2FH | azul | nome branco esq. |
| Equipe | X0..CX, −3FH | cinza | equipe + logo |
| Tile | X0+2FH..X0+3FH, −2FH | tile "1" | — |
| Tempo | X0+W/3..CX, −2FH | transl. 1x | tempo de corrida branco dir. |
| Média | CX..X1−W/3, −2FH [512..683] | cinza | `"%.3f %s"` velocidade média, preto **centro** (FONT.TEXT direto, sem recuo) |
| Distância | X1−W/3..X1−W/10, −2FH [683..922] | transl. 1x | `"%.3f %s"` amarelo `0xfff7dc42` centro |
| Rótulo | CX..X1−W/10, −3FH | amarelo | "Race winner" (0x10) preto esq. |

Distância = `4.8768·(vt0x40(0) & 0xffff)·voltas / (vt0x78 ? 1000 : 1609)` (km ou milhas). C.

### 4.7 Riding with (0x9c08a05) – C
- Equipe em −3FH (texto a partir de X0+2FH, logo X0+FH).
- Azul X0+W/4..CX em −2FH [256..512].
- Posição: corrida (0x9c0d750) → vt0x90−1; quali → posição de quali se houver tempo.
  - Com posição: tile em X0+W/4..X0+W/4+FH [256..274]; nome branco esq. a partir de X0+W/4+FH.
  - Sem posição: cinza X0..X0+W/4 em −2FH; nome a partir de X0+W/4.

### 4.8 Card genérico (0x9c08e7a; carnr, tyre, name, team, best, gap, laps, pos) – C
Usado pelo split de quali fora de volta lançada.
- Transl. 2x CX..X1 em −3FH; cinza X0..CX em −3FH (equipe preto X0+2FH..CX; logo X0+FH).
- Azul em −2FH: `pos == −1 ? X0+W/4..CX : X0+3FH..X0+W/3`; nome branco esq.
- pos ≠ −1 → tile em X0+2FH..X0+3FH, −2FH; senão cinza X0..X0+W/4 em −2FH.
- Best: transl. 2x X0+W/3..CX em −2FH, branco dir.
- Gap: transl. 1x CX..X1 em −2FH, amarelo esq.; laps: amarelo esq. em CX..X1, −3FH.
- Textos iguais ao D2004: `"+ %s"`, `"%d %s"` (Laps completed), `"%d/%d %s"`, "Warm-up lap".

### 4.9 Split de quali ao vivo (0x9c093e6; refName, refTime, delta, splitLabel, name, curTime, pos) – C
Dispatcher 0x9c0a070 (lógica de janelas/splits igual ao D2004).
- pos ≠ −1:
  - quali (0x9c0d780) e pos == 0: amarelo CX..X1−W/10 em −3FH "Pole position" (0x1f) preto esq.; `big1.tga` em
    (CX−3FH, SCR_B−6FH, CX, SCR_B−3FH) [458..512, 660..714];
  - tile pos em X0+2FH..X0+3FH, −2FH.
- pos ≠ 0 e refName: tile "1" em CX..CX+FH, −3FH [512..530]; azul CX+FH..CX+W/4 [530..768] com refName branco esq.;
  refTime em transl. X0+W/3..CX, −3FH, branco dir.
- Delta: transl. CX..X1−W/3 em −2FH [512..683], amarelo esq. com `L += FH` (FONT.TEXT direto); formato `"%s %s"`, sinal "+"/"-".
- Rótulo do split: cinza X0..X0+W/3 em −3FH, preto **dir.**
- Nome: azul X0+3FH..X0+W/3 em −2FH, branco esq.; curTime: transl. 2x X0+W/3..CX em −2FH, branco dir.

### 4.10 Standings (quali 0x9c099da, corrida 0x9c0c4d4, pit stops 0x9c0be70) – C
- CarsPerPage = 6 (ini); linhas por coluna `R = (CPP+1)/2 = 3`; 2 colunas.
- Cabeçalho: cinza X0+FH..CX, `T = SCR_B−(R+2)FH−2`, `B = SCR_B−(R+1)FH−2` [18..512, 676..694]; "Classification" (0x20) preto esq.
- Item i: `k = i − page·CPP`, `col = k/R`, `row = k%R`, `xoff = col·(W>>1)`, `y = SCR_B−(R+1)FH + row·(FH+2)`
  [linhas 696, 716, 736]. Coluna 0: padL=FH, padR=0; coluna 1: padL=0, padR=FH.
  - tile (i) em X0+xoff+padL .. +FH;
  - azul `X0+padL+xoff+FH+2 .. X0+xoff+W/3−padR`;
  - transl. `X0+xoff+W/3−padR .. CX+xoff−padR`;
  - texto em `L = X0+padL+xoff+FH`, `R = CX+xoff−padR`: nome branco esq., gap branco dir., **vAlign 0 (topo)**.
  - Px col 0: tile 18..36, azul 38..341, transl. 341..512. Col 1: tile 512..530, azul 532..835, transl. 835..1006.
- Quali: ordem vt0x114, limite 22; tile só para quem tem tempo; líder = melhor tempo, demais `"+ %s"`.
- Corrida: ordem vt0x108, limite MaxCarsRace (0x9c31f70, 1..22); líder "Lap %d"; demais `"+ %d Lap(s)"` ou gap de tempo;
  fora (vt0x54) → `"  Out"`, sem tile.
- Pit stops: `"%d Stop"` (n==1) / `"%d Stops"`; fora → `"  Out"`.

### 4.11 Gap à frente (0x9c0aa64) – C
Só se `G_9c33d32 > 0` e dentro da janela de split.
- P > 1: azul X0+3FH..X0+W/3 em −3FH (carro à frente); tile P−1 em X0+2FH, −3FH; transl. 2x X0+W/3..CX em −3FH (gap principal);
  transl. 1x X0+W/3..CX em −2FH (secundário).
  - Voltas atrás → `"+ %d Lap(s)"` centro na caixa de baixo (vAlign 0);
  - janela do próximo split → `"(± x)"` de 0x9c33d71 na caixa de baixo;
  - senão principal `"%s %s"` (sinal "-" ou "") branco centro em cima + `"(± gap do split anterior)"` embaixo se ≠ 0.
- Sempre: azul CX+FH..X1−W/4 em −2FH [530..768] com o carro em foco; tile P em CX..CX+FH, −2FH.

### 4.12 Gap atrás (0x9c0b19f) – C
- Azul X0+3FH..X0+W/3 em −3FH com o carro em foco; tile P em X0+2FH.
- P < vt0xb0 (nº de carros): transl. 2x X0+W/3..CX em −3FH (texto principal branco centro); transl. 1x em −2FH (`"(±x)"`);
  azul CX+FH..X1−W/4 em −2FH com o carro de trás; tile P+1 em CX.
- Principal: janela do próximo split → "0.0" + `"(± último gap)"`; split atual → timer ao vivo `"%s"` em décimos se o de trás
  ainda não chegou ao split, senão gap `"%s %s"`.

### 4.13 Speed trap (0x9c0b8cb) – C
Para cada um dos 3 traps, ativo com `pos ∈ [trap, trap + 2·SpeedTrapWidth]` (`[Misc] SpeedTrapWidth=75`).
| Caixa | Rect | Cor | Texto |
|---|---|---|---|
| Nome mais rápido | X0+FH..X0+W/3, SCR_B−4FH−3..SCR_B−3FH−3 [18..341, 693..711] | azul | nome branco esq. |
| Top speed | X0+FH..X0+W/3, −3FH | transl. 1x | `"%.1f %s"` `0xffffff00` esq. |
| Rótulo 1 | X0+W/3..X1−W/3, −3FH [341..683] | cinza | "Top Speeds" (0x23) preto centro |
| Rótulo 2 | X0+W/3..X1−W/3, SCR_B−2FH+3..SCR_B−FH+3 [735..753] | cinza | "Current Speed" (0x24) preto centro |
| Foco nome | X1−W/3..X1−FH, −4FH−3 [683..1006] | azul | nome branco **dir.** |
| Foco melhor | X1−W/3..X1−FH, −3FH | transl. 1x | melhor no trap, amarelo dir. |
| Foco atual | X1−W/3..X1−FH, −2FH+3 | transl. 1x | velocidade atual amarelo dir. (só se > 0) |

Velocidade = `(raw>>16)·G_9c34a88·3.6`. C.

### 4.14 Drive-through / jump start (0x9c0cdfd) – C
- `jump = IS_RACE && PLAYER_CAR.vt0x18() < 1` (P: ainda na volta 0).
- Cinza **L = 0 absoluto** (não X0)..CX, SCR_B−3FH..SCR_B−FH (2FH de altura); se jump, sobe FH.
- Logo de pneu em X0+FH/2 (FH²) na linha de cima; equipe preto esq. a partir de X0+2FH na linha de cima.
- Azul X0+W/4..CX na linha de baixo: `GetShortDriverName(idx, 0)` branco esq., recuo FH/2.
- Jump: transl. 0..X1−W/4 em −2FH com "Jump Start" (0x25) amarelo dir. (R −= FH/3); vermelho X1−W/4..X1 em −3FH [768..1024]
  com "10 Seconds" (0x27) branco esq., recuo FH/2.
- Senão: vermelho CX..X1 em −3FH com "Drive Through Penalty" (0x26 + 0x29) branco dir., `R = X1−2FH`; transl. CX..X1 em −2FH.

### 4.15 Director's cut (0x9c0d281) – C
- Elegível: gap à frente se na janela e P > 1; gap atrás se na janela e P < nº de carros. Ambos → o menor gap (v18 < v4 → atrás).
- Speed trap "grudento": `rand()%10 < 3` → speed trap (flags 0x9c3366c/0x9c3366d).
- No índice de split 2 com a flag 0x9c31218: standings; página +1 a cada 6000 ms (0x1770), volta a 0 em MaxCars;
  `lap%5 == 0` → pit stops, senão standings de corrida.

## 5. Tabelas de dados

- **GetString:** 0 Intermediate 1, 1 Intermediate 2, 2 Finish Line, 3 Pit stop, 4 Stop, 5 Stops, 6 Warm-up lap,
  7 Out of the race, 9 Accident, 0xa Out of fuel, 0xb In the pits, 0x10 Race winner, 0x12 Fastest Lap, 0x13 Lap,
  0x16 Laps completed, 0x19 Out, 0x1a Session over, 0x1f Pole position, 0x20 Classification, 0x23 Top Speeds,
  0x24 Current Speed, 0x25 Jump Start, 0x26 Drive Through, 0x27 10 Seconds, 0x29 Penalty. C.
- **Sinais:** 0x9c313xx = "+"/"-"; 0x9c33678/0x9c3367c = ""; 0x9c3139c/0x9c313a0 = "%s". C.
- **Ini `[Digital]`** (ini load 0x9c073e0 / save 0x9c0745d): `ShowTimeLeft`, `LapsLeftMode`, `DriverNameFormat`,
  `Yellow`, `Flag`, `Type1`, `Type2`, `CarsPerPage`. Instalado: `CarsPerPage=6, Type1=digital_m.tga, Type2=digital_b.tga`.
  `[Misc]`: `Mode=3`, `SpeedTrapWidth=75`, `AlphaValue` (padrão 0x40). `[Tyres] SatType%02d`. Fonte: `FontDigital=futura`.
- **Vtable (Digital → D2004):** 0x98 riding with 0x9c08a05 (0x9c10897), 0x9c in pits 0x9c07e70 (0x9c0ef10),
  0xa0 fastest lap 0x9c07705 (0x9c0e0c5), 0xa4 out 0x9c07b21 (0x9c0e9c4), 0xa8 winner 0x9c08166 (0x9c0f455);
  0x84 nome "Digital" 0x9c07110; 0x80 fonte "futura" 0x9c0748b; 0x74 texturas 0x9c074b0; 0x6c unidades 0x9c07369.

## 6. Diferenças vs D2004 (resumo)

| Aspecto | Digital | D2004 |
|---|---|---|
| Animação | nenhuma (estático) | fade/wipe + flip sequencial, Siemens |
| Caixas | cor sólida PDX.vt0x3c + translúcidas pretas (AlphaValue / 2×) | texturas (white/blue/yellow…) |
| Grade | faixa de 2 linhas (−3FH/−2FH), colunas X0+3FH, W/4, W/3, CX, X1−W/3, X1−W/4, X1−W/10 | colunas W/5 |
| Posição | tile do sprite `digital_yellow.tga` FH×FH | FONT_BIG em caixa 1.5FH×2FH |
| Extras | logo de marca de pneu, bandeira + big1, cabeçalho "Classification" | Siemens |
| Time left | texto branco com sombra no topo, sem caixa, sem nome de sessão | caixa + nome curto da sessão |
| Standings | 6/página em 2 colunas × 3 linhas | coluna única animada |
| Winner | bandeira, tempo, velocidade média, distância | — |
| Penalidade | variante Jump Start / 10 Seconds | só drive-through |
| Recuo de texto | FH/3 | FH/4 |
| Fonte | futura Height 18 | digital2004 Height 17 |
| Pit do jogador | pit timer tem prioridade máxima na corrida | via vt+0xc0 |

## 7. Lacunas

- FH exato em runtime (H: 18 em 768; não confirmado se a altura escala com a resolução).
- Cor das translúcidas vt0x38 (H: preto) e semântica exata de vt0x4/0x110/0x18/0x60/0x98 do carro (P).
- Rótulo de unidades (BSS de 0x9c07369, "km/h"/"mph", "km"/"miles") não lido; tempos de exibição das sky messages = base (D2004).
- Não decodificados: 0x9c07120 (init da lista de strings), 0x9c0d276 (vazia). Artefatos do sim: argumento extra numa vt0x3c,
  rects mortos no riding with (sem efeito no desenho).
