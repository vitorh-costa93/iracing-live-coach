# GPxSAT – modo "Digital 2010": especificação de layout (engenharia reversa estática)

Fonte: GPxSAT descompactado (`sat_rel.bin` = VA 0x9c01000), desmontado em `sat5.asm` e decompilado
linearmente com `sim.py` através de `d10/run10.py`. As saídas ficam em `d10/o_<VA>.txt`, com texturas,
cores, globais de layout, rects e strings simbolizados.
Confiança: **C** = certo (lido no asm), **P** = provável (fluxo + strings), **H** = hipótese.
Nota sobre o sim.py: ele omite o ajuste de sinal `cdq/sub eax,edx` antes de `sar 1`, e por isso imprime
`(x-?)>>1` ou `(x-C_DARK)>>1`. Leia sempre como **x/2** (conferido no asm em 0x9c1e36c, 0x9c1e4f2,
0x9c1efd4 e 0x9c1fd47). Também aparece `0*n` ou `0+1` no lugar de `this.0x10*n` / `this.0x20+1`
(composição de stores).

Referência: 1024x768. Os valores em px são **H** e assumem RH = 25. W = largura útil, CX = centro.

---
## 1. API de desenho e escala

| Item | Detalhe | Conf. | Evidência |
|---|---|---|---|
| Modo | `[0x9c3510c]` = 3. Vtable 0x9c2f5bc (a D2004 fica em 0x9c2f4f0; mesmo slot = +0xcc). Construtor 0x9c27260 | C | 0x9c27260 |
| Textura | `PDX.vt+0x2c(&rect{L,T,R,B}, tex, src, alpha=0xff, 0, 0)`. Diferente do D2004, aqui **src é um rect em px da textura** (a TGA é um atlas; a caixa usa só a parte {0,0,w,32}) | C | todas as chamadas DRAW |
| Rects src | R133 {0,0,133,32}, R176, R186, R222, R287, R320, R663 (todos com altura 32); R45, R102; R191x114 {0,0,191,114}; R209x26 {0,0,209,26} | C | 0x9c31770…0x9c31810 |
| Texto | `FONT.vt+0x18(&rect, hAlign 0/1/2, vAlign 1, ARGB, str)` | C | 0x9c1bd96 |
| Recuo do texto | TextPad 0x9c1bdca: SZ19 à esquerda/direita, SZ19/2 se centralizado | C | 0x9c1bdca |
| Fontes | FONT = `[Font] FontDigital2010` (instalado: din-bold, DIN 14 bold 700) em escala 1.0. FONT_D10BIG (G_336d0) = din-bold ×1.6667, usada só no número das caixas grandes de posição | C | 0x9c1b331, gpxsat.ini, fonts.ini |
| S(n) | `(screenH·n + 500)/1000` (per-mil da altura) | C | 0x9c1ad60 |
| Wid(x) | `x·6/5` (correção de aspecto ×1.2) | C | 0x9c1ad9a |
| RectW(src,h) | `Wid((srcW·h + srcH/2)/srcH)`, com h=0 → RH; 0x9c1ae00 = idem sem ×1.2; TexW(tex,h) = Wid(tex.vt0x10(h)) | C | 0x9c1ade2, 0x9c1ae00, 0x9c1adad |

**Globais de layout (0x9c1aaaa, slot vt0x68)**: valores C; os px entre parênteses são H.

| Nome | Fórmula | px |
|---|---|---|
| RH (33848) | max(FH, S(32)) | 25 |
| W222 (33844) | RectW(R222, RH) | 207 |
| TOP / BOT | SCR_T + 1.5RH / SCR_B − 1.5RH | 37 / 731 |
| LEFT / RIGHT | SCR_L + W/18 / SCR_R − W/18 | 57 / 967 |
| BOT_RH43 | BOT − 4RH/3 | 698 |
| F1L | LEFT + TexW(timing_background_1, RH)/2 | — |
| F1R | F1L + F1.vt0x10(2RH) (0 sem textura F1) | — |
| F1R_dx2 | F1R + (W/18)/2 | — |
| X1_W10 | X1 − W/10 | 922 |
| SZ19 | Wid(max(round(RH·19/32), S(18))) | 18 |
| SZ13 | Wid(max(round(RH·13/32), S(12))) | 12 |
| NEG2SZ19_3 | −2·SZ19/3 | −12 |
| RH10, S50, W00 | RH/10; Wid(S(50)); largura de "00" em FONT | 2, 45, — |

**Cores padrão (ARGB, sobrescritas por ini)**: C_LIGHT 0xfffefefe, C_DARK 0xff010101, C_PIT 0xfffe0000,
C_PITEXIT 0xff339900, C_FASTER 0xff339900, C_RETIRED (G_336cc, padrão documentado 0xa0fefefe). Na torre
inferior, "Out" usa `(C_LIGHT & 0xffffff) | 0xa0000000` fixo (0x9c1e64e). Tudo C.

**Texturas** (`gfx\digital2010\%s.tga`, 0x9c1b384). Atlas de 256x32 salvo onde indicado:
F1 256x128, LG 128x32, data_neutral, data_neutral_medium, data_caption, data_caption_big 512x32,
data_caption_medium, data_faster_medium, data_fastest, data_winner, race_control 1024x32, data_title,
big_position_first/neutral 256x128, labeled_data_neutral, gap_first/slower/faster, race_gap,
bottomscreenfirst/pos, positions_first/neutral 512x32, positiongain/lost, timing_background_1/2/3 32x32,
`timer\` lap_timer, lap_timer_yellow, timer_bg, timer_yellow, timer_green, timer_finished, pitstop_timer,
pitlane_timer, e `tower\` bg_newlap, bg_first, finished 64x32, InPit 64x32, posnegative/posneutral/pospositive
128x32, race_bg, race_bg_first. Ponteiros em 0x9c336d4…0x9c33788 (tabela em `d10/run10.py`). G_336fc
(classificação, código morto) não está na lista de carga (H). C

---
## 2. Animação

| Item | Detalhe | Conf. |
|---|---|---|
| Animador | ANIM (this+0x39d, vtable 0x9c2f688) é um stub: FADE% devolve 100, ROTtri devolve 0, FADEOUTDONE/SETTLED/FADEDONE devolvem 1. **Sem fade, rotação nem slide**: as telas aparecem e somem num único frame | C |
| Troca de key | 0x9c1bc39(key, idx) → ANIM_UPDATE(1,0,key,idx). Quando a key muda, grava now (vt0x164) em this.0x3a1 | C |
| Logo LG | 0x9c1c665: visível por **8000 ms** após this.0x3a1. Rect: B = B do rect passado, T = B − 4RH/3, L = R + S(5), largura LG.vt0x10(4RH/3) | C |
| Piscar da volta mais rápida | data_faster_medium com alpha a·255. dt = now − instante da volta; desenha só com 400 ≤ dt < 4000 ms. Com t = dt−400: a = (1−cos(2π·(t mod 1200)/1200))/2; nas fases 1..4 (t/600), a = min(1, 0.3+0.7a). Sobe em 600 ms e chega a 0 em t = 3600 | C (fórmula), P (leitura) |
| Destaque de volta na quali | C_FASTER no tempo por 7000 ms (0x1b58) após a volta, se a última volta = melhor volta | C |
| Banner da última volta | até 18000 ms após a passagem do líder (TimingInfo[0].vt0x20) | C |
| Timer verde | timer_green nos primeiros 5000 ms da sessão | C |
| Ciclo do Director's Cut | página da torre inferior a cada 10000 ms (0x2710) | C |

---
## 3. Despacho, teclas e prioridades

**Teclas (0x9c27648, modo 3)**: todas C.
- ToggleSpeedSplit: na corrida, `G_3511c = (G_3511c+1) % 3` (0 = gap à frente, 1 = gap atrás, 2 = nada); fora da corrida, mod 1 (sempre 0). Desliga o DC.
- Standings: na corrida, `G_35110 % 4` (0 off, 1 gap ao líder, 2 pit stops, 3 intervalo). Fora da corrida, `G_35118 % 3` (0 off, 1 gap à pole, 2 tempos absolutos), só se 0x9c26059 (P: sessão de quali/treino com torre).
- ScrollNext: com standings ligado, página this.0x10 (volta a 0 quando CarsPerPage·(p+1) ≥ MaxCars). Sem standings, na corrida e com G_35114, avança a página da torre this.0x20. O teste de volta é `page·page ≥ MaxCars` (o binário faz imul de 0x20 por ele mesmo; **H: bug**, o esperado seria CarsPerBottomPage·page).
- TowerStandings (F): só na corrida e só se standings = 0 ou AlwaysShowTowerStandings. `G_35114 = (G_35114+1) & 3` (0 off, 1 gap ao líder, 2 stops, 3 intervalo). Desliga o DC.
- TowerLeft (E): só na corrida, alterna G_35120 (torre esquerda "race progression"). Desliga o DC.
- DirectorsCut alterna G_34a99. ToggleOnOff = G_35108 % 3 (ShowMode). TimeLeft alterna OPT_TIMELEFT.
- Valores iniciais estáticos de G_35110/14/18/1c/20, G_34a99 e G_35108 = 0. O construtor põe G_35118 = 1 e OPT_TIMELEFT = 1; o ini pode sobrescrever.

**Corrida (vt0x90 = 0x9c217bd)**: C
1. Lap counter (0x9c1c915) se OPT_TIMELEFT.
2. Se ShowMode == 0, vale a **primeira** que casar:
   1. DT pendente (G_35129) → 0x9c1d194, e limpa a flag;
   2. carro em foco nos boxes (vt0xb8 em PLAYER_CAR) → pit timer 0x9c1f7ca;
   3. DC → 0x9c200e7;
   4. standings ≠ 0 → 0x9c1d8c7;
   5. split 0/1 → gap 0x9c1fb5f.
3. Depois, se o DC está desligado: torre inferior 0x9c1e26e (se standings = 0 ou AlwaysShowTowerStandings) e torre esquerda 0x9c1ed30 (se G_35120).

**Fora da corrida (0x9c218b2)**: time left (0x9c1c723). Com ShowMode == 0: DT → 0x9c1d194; senão,
torre de quali 0x9c20607 se G_35118 && 0x9c26059, e split 0x9c21655 se G_3511c == 0. C

**Com sky message ativa (vt0x88 = 0x9c216c5)**: C
- desenha a mensagem (0x9c25e6d) e depois time left ou lap counter;
- se ShowMode == 0, 0x9c26010 e corrida:
  - DC ligado → 0x9c200e7;
  - senão → torre inferior (mesma condição), torre esquerda (se G_35120) e gap, este último omitido quando "fora da corrida" ou "nos boxes" está ativo (G_35125/26).

Slots de sky message: 0x98 riding with, 0x9c nos boxes, 0xa0 volta mais rápida, 0xa4 fora da corrida, 0xa8 vencedor. C

**Logo F1 (vt0x8c = 0x9c2193a, após a base 0x9c260bb)**: rect {F1L, BOT_RH43−2RH, F1R, BOT_RH43},
textura F1 inteira, alpha 0xff. Aparece se ShowF1Logo == 1, ou se == 2 e `g_pGPx.vt0x1c() & 0x80`
(P: câmera externa/TV). C (H: o significado do bit)

---
## 4. Opções de ini

| Seção/chave | Alvo | Padrão | Efeito | Conf. |
|---|---|---|---|---|
| [Digital2010] ShowTimeLeft | OPT_TIMELEFT | 1 | relógio/contador de voltas | C |
| LapsLeftMode | this.0x27 | 1 | 0 = voltas restantes, 1 = volta atual | C |
| ShowF1Logo | this.0x3a5 | 2 | 0 off, 1 sempre, 2 só com o bit 0x80 | C |
| CarsPerPage | this.0xc | 8 (o ini instalado também tem 8) | standings; limitado a 1..MaxCarsRace; **5 vira 8** | C (0x9c1af13) |
| CarsPerBottomPage | this.0x14 | 5 | número de células da torre inferior | C |
| RaceTowerLeftCars | this.0x1c | 10 | linhas da torre esquerda (1..MaxCarsRace) | C |
| QualTowerLeftCars | this.0x18 | 22 | linhas da torre de quali (1..22) | C |
| ShowLapInStandings | this.0x3a9 | 1 | "Lap N" na linha do P1 do standings | C |
| ShowPitIconWhenInPitLane | this.0x3aa | 1 | ícone InPit na torre de quali | C |
| ShowRacePosTower / ShowQualPosTower | G_35114 / G_35118 | 0 / 1 | modo inicial das torres | C |
| ShowDriver, ShowDriverForTiming/FastestLap/Winner | G_31838, G_33890/91/92 | 1 | foto do piloto em riding with / split / FL / vencedor | C |
| ColorTextLight/Dark/Pit/PitExit/Faster/Retired | cores §1 | — | — | C |
| Scaling | this.0x33 | — | loader comum 0x9c23328 | C (efeito H) |
| [Misc] AlwaysShowTowerStandings | this.0x25 | 0 | torre inferior junto com o standings | C |
| [Misc] MaxCarsRace, ShowMode, DirectorsCut | G_31f70, G_35108, G_34a99 | — | — | C |
| FlagsDir / DriversDir (flags.ini/drivers.ini: Ext, Width, Height, AdjustWidth) | 0x9c3378c / 0x9c337e4 + idx·4 | — | bandeira e foto | C |

---
## 5. Helpers de caixa (comuns às telas)

- **BoxRect(row, side)** 0x9c1bc8d: B = BOT_RH43 − row·RH, T = B − RH.
  - Lado esquerdo: L = F1R_dx2 + SZ19·row, R = L + W222.
  - Lado direito: R = X1_W10 − SZ19·(2−row) − W/20, L = R − W222.
  - Resultado: uma **escada** inclinada, em que cada linha acima se desloca SZ19 para a direita.
  - C
- **ShiftDown** 0x9c1bd43: T,B += RH; L,R −= SZ19 (desce um degrau). C
- **NameRow(rect, idx, align, flag)** 0x9c1c2fe: faz ShiftDown e desenha data_neutral R222. Com flag e align = 0, a bandeira vai na ponta direita (largura RectW(srcFlag, RH)) e o texto termina em flagL + SZ19. O nome vem de `GPxP_GetShortDriverName(idx+1,1)`, em C_LIGHT, com TextPad(align). C
- **TeamRow** 0x9c1c494: faz ShiftDown e desenha data_caption R222 com o nome da equipe (0x9c34fdc + (idx>>1)·0x1b), à esquerda, C_LIGHT. C
- **DriverPic** 0x9c1c1de: foto acima do rect, largura = largura do rect, centralizada, base = T do rect. C
- **BigPos(rect, pos, rightOf)** 0x9c1c517: caixa de posição grande.
  - Tamanho: h = 2RH, w = RectW(R191x114, 2RH)·105/117, T = B − 2RH.
  - rightOf: L = R − 2SZ19/3. Caso contrário: R = L + 2SZ19/3 + SZ19, L = R − w.
  - Textura: big_position_first se pos == 1, senão big_position_neutral (src R191x114).
  - Texto: "%d" em FONT_D10BIG, centralizado, C_LIGHT.
  - C
- **LabeledPos** 0x9c1c0f1: gap_first (pos 1) ou labeled_data_neutral, R222. "%d" centralizado em [L, L+S50]; nome curto à esquerda em [L+S50, R−SZ13]. C
- **RaceControlMsg(l1, l2)** 0x9c1bf4a:
  - caixa race_control com largura RectW(R663), centralizada em CX, linha de baixo B = BOT_RH43;
  - l2 (se houver) na linha de baixo e l1 uma RH acima, ambas à esquerda com recuo, C_LIGHT;
  - acima delas, a aba data_title (W222, mesmo L) com "Race control" em C_DARK.
  - C
- Variantes de texto: 0x9c1be4c (R −= 2SZ19/3, à direita, light); 0x9c1bea3 (R −= 2SZ19/3 + W00); 0x9c1bf00 (R −= W00, TextPad à direita). C

---
## 6. Telas

### 6.1 Time left, fora da corrida (0x9c1c723) – C
- **Caixa**: largura RectW(R133, RH), centralizada em CX, T = TOP, altura RH.
- **Textura**: timer_finished se elapsed > limite (vt0xac); timer_green se elapsed < 5000 ms; timer_yellow se TimingInfo+0xa (P: bandeira amarela); senão timer_bg.
- **Texto 1**: nome curto da sessão ("P1/P2/Q1/PR…", 0x9c23f55), centralizado em [L, L+S50], C_LIGHT.
- **Texto 2**: tempo restante "h:mm:ss"/"m:ss", centralizado em [L+S50−SZ19/2, CX+w/2]; C_DARK no amarelo, senão C_LIGHT.

### 6.2 Lap counter, corrida (0x9c1c915) – C
- **Dados**: cur = 0x9c070a0, total = vt0xb8, restantes = cur==0 ? total : total−cur+1.
- **Caixa**: lap_timer (ou lap_timer_yellow), largura 186/32·RH (sem ×1.2), centralizada, T = TOP.
- **Textos**:
  - "Lap" à esquerda;
  - "/" centralizado em [L+w/3, R];
  - número 1 (LapsLeftMode 0 → restantes, 1 → min(cur,total)) centralizado em [L+w/3+SZ19/2, R−w/3];
  - número 2 (total) centralizado em [R−w/3, R−SZ19/2];
  - cor: C_DARK no amarelo, senão C_LIGHT.
- **Banner**: aparece com 1 ≤ restantes ≤ 5, cur ≥ 1 e até 18 s após a passagem do líder. T = TOP+RH+RH/10, data_caption_big com largura 287/32·RH, centralizado. Texto "Final lap" ou "%d Laps remaining", centralizado, light.

### 6.3 Volta mais rápida (0x9c1ccab; key 2) – C
- **Linha 1 (esquerda)**: NameRow sem bandeira, com foto se ShowDriverForFastestLap.
- **Linha 0**: TeamRow.
- **Coluna 2**: L = R_nome − 2SZ19/3, largura RectW(R176).
  - Linha 1: data_caption_medium com "Fastest Lap" centralizado.
  - Linha 0: data_neutral_medium com o tempo da volta (vt0x118[idx], formato 0x9c28ec9) centralizado.
  - Por cima, o piscar de data_faster_medium (§2).
- **LG** à direita.

### 6.4 Vencedor (0x9c1d2de; key 3) – C (layout), P (unidades)
- **Coluna esquerda** (BoxRect linha 2):
  - linha 2: data_winner R222 com "Winner" à esquerda, light;
  - linha 1: foto (se ShowDriverForWinner) e NameRow com bandeira;
  - linha 0: TeamRow.
- **Coluna 2**: L = R − 2SZ19/3, largura RectW(R176), três data_neutral_medium em escada (ShiftDown). Textos centralizados, light:
  1. tempo total de corrida (vt0x11c[idx]);
  2. distância "%.3f %s" = 4.8768·comprimento(vt0x40)·voltas / (1000 ou 1609 conforme vt0x78), com unidade 0x9c34a68;
  3. velocidade média "%.3f %s" = dist·3600/(tempo/1000), com unidade 0x9c34a74.
- **LG** no fim.
- P: 4.8768 m é a unidade de pista do GP3; as unidades são km/mi e km/h/mph.

### 6.5 Fora da corrida (0x9c1cfef) / Drive-through (0x9c1d194) – C
- **Fora da corrida**: RaceControlMsg("Car %d (%s) Is out of the race: %s"). O motivo é a string GPx 0x609+código (se car.vt0x60); senão "Out of fuel" (vt0x98 == 0); senão "Accident".
- **DT na corrida**: l1 = "Car %d (%s) got a drive-through penalty"; l2 = "for jump starting" se car.vt0x18 < 1, senão "for cutting".
- **Fora da corrida (DT)**: "Car %d (%s) Time Disallowed - Track Limits".

### 6.6 Riding with (0x9c1d680; key 0xc) – C
- Se o carro da câmera está nos boxes e !vt0x68 → PitInfo(car, 1).
- Senão:
  - linha 1: NameRow com bandeira e foto (ShowDriver);
  - linha 0: TeamRow;
  - BigPos à direita (posição de corrida vt0x90, ou posição de quali);
  - LG.

### 6.7 Nos boxes (0x9c1d817; key 5), pit timer (0x9c1f7ca; key 8) e PitInfo (0x9c1f818) – C/P
- **Nos boxes**: carro = vt0xd4.
  - Se é o carro da câmera → pit timer = PitInfo(cam, 1).
  - Senão → PitInfo(car, 0), e também o pit timer se a câmera estiver nos boxes.
- **PitInfo**: tempos em TimingInfo[idx·0x1e+0x20] (pit lane) e +0x24 (parada).
  - Linha inicial = 3 − v10 (v10 = flag de car.vt4/vt8, P: já parou), no lado esquerdo se ac.
  - NameRow.
  - Caixa pitlane_timer (data_caption em replay ou com ac = 0) com "Pit Lane"; o tempo fica centralizado a partir de L+W222/2 (formato 0x9c2905f, ou 0x9c29178 se !car.vt4).
  - Se ac && !v10: caixa pitstop_timer com "Pit Stop" e o tempo de parada em C_DARK.
  - BigPos (rightOf = ac) e LG se ac.

### 6.8 Standings de corrida (0x9c1d8c7; key 0xa) – C
**Paginação**
- Página p = this.0x10; carros [p·N, min(p·N+N, MaxCars)), com N = CarsPerPage.
- Duas colunas com rows = (N+1)/2 linhas.

**Layout**
- Topo T0 = BOT_RH43 − rows·RH, cada linha com altura RH.
- Coluna 0 = [CX − W320, CX] e coluna 1 = [CX, CX + W320], com W320 = RectW(R320, RH).
- Textura positions_first (P1) ou positions_neutral (src R320).

**Textos de cada linha**
- Posição "%d" centralizada em [L, L+16·S50/15], light.
- Código de 3 letras (0x9c1c1c7) à esquerda em [L+16·S50/15, L+2·W320/3], cor C_LIGHT, ou C_RETIRED se fora.
- Info centralizada em [L+2·W320/3, R]. A cor é a especial (pit) quando houver, senão a da linha.

**Info, na primeira regra que casar**
1. Fora (vt0x54 falso) → "Out" (C_RETIRED, caixa neutral).
2. Modo 2 → "%d Stop"/"%d Stops" (TimingInfo+0x1d).
3. Carro na pit lane (vt0xb8) → "Pit" (C_PIT) se vt0x4, senão "Pit exit" (C_PITEXIT).
4. Posição mudou desde a última linha de cronometragem (pos ≠ 1 e pos ≠ +0x13; P: posição anterior) → texto vazio.
5. P1 → "Lap %d" (+0xb) se ShowLapInStandings, senão vazio.
6. Modo 1 → "+%d Lap(s)" (+0xe) ou o gap ao líder no último split (P1 usa o split +0xc da volta, a coluna +0xc quando +0xb/+0xd casam com a volta do líder), em formato 0x9c28e51.
7. Modo 3 → intervalo ao carro da frente: "+%d Lap(s)" se tem menos voltas e cruzou depois, senão a diferença no último split comum (tabela 0x9c33d40/41/59/75, passo 0x39).

**Extras**
- LG com L = CX − W320 e B = T0 − S(5).
- Sem cabeçalho/título.

### 6.9 Torre inferior (0x9c1e26e) – C
- **Condição**: só se G_35114 ≠ 0. Página this.0x20, com N = CarsPerBottomPage células.
- **Geometria**:
  - largura da célula w = ((RIGHT−LEFT) − (TexW(bg1)+TexW(bg3))/2 − (N−1)·S(8)) / N, com espaço S(8);
  - faixa de fundo: T = BOT − RH − 2·S(4), B = BOT;
  - fundo = bg1 em [LEFT, LEFT+TexW(bg1)], bg2 até LEFT + n·w + (n−1)·S(8), bg3 com largura TexW(bg3), tudo com src 0 (textura inteira);
  - células começam em L = LEFT + TexW(bg1)/2, com T = BOT−RH−S(4), B = BOT−S(4) (altura RH).
- **Textura da célula**:
  - bottomscreenfirst (P1) ou bottomscreenpos (src R209x26);
  - positiongain/positionlost quando a posição mudou (pos < / > +0x13), e aí sem texto de info;
  - fora → data_neutral R222, texto 50% transparente (alpha 0xa0).
- **Textos** (todos centralizados):
  - posição em [L, L+46w/209] (não aparece se "Out");
  - nome mini (GPxP_GetTinyDriverName) em [L+46w/209, L+w/2] (ou [L, L+w/2] se fora);
  - info em [L+w/2, R].
- **Info**:
  - igual ao standings, mas o P1 mostra "Leader" (modo 1) ou "Interval" (modo 3);
  - modo 2 = stops; Pit/Pit exit.

### 6.10 Torre esquerda "race progression" (0x9c1ed30; key 0x13) – C
- **Condição**: exige as 5 texturas tower\race_bg*, pos*.
- **Linhas**: RaceTowerLeftCars linhas a partir de T = TOP, altura RH, L = LEFT, largura W133 = 133/32·RH (sem ×1.2). Fundo race_bg_first (P1) ou race_bg (R133).
- **Textos**:
  - "%d" centralizado em [L, L+5·S50/6];
  - nome mini à esquerda em [L+5·S50/6+SZ13, L+W133−SZ13], light.
- **Caixa de ganho**:
  - L = L+W133−2SZ19/3, largura 102/32·RH (R102);
  - textura posnegative / pospositive / posneutral conforme d = largada(+0x14) − pos (< 0, > 0, = 0);
  - texto |d| centralizado em [L+S50/2, R], C_DARK se d < 0, senão light.

### 6.11 Gap à frente/atrás (0x9c1fb5f) – C (layout), P (semântica dos campos)
**Quais carros**
- Se split == 0 e posição do foco (G_33d2c) > 1, ou se foco = último: par (foco−1, foco).
- Senão: par (foco, foco+1).

**Quando aparece**
- Só se o carro de trás já tem volta (+0xb > 0) e a posição dele na pista (vt0x38+0x1a & 0xfff) está na janela do split atual (≤ fim, tabela 0x9c34230) ou do próximo (≥ início, 0x9c3422c).
- Fora da janela, não desenha nada.

**Layout** (alinhado à direita em X1_W10, três linhas)
1. LabeledPos do carro da frente em {X1_W10−W222, BOT_RH43−3RH, X1_W10, BOT_RH43−2RH}.
2. Uma RH abaixo:
   - caixa race_gap (R133) em [X1_W10−W222, X1_W10 − W222/2 + 2SZ19/9];
   - segunda race_gap opcional em [R1 − 4SZ19/9, X1_W10];
   - os textos ficam à direita, em light.
3. LabeledPos do carro de trás, com B = BOT_RH43.

**Conteúdo**
- Volta(s) de diferença → "+%d Lap(s)".
- O de trás ainda não chegou ao split que o da frente já passou:
  - se estão no mesmo setor → "(gap do último split)";
  - senão → gap ao vivo "+%s" = now − pausa − split do da frente (formato 0x9c28ded), mais a 2ª caixa "(gap anterior)".
- O de trás acabou de cruzar → gap nesse split, e a 2ª caixa "(gap do split anterior)" se > 0.
- As texturas gap_faster/slower **não** são usadas aqui.

### 6.12 Director's Cut (0x9c200e7) – C/P
**Torre inferior**
- Ao entrar, ou a cada 10 s, avança this.0x20 (volta a 0 quando CarsPerBottomPage·p ≥ MaxCars) e escolhe o modo:
  - G_35114 = 2 (stops) se algum carro já parou e a volta é múltipla de 5;
  - senão G_35114 = 3 em volta ímpar, 1 em volta par.
- Depois desenha a torre inferior.

**Torre esquerda**
- G_35120 = 1 só quando o líder está no setor/estado +0xd == 1 e volta == 3 ou volta == total−3 (P: 2ª volta concluída e 3 para o fim).

**Gap**
- O foco precisa estar na janela de um split.
- "Frente" é possível se pos > 1, e "trás" se pos < vt0xb0.
- Na primeira vez há sorteio rand%10 < 3, que cancela naquele frame (30% por frame até travar G_33898).
- Mostra "trás" (G_3511c = 1) se só trás é possível ou se gap_trás < gap_frente; senão "frente" (0). Sem nenhum dos dois, G_3511c = 2.

### 6.13 Torre de quali (0x9c20607) – C
- **Linhas**: até QualTowerLeftCars linhas a partir de TOP, altura RH, L = LEFT, largura W222. Para no primeiro carro sem tempo.
- **Fundo**: tower\bg_first (P1) ou tower\bg_newlap (R222).
- **Textos**:
  - "%d" centralizado em [L, L+S50];
  - nome mini à esquerda em [L+S50+SZ13, +⅓ do restante];
  - tempo centralizado no resto até R−SZ13: P1 ou modo 2 → tempo absoluto; senão "+gap à pole";
  - cor C_FASTER por 7 s se a última volta = melhor, senão light.
- **Ícone** (src R45, L = R − 2SZ19/3):
  - tower\finished se o carro esgotou as voltas: voltas + (em volta) ≥ vt0xec·3 quando 0x9c219f0, senão ≥ vt0xb8. H: 0x9c219f0 = quali com limite de voltas.
  - senão tower\InPit se ShowPitIconWhenInPitLane e o carro está na pit lane.

### 6.14 Split fora da corrida (0x9c21655 → 0x9c20b16 / 0x9c20eae; key 0x11, 4 com amarelo) – C/P
**Escolha**
- Carro da câmera nos boxes, ou sem volta em curso (vt0x20 ≤ 0) → 0x9c20b16; senão → 0x9c20eae.

**0x9c20b16 (parado / sem volta)**
- Treino (não 0x9c26059):
  - sem melhor volta (0x9c344f0 + idx·64): Name (flag), foto, Team;
  - com melhor volta: Name centralizado e, logo abaixo, data_neutral_medium com a melhor volta.
- Quali:
  - Name, foto, Team e BigPos à direita (posição de quali 0x9c1d786);
  - coluna (L = R + SZ19 − 2SZ19/3, R176): melhor volta e, abaixo, "+gap" à pole (ou ao P2 se ele for a pole).

**0x9c20eae (volta em curso)**
- Name centralizado com bandeira e foto; abaixo, data_neutral com o cronômetro.
- s = setor atual (vt0x1c):
  - fora das janelas → cronômetro "now − início" (0x9c2905f), à direita com −W00;
  - perto do próximo split → cronômetro e, abaixo, gap_first com "1" + split da pole;
  - acabou de cruzar o split s → tempo do split (light); abaixo, a comparação com a pole (ou, se o foco é a pole e s = 2, com min(melhor própria, P2)).
- Caixa da comparação:
  - gap_faster, ou data_fastest se pole na chegada, quando delta < 0; texto light;
  - gap_slower e texto **C_DARK** quando delta > 0;
  - "1" na esquerda, exceto quando é data_fastest.
- No fim: BigPos (linha 0, rightOf) se quali e última = melhor volta, e LG.

### 6.15 Classificação final (0x9c1f02b) – C (código morto)
- **Referências**: nenhuma (nem call nem vtable).
- **Conteúdo**:
  - cabeçalho "2013 FORMULA 1 UBS CHINESE GRAND PRIX" (string de teste) em G_336fc R663;
  - race_control com "Race Classification after %d laps";
  - 12 linhas bottomscreen R209x26 + código + data_caption R222 (equipe) + data_neutral_medium (tempo/gap);
  - T0 = H/7.
- Não replicar como tela real.

---
## 7. Lacunas e hipóteses
- FH real do din-bold 14 não foi medido, e RH = max(FH, S(32)) depende dele. Os px são H com RH = 25.
- Significado exato dos campos de TimingInfo (+0xb volta, +0xc setor, +0xd, +0xe voltas atrás, +0x13 posição anterior, +0x14 grid, +0x1d stops): P, pelo uso.
- Bit 0x80 de g_pGPx.vt0x1c (ShowF1Logo = 2), 0x9c26059 (sessão com torre) e 0x9c219f0 (limite de voltas da quali): H/P.
- ScrollNext da torre testa `page²` (H: bug do binário). Replicar com CarsPerBottomPage·page.
- Scaling (this.0x33): efeito não rastreado. A base 0x9c260bb (track map, microtime, speed trap, sky messages) é comum aos modos e não foi detalhada aqui; o doc diz que o D2010 não tem speed traps.
- Formatos de tempo 0x9c28ec9 / 0x9c28e51 / 0x9c28ded / 0x9c2905f / 0x9c29178 não foram decompilados (P: absoluto m:ss.mmm, gap +s.mmm, gap ao vivo com décimos, cronômetro, cronômetro parado).
- G_336fc e o cabeçalho da classificação só existem no código morto.
