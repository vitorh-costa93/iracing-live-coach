# GPxSAT – modo "Classic": especificação de layout (engenharia reversa estática)

Fonte: GPxSAT descompactado (`sat_rel.bin` = VA 0x9c01000), `sat5.asm` (linhas ~640–6586 = VA 0x9c017f0..0x9c07110),
decompilado com `sim.py` via `runclassic.py` (saídas `cl/c_<VA>.txt`; funções da base em `cl/b_<VA>.txt`).
Confiança: **C** = lido no asm, **P** = provável (fluxo/strings), **H** = hipótese.
Aviso sobre o sim: `cdq; sub eax,edx; sar 1` é uma divisão com sinal por 2. O sim imprime isso como `(x-?)>>1` ou
como uma subtração espúria (ex.: `((this.0xc+1)-this)>>1`), mas o valor real é `(CPP+1)/2`. Os valores abaixo já vêm corrigidos.

Referência: 1024x768, Scaling=0. W = min(SCR_R, H·4/3) = 1024, X0 = 0, X1 = 1024, CX = 512, H = 768.
**FH** = FONT.vt0xc. A fonte é "Mañana" (gstmanb0.ttf) com Height 18, logo **H: FH ≈ 18 px**, e os exemplos em px usam FH = 18.

---
## 1. API de desenho, fontes, texturas e escala

| Item | Detalhe | Conf. | Evidência |
|---|---|---|---|
| Modo Classic | índice `[0x9c3510c]` = 0 (Misc `Mode=0`; padrão do ini é 3); vtable 0x9c2f380; construtor 0x9c26f90 (CarsPerPage this+0xc = 8, this+0x24 = 1, OPT_TIMELEFT = 0) | C | 0x9c26f90 |
| Secção ini | vt0x84 → "Classic" (Scaling, CarsPerPage, RaceTowerLeftCars, QualTowerLeftCars, FontShadow, Yellow, Flag, Type1, Type2) | C | 0x9c01a..., 9c23328 |
| Escala | se Scaling = 0: W = min(SCR_R, H·4/3), com X0 centrado; senão W = SCR_R e X0 = 0. CX = (X0+X1)/2. Tudo deriva de W, FH e H; não há coordenadas absolutas | C | 9c23328 |
| Fonte | `[Font] FontClassic` (padrão "manana") → fonts.ini [manana]: FaceName=Mañana, FontFile=gfx\fonts\gstmanb0.ttf, Height=18, Bold=700, Quality=1 | C | 0x9c01c20, fonts.ini |
| FONT_C2 | mesma face com escala 1.25 (0x3ff40000), em 0x9c33624; altura FH_C2 em 0x9c33628. Usada **só** em "P E N A L T Y" | C | 0x9c01ac0, 0x9c06bcf |
| Texto | wrapper `0x9c01c45(rect, hAlign 0=esq/1=centro/2=dir, vAlign, ARGB, str)`. Com `[Classic] FontShadow` chama FONT.vt0x20 (com sombra), senão FONT.vt0x18. **vAlign = 0 (topo)** em quase tudo; = 1 (centro) no riding-with e no drive-through. **Não há recuo**: o texto encosta na borda do rect | C | 0x9c01c45 |
| Caixa escura | `0x9c26546(rect)` = PDX.vt0x38(rect, AlphaValue [Misc] (padrão 0x40), 0): preenchimento translúcido escuro. `0x9c265a9(rect, 0x64)` = idem com alpha fixo 100 | C (chamada), H (cor preta) | 0x9c26546, 0x9c265a9 |
| Gradiente | PDX.vt0x3c(rect, c1, c2, c3, c4): 4 cantos (TL, TR, BL, BR); nos usos, horizontal | C (chamada), P (ordem) | 0x9c044c7 |
| Moldura | PDX.vt0x40(rect, −1) no speed trap | C (chamada), H (contorno branco) | 0x9c05840 |
| Faixa inferior | todas as telas de baixo usam rect {X0, H−n·FH−10, X1, H}, com origem do conteúdo y0 = T+5. Com FH = 18: n=2 → T=722; n=3 → T=704; n=4 → T=686 (y0=691) | C | todas |

**Texturas (gpxpdata\gfx; nomes sobrescritos por [Classic] Yellow/Flag/Type1/Type2):**

| Var | Arquivo | Formato | Uso | Desenho |
|---|---|---|---|---|
| 0x9c35158 | classic_yellow.tga | 512×256, 24bpp RLE | atlas de quadrados amarelos com a posição "1".."22", células de 64 px | `0x9c265de(rect, idx)`: src = (idx%8·64, idx/8·64, +64, +64), idx = pos−1 |
| 0x9c3362c | classic_flag.tga | 256×256 | bandeira quadriculada | textura inteira, alpha 255 |
| 0x9c33630 | blue.tga | 512×1024 | ovais ciano com o número do carro 0..99, células de 64 px | `0x9c01ca8(rect, carNr)` |
| 0x9c33634 | winner.tga | 256×128 | letras brancas "WINNER" | textura inteira |
| 0x9c3515c[i] | Type1 = classic_m.tga (círculo azul "M"), Type2 = classic_b.tga (vermelho) | 32×32 | ícone do fornecedor de pneus | `0x9c26666(rect, type)`; type = `0x9c23f05(idx)` ([Tyres] "Team #NN" ou GPxP_GetTeamTyreBrand); −1 = sem ícone |
| — | classic_a/c/d/e/f/g/p.tga | 32×32 | só através de SatTypeNN em [Tyres] | H |

**Cores ARGB:** branco 0xfffefefe (nomes, rótulos); amarelo 0xffffff00 (tempos, valores, rótulos de split); ciano 0xff00ffff
(equipe/motor, "Fastest Lap", delta de quali); gradiente amarelo 0xfff7dc42 e preto 0xff010101.

## 2. Animação

**Não há.** O construtor do Classic não cria objeto ANIM e nenhuma função do intervalo 0x9c017f0..0x9c07110
chama ANIM, FADE ou ROT (grep em cl/c_*.txt) (C). Cada tela aparece e some de uma vez, sem fade, wipe nem flip.
Os únicos comportamentos temporizados são:
- **Director's cut:** paginação dos standings a cada 6000 ms (0x1770, via GPx.vt0x164 = relógio em ms) (C).
- **Timers ao vivo:** o pit timer, o tempo correndo do split de quali e o timer do gap atrás são recalculados a cada quadro, em décimos (`0x9c2905f`) (C).
- **this.0x3b = 1:** as telas de faixa marcam o overlay como "ocupado" (fastest lap, winner, card B, quali standings, gaps, speed trap) (C quanto à escrita, H quanto ao efeito: provavelmente suprime outra camada).

## 3. Despacho, teclas e prioridades

Teclas (0x9c27648; mesmas de D2004):
- **TimeLeft:** no Classic, alterna OPT_TIMELEFT só se ShowMode == 0. Enquanto OPT_TIMELEFT está ligado, as outras teclas (exceto ShowMode/ToggleOnOff) ficam bloqueadas. C.
- **SpeedSplit:** corrida mod 3 (também zera o DirectorsCut); fora da corrida mod 2. C.
- **Standings:** corrida mod 3; fora da corrida mod 2. C.
- **ScrollNext:** página this+0x10 += 1, voltando a 0 ao atingir MaxCarsRace (corrida) ou 22 (fora da corrida). C.
- **DirectorsCut:** alterna 0x9c34a99. C.

**Sky messages (vt0x8c 0x9c01fc4 → base 0x9c25e6d), P no mapeamento flag→tela (igual a D2004):**
- 35124 → fastest lap (0x9c02180);
- 35125 → fora da corrida (0x9c02506);
- 35126 → nos boxes (0x9c0276c);
- 35127 → vencedor (0x9c02959);
- 35128 → riding with (0x9c02f4f). O Classic testa 35128 antes da base e chama o riding-with direto (H: ignora o enable do hook);
- 35129 → drive-through (tratado no despacho de corrida).
- [Sky] Replace* e ShowInCockpit / ReplaceSkyMessagesWhenShowInCockpitIsOff decidem se as mensagens nativas do GP3 são substituídas (base, C).

**Corrida (vt0x90 0x9c01ff7, C):** a primeira condição verdadeira vence.
1. Carro em foco com vt4 (P: no pit lane) e sem vt0x110 → pit timer 0x9c067ac.
2. Flag 35129 → drive-through 0x9c06925; depois a flag é zerada.
3. ShowMode != 0 → nada.
4. OPT_TIMELEFT → lap counter 0x9c01e14 (exclusivo, sem mais nada).
5. DirectorsCut → 0x9c06c15.
6. Standings 1 → standings de corrida 0x9c061b8; Standings 2 → pit stops 0x9c05d06.
7. SpeedSplit: 0 → gap à frente 0x9c044c7; 1 → gap atrás 0x9c04e35; 2 → speed trap 0x9c05840.

**Fora da corrida (vt0x94 0x9c020f9, C), só com ShowMode == 0:**
1. OPT_TIMELEFT e GPx.vt0xa4() (sessão com limite de tempo) → time left 0x9c01d30.
2. Senão, se Standings == 1 e houver tempos → standings de quali 0x9c037ad.
3. Senão, SpeedSplit: 0 → split de quali 0x9c03c32; 1 → speed trap.

Diferença para D2004: no Classic, time left e lap counter são **exclusivos** (substituem a tela), não sobrepostos.

## 4. Telas

Notação: rect {L, T, R, B}; "esq/centro/dir" = hAlign; vAlign é topo, salvo indicação. y0 = topo da faixa + 5.
Exemplos em px com FH = 18 e W = 1024.

### 4.1 Time left (0x9c01d30) – C
- Faixa de 2 linhas {X0, H−2FH−10, X1, H} (722..768), caixa AlphaValue.
- Texto ocupa a faixa inteira, centro/centro, amarelo: "h:mm:ss" (0x9c28f97) ou "Session over" (str 0x1a) quando o tempo acaba.

### 4.2 Lap counter (0x9c01e14) – C
- Faixa de 3 linhas (704..768), caixa AlphaValue. y = T + FH/2 (713).
- Linha 1 {X0, y, X1, H}, centro, branco:
  - "Race over" (0x1b) quando faltam 0 voltas;
  - "Final lap" (0x18) quando falta 1;
  - senão "%d Laps remaining" (0x17).
  - Voltas restantes = total − voltasDoLíder + 1 (= total quando o líder ainda não fechou volta).
- Linha 2, em y += 4FH/3 (737): "Leader - <nome curto>" (0x1c), centro, branco.

### 4.3 Volta mais rápida (0x9c02180; faixa de 4 linhas) – C
- Caixa AlphaValue (686..768); y0 = 691; marca this.0x3b.
- Colunas: cw1 = textW("Fastest Lap") + textW("  ") e cw2 = textW("Lap") + textW("  ").
- Nome curto {X0+64, y0, CX, y0+FH}: esq, branco. Equipe/motor {X0+64, y0+FH+8, CX, y0+2FH+8}: esq, ciano.
- "Lap" {X1−(cw1+cw2+32), y0, X1−(cw1+32), y0+FH}: centro, amarelo. O número da volta (voltas−1) fica na mesma coluna, linha y0+FH+8: amarelo.
- "Fastest Lap" {X1−cw1, y0, X1−32, y0+FH}: centro, ciano. O tempo da volta (0x9c28ec9, m:ss.mmm) fica embaixo, em y0+FH+8: centro, amarelo.
- Sem quadrado de posição nem oval.

### 4.4 Fora da corrida (0x9c02506; 4 linhas) – C
- Caixa AlphaValue.
- "<Nome completo> Is out of the race" (0x8) {CX, y0, X1−32, y0+FH}: dir, branco.
- Motivo em MAIÚSCULAS {CX, y0+FH+8, X1−32, y0+2FH+8}: dir, amarelo.
  - Se car.vt0x60: string do GP3 0x609 + vt0x6c(50).
  - Senão: vt0x98 ? "Accident" (0x9) : "Out of fuel" (0xa).

### 4.5 Nos boxes (0x9c0276c; 4 linhas) – C
- Se o carro é o carro em foco → pit timer (4.6).
- Senão:
  - Caixa AlphaValue.
  - Quadrado amarelo de posição {X0+2FH, y0+FH, X0+4FH, y0+3FH} (36..72 × 709..745).
  - "<Nome> Is in the pits" (0xc) {CX, y0, X1−32, y0+FH}: dir, branco.

### 4.6 Pit timer do carro em foco (0x9c067ac; 3 linhas) – C
- Caixa AlphaValue (704..768), y0 = 709.
- "Pit stop" (0x3) {X0, y0+FH, X1, y0+2FH}: centro, amarelo.
- Nome completo {CX, y0, X1−4FH, y0+FH}: dir, branco.
- Tempo parado em décimos (0x9c2905f, da timing info +0x24) {CX, y0+FH, X1−4FH, y0+2FH}: dir, amarelo. Fica na mesma linha de "Pit stop", à direita.

### 4.7 Drive-through / largada queimada (0x9c06925; 4 linhas) – C
- Caixa alpha 100 {X0, H−4FH−10, X1, H}; T = 686.
- Oval com o número {X0+W/10, T+FH/2, +FH, +FH} (102..120 × 695..713). Ícone de pneu logo abaixo (T+FH/2+FH+5, altura FH), se type != −1.
- Nome curto {X0+W/7, T+FH/2, X1, +FH}: esq, **vAlign centro**, branco. Equipe/motor na linha +FH+5: esq, centro, ciano.
- Título {X0+W/6, T+FH/4, X1, +FH}: centro (centrado em (W/6+X1)/2, não em CX), branco.
  - "Jump Start" (0x25) se for corrida e PLAYER_CAR.vt0x18 < 1 (P: nenhuma volta completada).
  - Senão "Drive Through" (0x26).
- "P E N A L T Y" (0x28, sobrescrito por ClassicPenalty), em FONT_C2 (1.25×) e sempre com sombra (vt0x20): rect L = X0+W/6, T += 3FH/4, B = T+FH_C2; centro, branco.

### 4.8 Vencedor (0x9c02959; 4 linhas) – C (layout), H (sobreposição bandeira/texto)
- Caixa AlphaValue; marca this.0x3b.
- Bandeira classic_flag {CX−3FH, y0−2FH, CX+3FH, y0+3FH} (458..566 × 655..745), alpha 255. Ultrapassa a caixa por cima.
- Oval {X0+FH/2, y0, X0+1.5FH, y0+FH}; ícone de pneu embaixo (T+FH+5).
- Nome {X0+2FH, y0, CX, y0+FH}: esq, branco. Equipe {X0+2FH, y0+FH+5, CX, +FH}: ciano.
- Imagem WINNER {X1−9FH, T+FH/4, X1−2FH, y0+2FH}.
- Linha 3 (y0+2FH+5..y0+3FH+5), toda em amarelo:
  - tempo de corrida {X0+2FH .. CX}: esq;
  - velocidade média {X0+W/4 .. X1−W/4}: centro;
  - distância {CX .. X1−2FH}: dir.
- Fórmulas:
  - dist = 4.8768 · trackLen(vt0x40(0)&0xffff) · voltas(vt0xb8) / (vt0x78 ? 1000 : 1609);
  - média = dist · 3600 / (t_ms/1000);
  - formato "%.3f %s" (unidade km / mi, e km/h / mph).

### 4.9 Riding with (0x9c02f4f; topo direito) – C
- nw = textW(nome completo).
- Caixa AlphaValue {X1−nw−FH·(corrida?4:3), FH, X1−FH, 2FH}.
- Na corrida, quadrado de posição {L, FH, L+FH, 2FH}.
- Texto {L+3FH (corrida) .. X1−2FH, FH..2FH}: dir, **vAlign centro**, branco.

### 4.10 Cartão A (0x9c030b1: carNr, pneu, nome, equipe, linha1, linha2, pos; 4 linhas) – C
- Caixa AlphaValue.
- Oval {X0+FH/2, y0, X0+1.5FH, y0+FH}; pneu embaixo (+FH+5).
- Nome {X0+2FH, y0, CX, +FH}: esq, branco. Equipe em y0+FH+5: ciano.
- Quadrado de posição 2FH×2FH {X1−2·textW("0:00.000")−2FH, y0, X1−2·textW("0:00.000"), y0+2FH}.
- linha1 {CX, y0, X1−FH/2, +FH}: dir, amarelo. linha2 em y0+FH+5: dir, amarelo.

### 4.11 Cartão B (0x9c033ab: a8..a2c; 4 linhas) – C
- Caixa AlphaValue; marca this.0x3b.
- Se pos != −1: quadrado de posição {X0+W/4−FH, y0+FH, X0+W/4+FH, y0+3FH}, que substitui o bloco esquerdo.
- Senão, a8/ac/a10 nas linhas 0/1/2 de {X0..CX}: centro; branco/amarelo/amarelo.
- a14/a18/a1c nas linhas 0/1/2 da largura toda {X0..X1}: centro; amarelo/ciano/amarelo.
- a20/a24/a28 nas linhas 0/1/2 de {CX..X1}: centro; branco/amarelo/amarelo.

### 4.12 Split de quali ao vivo (0x9c03c32) – C
- Carro em foco nos boxes → nada.
- Fora de volta lançada → Cartão A com:
  - carNr, pneu, nome curto, equipe;
  - melhor tempo (ou "Warm-up lap" (0x6) se vt0xb4);
  - "+ gap" para a pole;
  - posição na quali.
- Em volta lançada, dentro da janela do split (mesma lógica de D2004: G_9c33d30, 0x9c34230/0x9c3422c) → Cartão B:
  - esquerda: nome da pole / tempo da pole no split / "";
  - centro: delta "+ x"/"- x" (ciano, linha 1) e rótulo do split ("Intermediate 1/2", "Finish Line");
  - direita: nome em foco / tempo no split, ou tempo correndo em décimos (0x9c2905f) / "";
  - posição.

### 4.13 Standings de quali (0x9c037ad) – C
- Marca this.0x26 = 1 e this.0x3b. rows = (CPP+1)/2 (CPP = 8 → 4 linhas × 2 colunas).
- Caixa alpha 100 {X0, H−rows·FH−10, X1, H}.
- Entradas de página·CPP a min(+CPP, 22). Entrada k: coluna c = k/rows, xo = c·W/2; ro = (k%rows)·FH.
- Quadrado {X0+xo+8, y0+ro+1, X0+xo+FH+5, y0+ro+FH−1} (15×16 px; o src de 64 px é encolhido), só se o carro tiver tempo.
- Rect de texto {X0+xo+2FH−5, y0+ro, X0+xo+W/2−FH, +FH}:
  - nome curto: esq, branco;
  - no mesmo rect, dir, amarelo: líder → melhor tempo; demais → "+ gap".

### 4.14 Standings de corrida (0x9c061b8) – C
- Mesma grade, caixa e quadrado de 4.13 (rows = (CPP+1)/2, alpha 100); this.0x26 = 1. Limite de entradas: G_9c31f70 = MaxCarsRace.
- Texto à direita (amarelo):
  - P1: "Lap %d" (0x13 + voltas do líder), só se o tempo do líder for válido (≠0 e sem o bit 0xf0000000);
  - demais, se Δtempo > 0: "+ N Lap(s)" quando Δvoltas > 0, senão o tempo do gap em m:ss.mmm (0x9c28ec9), **sem "+"**.
- Carro fora (vt0x54 == 0): sem quadrado; acrescenta "  Out" (0x19).
- Nome curto: esq, branco.

### 4.15 Pit stops (0x9c05d06) – C
- Grade/caixa de 4.14.
- Texto à direita: "%d Stop" / "%d Stops" (0x4 se n == 1, senão 0x5), com n = timing info +0x28. Carro fora: + "  Out".

### 4.16 Gap à frente (0x9c044c7; 4 linhas) – C
- Exige G_9c33d32 > 0 e estar na janela do split. Caixa alpha 100; marca this.0x3b.
- Se P > 1:
  - **Gradientes**, faixa y0+FH/2..y0+4FH/3, w = W/5:
    - [X0+4FH, +w/2]: preto→amarelo;
    - [+w/2, X0+4FH+w]: amarelo→branco;
    - [X1−4FH−w, X1−4FH−w/2]: branco→amarelo;
    - [X1−4FH−w/2, X1−4FH]: amarelo→preto.
  - Nome do carro à frente {X0+4FH, y0+1.5FH, CX, y0+2.5FH}: esq, branco. Quadrado {X0+FH, y0+FH/2, X0+3FH, y0+2.5FH} (pos−2 = P−1).
  - Linhas centrais, centro, amarelo: superior {X0, y0+FH/2, X1, y0+1.5FH}, inferior {X0, y0+1.5FH, X1, y0+2.5FH}.
    - Retardatário: "+ N Lap(s)" (inferior).
    - No split seguinte: "(± x)".
    - Senão: "%s %s" gap (superior) e "(± variação)" (inferior).
- Sempre:
  - rótulo do split {X0, y0+3FH, X1, y0+4FH}: centro, amarelo;
  - quadrado do carro em foco {X1−3FH, y0+FH/2, X1−FH, y0+2.5FH};
  - nome {CX, y0+1.5FH, X1−4FH, y0+2.5FH}: dir, branco.

### 4.17 Gap atrás (0x9c04e35) – C
- Espelho de 4.16: o carro em foco fica à esquerda (quadrado P em X0+FH..X0+3FH, nome esq em X0+4FH) e o carro de trás à direita (nome dir {CX..X1−4FH}, quadrado P+1 {X1−3FH..X1−FH}).
- Se o carro em foco é o último: o nome desce para y0+FH e nada mais é desenhado.
- Linha superior: "0.0", timer correndo em décimos ou "%s %s" gap. Linha inferior: "(± x)".
- Gradientes e rótulo do split iguais a 4.16.

### 4.18 Speed trap (0x9c05840; 4 linhas) – C (layout), H (moldura)
- Para cada um dos 3 traps, ativo enquanto trackpos ∈ [trapPos, trapPos + 2·SpeedTrapWidth] (ini 75). Caixa alpha 100; marca this.0x3b (0x9c26522).
- Coluna esquerda {X0..CX}, centro:
  - nome do mais rápido (y0): branco;
  - velocidade "%.1f %s" (raw>>16 · G_9c34a88 · 3.6) (y0+FH): amarelo.
- Rótulos centrais {X0..X1}: "Speed" (0x21) em y0+FH e "Current" (0x22) em y0+2FH, branco.
- Coluna direita {CX..X1}:
  - nome em foco (y0): branco;
  - velocidade atual (y0+FH..y0+2FH): amarelo;
  - segundo valor (offset 0x9c342b6, se > 0) (y0+2FH..y0+3FH): amarelo.
- PDX.vt0x40(rect centrado em CX+W/4, largura textW(" 000.0 "), −1): H, moldura em volta da velocidade atual.

### 4.19 Director's cut (0x9c06c15) – C (fluxo), P (semântica de G_9c33d30)
- s = G_9c33d30 (último split do carro em foco); s' = (s+1)%3.
- Janela ativa: tempo atual ≤ 0x9c34230[s] ou ≥ 0x9c3422c[s'] + 2·G_9c344dc.
- Decisão:
  - frente = janela ativa e P > 1;
  - atrás = janela ativa e P < nCarros (GPx.vt0xb0).
- **Sorteio:** na primeira vez em que frente/atrás fica verdadeiro, rand()%10 < 3 (30%) faz o episódio inteiro mostrar o **speed trap** em vez do gap (G_9c3363d). Os flags zeram quando a janela fecha.
- Se s == 2 e G_9c31021 (rearmado sempre que s != 2):
  - standings paginados a cada 6000 ms (this.0x10 += 1, P);
  - ao passar de MaxCarsRace, a página volta a 0 e G_9c31021 = 0 (para até o próximo s != 2);
  - voltas do líder (0x9c070a0) %5 == 0 → pit stops, senão standings de corrida.
- Senão:
  - frente e atrás → o menor gap vence (Δatrás < Δfrente → gap atrás, senão gap à frente);
  - só frente → gap à frente; só atrás → gap atrás;
  - nenhum → speed trap só se o sorteio estiver ativo (na prática, nada).

## 5. Tabelas de dados (C)
- **Strings** (GetString n, sobrescrevíveis em [String]):
  - 0x0 Intermediate 1, 0x1 Intermediate 2, 0x2 Finish Line;
  - 0x3 Pit stop, 0x4 Stop, 0x5 Stops, 0x6 Warm-up lap;
  - 0x7/0x8 (Is) out of the race, 0x9 Accident, 0xa Out of fuel;
  - 0xb/0xc (Is) in the pits;
  - 0x10/0x11 (Race) Winner, 0x12 Fastest Lap, 0x13 Lap, 0x14 Laps, 0x17 Laps remaining, 0x18 Final lap;
  - 0x19 Out, 0x1a Session over, 0x1b Race over, 0x1c Leader;
  - 0x21 Speed, 0x22 Current, 0x25 Jump Start, 0x26 Drive Through, 0x28 "P E N A L T Y" (ClassicPenalty).
- **Formatos:** "%d %s", "  %s", "%s %d", "+ %d %s", "%.1f %s", "%.3f %s", "%s %s". Tempos: 0x9c28ec9 = m:ss.mmm; 0x9c2905f = décimos; 0x9c28f97 = h:mm:ss.
- **Timing info** (GPxP_GetTimingInfo, 0x1e bytes por carro): +0x24 = tempo de boxes; +0x28 = nº de paradas (byte).
- **Atlas:** posição → (idx%8·64, idx/8·64); número do carro → blue.tga em células de 64 px.

## 6. Opções de ini que mudam o comportamento
- [Misc]:
  - Mode = 0 seleciona o Classic;
  - AlphaValue: opacidade das caixas "normais"; as caixas de standings, gaps, speed trap e drive-through usam 100 fixo;
  - SpeedTrapWidth (largura da janela do trap; ini 75);
  - MaxCarsRace (1..22): limite da paginação/standings;
  - ShowMode, ShowInCockpit, DirectorsCut, SpeedTrapLogging, AlwaysShowTowerStandings (este sem uso visto no Classic).
- [Classic]:
  - Scaling (4:3 centrado ou largura total);
  - CarsPerPage (padrão 8; grade com (CPP+1)/2 linhas);
  - FontShadow;
  - Yellow / Flag (texturas);
  - Type1 / Type2 (ícones de pneu);
  - RaceTowerLeftCars / QualTowerLeftCars (lidos, H: sem torre no Classic).
- [Font] FontClassic; fonts.ini [manana]. [Tyres] Team #NN / SatTypeNN. [Sky] Replace*. [String] n / ClassicPenalty.

## 7. Lacunas e hipóteses
- O valor real de FH (≈18 px) e a cor/opacidade exatas de PDX.vt0x38 (supostamente preto com alpha AlphaValue ou 100) não foram medidos (H).
- PDX.vt0x40(rect, −1) no speed trap: moldura ou realce (H). A ordem dos cantos do gradiente é P.
- Mapeamento flag → sky message (35124..35129): P (por analogia com D2004). O riding-with ignorar o enable do hook é H.
- O incremento da página no director's cut aparece como "0+1" no sim; a leitura é this.0x10+1 (P).
- Strings 0x9c31170..0x9c311c4 (prováveis "+"/"-"/"%s" dos gaps) não foram lidas individualmente: os sinais "±" vêm de "+"/"-" já confirmados em 0x9c31140/44 e 0x9c31150/54 (P).
- O efeito de this.0x3b e this.0x26 fora destas funções não foi seguido (H: ocupado / standings ativos).
- Não há torre (tower standings) no Classic, salvo uso via base não rastreado (H).
- Sem validação visual: os layouts são do código, não de captura.
