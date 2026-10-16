# GPxSAT – itens compartilhados entre os modos: especificação (engenharia reversa estática)

Fonte: GPxSAT descompactado (`art\sat_rel.bin`, VA 0x9c01000; `art\sat5.asm`), decompilado com `art\sim.py`.
As saídas ficam em `shared\o_<VA>.txt`. String em VA = 0x9c00000 + offset de `art\sat_strings.txt`.
Confiança: **C** = certo (lido no asm), **P** = provável, **H** = hipótese.
Modos (`[0x9c3510c]`): 3 = Digital 2010 e 2 = Digital 2004 (C); 0/1 = Classic/Digital (P).

---
## 1. Laço do quadro (0x9c26121)

| Passo | Detalhe | Conf. | Evidência |
|---|---|---|---|
| 1 | só desenha a tela do modo (`this.vt0x8c`) quando 0x9c25f59 (ShowMode global) permite | C | o_9c26121.txt |
| 2 | se `G_9c35122` (InfoScreen ligado) → tela de info 0x9c255cc | C | idem |
| 3 | track map 0x9c2aa48 | C | idem |
| 4 | 0x9c263b4: duas janelas de mensagem (this+4, this+8), canto inferior esquerdo | P | o_9c263b4.txt |
| 5 | microtime 0x9c280ff | C | idem |
| Ordem | os passos 2–5 rodam em **todos** os modos e são independentes do ShowMode | C | — |

## 2. ShowMode global (mensagens)

| Item | Detalhe | Conf. | Evidência |
|---|---|---|---|
| Variável | `G_9c35108`; a tecla `[0x9c32e84]` faz `(v+1) % 3` | C | 0x9c27684…; o_9c255cc linhas 15–18 |
| Valores | 0 = tudo; 1 = **Skymessages only**; 2 = **Hide all** | C | rótulo 0x31 destacado com ==1, 0x30 com ==2 |

## 3. Tela de info (tecla padrão "1", 0x9c255cc + 0x9c25505 + 0x9c253e1)

É uma lista de atalhos, desenhada de cima para baixo, no canto superior direito, com FONT_DINS.

**Layout (C, 0x9c253e1):**
- A linha i fica em `T = SCR_T + i·FH`, com altura FH (FONT_DINS.vt0xc); o contador `this.0x361` é zerado a cada quadro.
- A coluna da tecla começa em `x = SCR_R − 3·CW` (CW = FONT_DINS.vt0x10) e mostra o caractere da tecla com flags (1,1). Só aparece se `isprint`.
- O rótulo vai de SCR_L até `x_tecla − CW/2`, com alinhamento 2 (**à direita**), e o texto vem de GetString(id) (tabela 0x9c338a8, 12 bytes por entrada).

**Cores (C, 0x9c25505):**

| Estado | Rótulo | Tecla |
|---|---|---|
| Ativo / selecionado | **0xfffe0000** (vermelho) | 0xfffefefe |
| Habilitado | 0xfffefefe (branco) | 0xfffefefe |
| Desabilitado | 0xff808080 (cinza) | 0xff808080 |
| Alpha | 0xc0 nas linhas habilitadas quando o Director's cut está ativo; senão 0xff | — |

**Linhas na ordem de desenho (C, o_9c255cc). Id = índice da string:**

| # | Rótulo (id) | Destaque quando | Tecla | Habilitada quando | Só em |
|---|---|---|---|---|---|
| 1 | Skymessages only (0x31) | ShowMode == 1 | e84 | sempre | — |
| 2 | Hide all (0x30) | ShowMode == 2 | e84 | sempre | — |
| 3 | Time left (0x32) / Lap counter (0x33) | ShowMode ≠ 2 e OPT_TIMELEFT | e9c | modo ≠ 0 ou ShowMode == 0 | não aparece se 0x9c070e0 (P: sessão sem limite) |
| 4 | Director's cut (0x38) | `G_9c34a99` | ea0 | ShowMode == 0 e sem time left | corrida |
| 5 | Standings (0x34) | `G_9c35110 == 1` | e8c | idem | corrida |
| 6 | Pit stops (0x3f) | `G_9c35110 == 2` | e8c | idem | corrida |
| 7 | Tower interval (0x1d, texto "Interval") | `G_9c35110 == 3` | e8c | idem | corrida, D2010 |
| 8 | Next page (0x37) | — | e98 | standings ou tower ligados | corrida |
| 9 | Tower standings (0x35) | `G_9c35114 == 1` | e90 | standings == 0 ou this.0x25 | corrida, D2010 |
| 10 | Tower pit stops (0x40) | `G_9c35114 == 2` | e90 | idem | corrida, D2010 |
| 11 | Tower interval (0x1e) | `G_9c35114 == 3` | e90 | idem | corrida, D2010 |
| 12 | Race progression (0x41) | `G_9c35120` | e94 | sempre | corrida, D2010 |
| 5' | Standings (0x34) | `G_9c35118 == 1` | e8c | — | quali/treino (0x9c26059) |
| 6' | Standings (times) (0x36) | `G_9c35118 == 2` | e8c | — | quali, D2010 |
| 7' | Next page (0x37) | — | e98 | standings ≠ 0 | quali, fora do D2010 |
| 13 | Gap car ahead (0x3b) | `G_9c3511c == 0` | e88 | standings desligado e gap ≠ 2 | corrida |
| 14 | Gap car behind (0x3c) | `G_9c3511c == 1` | e88 | idem | corrida |
| 15 | Speed traps (0x39) | `G_9c3511c == 2` | e88 | — | corrida, fora do D2010 |
| 13' | Split times (0x3a) | `G_9c3511c == 0` | e88 | — | não corrida, fora do D2010 |
| 14' | Speed traps (0x39) | `G_9c3511c == 1` | e88 | — | não corrida, fora do D2010 |

Notas:
- Teclas = endereços `[0x9c32eXX]` lidos do ini (chaves Info_*).
- "MicroTime" (0x3d) e "Track map" (0x3e) existem na tabela, mas **não** são desenhados nesta lista (C).

**Variáveis de estado (C):**
- `G_9c35110`: standings na corrida (0 desligado, 1 standings, 2 pit stops, 3 interval).
- `G_9c35114`: tower (0, 1 standings, 2 pit stops, 3 interval).
- `G_9c35118`: standings na quali.
- `G_9c3511c`: gap/split/speed trap.
- `G_9c35120`: race progression.

## 4. Tower (Digital 2010 apenas)

| Item | Detalhe | Conf. | Evidência |
|---|---|---|---|
| Escopo | só no modo 3 (D2010): as linhas 7, 9–12 da tela de info exigem `[0x9c3510c] == 3`; a doc diz "digital 2010 mode only" (teclas F/E) | C | o_9c255cc linhas 83, 104 |
| Assets | `gpxpdata\gfx\digital2010\tower\`: bg_first/bg_newlap 256x32 (útil 222), race_bg/race_bg_first 256x32 (útil 133), pos{negative,neutral,positive} 128x32 (útil 102), InPit/finished 64x32 (útil 45). PNG em `shared\tower_png\` | C | loader 0x9c1b384; alpha bbox |
| ini | `[Digital2010] RaceTowerLeftCars=10, QualTowerLeftCars=22, AlwaysShowTowerStandings=0` | C | sat_strings / ini |
| Layout | fica na spec do D2010 (agente separado) | — | — |

## 5. Track map (0x9c292d3 config, 0x9c296dc criação, 0x9c2aa48 quadro)

**Config `[TrackMap]` (C, 0x9c292d3):**

| Chave | Padrão | Observação |
|---|---|---|
| TrackMap | 1 | — |
| TrackMapPosX / PosY | 0 | — |
| TrackMapSize | 256 | — |
| TrackMapBackground | 0 | — |
| TrackMapColor | 0xffc0c0c0 | — |
| TrackEdgeColor | 0xff202020 | — |
| YellowZoneColor | 0xfff0f000 | — |
| TrackEdgeWidth | 3 | — |
| TrackWidthVariable | 0 | — |
| ShowCars | 1 | — |
| ShowCCs | 1 | — |
| ShowYellowZones | 1 | — |
| AutoShowEnabled | 1 | só efetivo com ShowYellowZones |
| ShowMode | 2 | é salvo de volta ao sair (0x9c29571) |

**Visibilidade (C, 0x9c29efb):**
- ShowMode 0 = oculto; 1 = sempre; 2 = **auto**, só aparece quando `GPxP_GetTimingInfo()+0xa` está ligado (bandeira amarela ativa).
- A tecla `[0x9c32ea8]` (padrão '8', 0x9c29f5c) faz `ShowMode = (ShowMode+1) % (auto ? 3 : 2)`.

**Posição (C, 0x9c296dc):**
- X: se `0 ≤ PosX < 1024`, aplica ScaleX(PosX); senão usa o valor bruto. Com **PosX < 0**, ancora pela direita: `L += SCR_R − largura`.
- Y: mesma regra, com 768 / ScaleY / borda de baixo.
- Rect `{L, T, L+size, T+size}`, em G_9c35330.

**Rasterização (C, uma vez, numa textura `G_9c3531c`):**
- Bounding box dos dois lados da pista (`g_pGPx.vt0x40/44(0/1)`): coords `(x<<3)|frac`; o eixo X é invertido (`(maxX − x)/escala`).
- Escala = máx(spanX, spanY)/(tex − 2·margem).
- Fundo `G_9c33198`, pintado só com TrackMapBackground.
- Primeiro passe = **borda** (TrackEdgeColor, espessura + TrackEdgeWidth); segundo passe = **pista** (TrackMapColor).
- Cada segmento é um quad girado pelo ângulo do segmento: `θ = π − ang·π/32768`.
- A largura do quad é fixa ou, com TrackWidthVariable, vem da largura real (+0x60/+0x62)·size/0x1900.

**Overlays por quadro (C, 0x9c2a4e7):**
- Zonas amarelas (ShowYellowZones e bandeira ativa): os segmentos com flag 0x1000 são redesenhados com a textura YellowZoneColor.
- Carros (ShowCars) usam o sprite `trackmapbuttons.tga` (48x16, 3 quadros de 16x16; PNG em `shared\trackmapbuttons.png`), girados pelo GetZAngle do carro:

| Quadro | Desenho | Uso | Lado |
|---|---|---|---|
| 0 | seta vermelha | carros da IA (só com **ShowCCs**) | ≈ size/32 |
| 1 | seta em círculo **azul** | carros de jogadores humanos | ≈ size/24 |
| 2 | seta em círculo **verde** | carro em foco, desenhado por último | ≈ size/24 |

## 6. Microtime (0x9c27fc0 config, 0x9c280ff desenho)

**Config (C):**
- `[Misc] ShowMicroTime=0`.
- `[MicroTime]`: PosX=10, PosY=10 (px, sem escala, P); MaxNameLength=10, limitado a 9..24; Abs=1; Compare=1.
- Teclas: M liga/desliga, H alterna Compare, J alterna Abs.

**Grade (C):**
- Fonte FONT_2D (Courier, Height 10). RH = altura + 2; CW = largura do caractere; N = MaxNameLength.
- Caixas em bevel `PDX.vt0x34(rect, fill, light, dark)`.

| Linha | Conteúdo | Cores |
|---|---|---|
| 0 | título "µtime", largura `2CW + 2·N·CW + 8`, texto em x+2 | fill 0xff010101, bevel d4d0c8/808080; texto 0xfffefefe |
| 1, col. A | nome curto da **referência**, à direita | 0xff000099 / 1675a5 / 000033 (azul) |
| 1, col. B | nome curto do **carro em foco** | 0xff339900 / 66cc33 / 003300 (verde) |
| 2–4 | rótulos "1", "2", "F" (célula 2CW) | fill 0xfffefefe, texto 0xff101010 |
| 2–4, col. A | tempo de referência do setor i | cinza decrescente (0xd4−15i, 0xd0−15i, 0xc8−15i) |
| 2–4, col. B | tempo do carro em foco | — |

**Texto (C):**
- Abs=1 mostra a diferença `%c%lu:%02lu.%03lu` / `%c%lu.%03lu` (sinal '−' se mais rápido); inválido = ` -:--.---`.
- Abs=0 mostra o tempo absoluto.
- Cor da coluna B: setor ainda não alcançado = 0xff808080; mais rápido que a referência = 0xfffe0000; senão 0xff101010.
- Referência: com Compare=1, é o carro da volta mais rápida (`(g_pGPx.vt0x114 & 0x3f) − 1`); com Compare=0, o próprio carro.

## 7. Janelas de mensagem (0x9c263b4, P)

- Duas janelas (this+4, this+8), alinhadas a `X1 − 12·FH`.
- Base em `H − n·FH − 10`; n = 4 ou o número de linhas (this.0x26).
- Cada uma é desenhada por 0x9c26f40/0x9c27c5e. P: sky messages / timers (Timer_PitStop, "Pit Lane"...).

---
## Lacunas

1. **Layout do tower** (posições, alturas, ordem das texturas) não foi decodificado aqui: depende do objeto D2010 (spec de outro agente).
2. **Janelas de mensagem** (seção 7): as funções 0x9c26f40, 0x9c27c5e e 0x9c26f70 não foram decompiladas. Conteúdo e cores são H.
3. **Valores reais de FH/CW** (FONT_DINS, FONT_2D) dependem do runtime. Não foram medidos.
4. **0x9c070e0** (oculta a linha Time left/Lap counter) e **0x9c26059** (corrida vs. quali): sentido inferido, P.
5. **Dimensão exata dos sprites de carro** no track map: as fórmulas `(size+31)>>5` e `size/24` foram lidas parcialmente (o operando `?` do decompilador). Os lados são P.
6. Os **mapeamentos de modo 0/1** (Classic vs. Digital) em `[0x9c3510c]` não foram reconfirmados aqui.
