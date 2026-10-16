# GPxPatch – velocímetro e instrumentos de cockpit: especificação (engenharia reversa estática)

Fonte: plugin GPxCInfo (`gpxpdata\GPxCInfo.gxp` descompactado: `cinfo\ci.bin`, com relocações aplicadas em
`cinfo\ci_rel.bin`, base 0x9e01000, CTO=6, code_end 0x1d000; disassembly em `cinfo\ci_rel.asm`), mais os ini
`gpxpdata\gpxcinfo.ini` e `gpxpdata\pnl\Lo2kTheme\*.ini`. Nenhum binário foi executado.
Confiança: **C** = certo (lido no asm ou no ini), **P** = provável, **H** = hipótese.

Referência: o painel é desenhado sobre uma tela lógica de 1024x768 (`[Misc] Width/Height` do Counter.ini).
Todas as coordenadas abaixo estão nessa tela lógica.

---
## 1. Origem dos instrumentos

| Item | Detalhe | Conf. | Evidência |
|---|---|---|---|
| Quem desenha | o plugin **GPxCInfo** ("Cockpit Info"), não o GPxSAT. Os modos do GPxSAT (Classic/Digital/2004/2010) não têm velocímetro nem tacômetro | C | strings de ci_strings.txt; GPxSAT sem refs a rpm/speed gauge |
| Painel ativo | `gpxcinfo.ini [Panel] Enable1=1, Path1=Lo2kTheme\Counter.ini` (tema "Lo2k Blue Theme"); slots 2..10 vazios | C | gpxcinfo.ini |
| Outro painel do tema | `Position Bar.ini` existe, mas está desabilitado (Rule_Line/Rule_Rainbow/Rule_Text, Mode 0 Truncate, WeightTop −1) | C | pnl\Lo2kTheme\Position Bar.ini |
| Por época | **não existe** opção por época/ano: o mesmo painel vale para qualquer temporada ou modo do GPxSAT | C | nenhuma chave de ano em gpxcinfo.ini nem em Counter.ini |
| Painel nativo do GP3 | o cockpit original (conta-giros 3D, LEDs de marcha) faz parte do GP3 base, e os arquivos dele não estão nesta instalação | — | ver Lacunas |
| Filtro | `[Filter] Enable=0` → desenha sempre (cockpit, TV, replay). A chave `ShowInCockpit` só vale com o filtro ligado | C | Counter.ini; string ShowInCockpit |

## 2. Gatilhos e teclas

| Item | Detalhe | Conf. | Evidência |
|---|---|---|---|
| Liga/desliga | ação `CInfo_ToggleOnOff` (tecla padrão **I**): ciclo de 4 passos (todos os painéis, ..., desligado) | C (ação) / P (ciclo) | string CInfo_ToggleOnOff; doc do GPxCInfo |
| Painel n | Ctrl+1..Ctrl+0 alternam os painéis 1–10 individualmente | P | doc; ações CInfo_Panel1..10 |
| Escala | `[Misc] Scaling=0` → sem escala própria; a tela lógica de 1024x768 é mapeada na resolução real | P | Counter.ini; H: proporcional em X e Y |

## 3. Configuração global do painel (Counter.ini `[Misc]`)

| Chave | Valor | Significado | Conf. |
|---|---|---|---|
| Font | RevFont | fonts.ini local: **Arial, Height 16, Weight 700 (bold)** | C |
| X, Y, Width, Height | 0, 0, 1024, 768 | área lógica | C |
| TextColor | 0x00ffffff | branco (alpha 0 no ini; o código força opaco, P) | C / P |
| CountOnTop | 0 | as Images ficam **por baixo** dos Items | C |

## 4. Fundo (Images, desenhadas primeiro, na ordem)

Arquivos exportados para PNG em `gauges\` (nome com `_` no lugar de espaço).

| # | Arquivo | Posição (x,y) | Tamanho | Textura original | Conf. |
|---|---|---|---|---|---|
| Image1 | Counter Color.tga | (72,403) | 200x200 | 512², disco azul semitransparente (nenhum pixel com alpha 255) | C |
| Image2 | Counter Display.tga | (72,403) | 200x200 | 512², números, marcas e zonas | C |
| Image3 | Gauge Temp.tga | (129,483) | 40x40 | 128², minimostrador de temperatura | C |
| Image4 | Gauge Fuel.tga | (174,483) | 40x40 | 128², minimostrador de combustível | C |

Geometria derivada: o centro do mostrador fica em **(172,503)**, com raio de 100 px (diâmetro ≈ 26% da altura). A escala impressa
de velocidade começa com 0 às 6h e corre 270° no sentido horário até 360 às 3h; 240 cai às 12h. Conf.: C para os valores do ini, P para a leitura da arte.
As imagens dos minimostradores têm centro em (149,503) (temperatura) e (194,503) (combustível).

Os pivôs das agulhas ficam nas bordas externas, e cada agulha varre um arco lateral de 90°. Raio útil ≈ 30 px; só a metade de cima da textura tem agulha.

| Agulha | Pivô | Varredura |
|---|---|---|
| Temperatura | (129,503), borda esquerda | de baixo-direita (225°) a cima-direita (315°) |
| Combustível | (214,503), borda direita | de baixo-esquerda (135°) a cima-esquerda (45°) |

Conf.: C (rects do ini), P (leitura visual).

## 5. Itens (desenhados depois, na ordem da lista `Items`)

ControlType=2 = **controle de imagem** (função 0x9e01383, config em +0x132). Os modos são 0 Truncate, 1 Scale, 2 Rotate, 3 Transpose,
4 Toggle e 5 Radial fill. Todos os ponteiros usam Mode 2 Rotate.

| # | Item (tipo) | Imagem | Rect (x,y,w,h) | Faixa → ângulo | Extra | Conf. |
|---|---|---|---|---|---|---|
| 1 | Gear (0) | texto | (165,520) | −1..7 | branco, RevFont; R/N/1..7 (P) | C |
| 2 | EngineTemp (4) | Gauge Needle Shadow | (127,474,2,60) | 80..125 °C, 225°→315°, anti-horário | alpha 0.4 | C |
| 3 | EngineTemp1 (4) | Gauge Needle | (128,473,2,60) | idem | — | C |
| 4 | Fuel (3) | Led1 / Led0 | (199,508,16,16) | Mode 4 Toggle, Threshold = ThresholdHi = 4 | Image0 = Led1 (vermelho aceso), Image1 = Led0 (apagado). Aceso com **≤ 4 voltas** de combustível | C (ini) / P (sentido ≤) |
| 5 | Fuel1 (3) | Gauge Needle Shadow | (212,474,2,60) | 0..20 voltas, 135°→45°, horário | alpha 0.4 | C |
| 6 | Fuel2 (3) | Gauge Needle | (213,473,2,60) | idem | — | C |
| 7 | Revs (1) | Rev Needle | (170,403,4,200) | 0..18000 rpm, 180°→270°, horário | 2ª agulha: **pico segurado** (P) | C |
| 8 | Revs1 (1) | Rev Needle Shadow | (170,404,2,200) | idem | alpha 0.4 | C |
| 9 | Revs2 (1) | Rev Needle | (171,403,2,200) | idem | TopFreezeDuration = 1500 ms | C |
| 10 | Speed (2) | Speed Needle shadow | (159,405,24,200) | 0..360 km/h, 180°→270°, horário | alpha 0.4 | C |
| 11 | Speed1 (2) | Speed Needle | (160,403,24,200) | idem | texto em X=328, Y=520, Align 3 (direita), Postfix `_km/h` → " km/h" | C |
| 12 | PO (26) | — | — | — | Enable=0 | C |
| 13 | Pit (9) | Pit Closed.tga | X=−2 (a partir da direita), Y=384, 128x32 | — | UseImages; aparece com o pit limiter/pit lane (P) | C (ini) / H (gatilho) |

Itens órfãos: `Speed2` e `EngineTemp2` estão definidos no ini, mas não entram na lista Items, então não são desenhados (C).
As seções Color/Offset/Length de controle de linha existem, mas são ignoradas, porque ControlType=2 (C).

**Convenção de ângulo (C pela leitura de código, P forte pela renderização em `_render_check.png`):**
- `ângulo = início + frac·(fim − início)`, com `frac = (v − Min)/(Max − Min)` limitado a 0..1 (0x9e01aa9–0x9e01c2d; float em +0x3a).
- Graus positivos giram no **sentido anti-horário**. Com 0°, a imagem fica como desenhada (agulha para cima).
- Com `Clockwise=1`, o fim recebe −360 até ficar abaixo do início (0x9e0120b–0x9e01382).
- O pivô é o centro do rect.

Validação pela renderização:
- 240 km/h aponta para 12h e 9000 rpm aponta para a marca "180", porque a escala de rpm é ×50.
- A temperatura e o combustível ficam coerentes com as zonas vermelhas da arte.

## 6. Texturas dos ponteiros (PNG em `gauges\`)

| Arquivo | Tamanho | Parte útil | Conf. |
|---|---|---|---|
| Gauge_Needle(_Shadow) | 8x128 | agulha só na metade de cima (y 0..71); pivô no centro | C |
| Rev_Needle(_Shadow) | 8x512 | y 73..240 | C |
| Speed_Needle(_shadow) | 64x512 | y 36..292, com cauda abaixo do pivô | C |
| Led0 / Led1 | 16² | apagado / aceso vermelho | C |
| Rule_Line / Rule_Rainbow / Rule_Text | 2x1024 / 2x1024 / 64x1024 | só o Position Bar (desabilitado) | C |

Prévias: `gauges\_preview_counter.png` (montagem estática) e `gauges\_render_check.png` / `_render_check_zoom.png`
(render com 240 km/h, 9000 rpm, 100 °C, 10 voltas e marcha 4).

## 7. Origem dos valores (bloco de export do CInfo, 0x9e020b5…)

| Valor | Fórmula | Conf. |
|---|---|---|
| Rotação | byte de rpm = raw/6.4 (escala interna); rpm exibido = raw | P |
| Velocidade | `(car.vt0x44 >> 16)·0.0047625·3.6` km/h; ÷1.609 quando `g_pGPx.vt0x78` (métrico) é falso | P |
| Temperatura | `car.vt0xc0 − 100` °C | P |
| Combustível | em voltas restantes (o tipo 3 imprime "F ") | P |
| Máximos padrão | Revs 20000, Speed 400; LongAcc −5..1.5 | C |
| Prefix / Postfix | `_` no fim do Prefix ou no início do Postfix vira espaço | C (0x9e16c61, 0x9e16cb0) |

Tipos de item (switch em 0x9e0766b, tabela 0x9e09294; C):

| Tipo | Item |
|---|---|
| 0 | Gear |
| 1 | Revs |
| 2 | Speed |
| 3 | Fuel |
| 4 | EngineTemp |
| 5 | Lap |
| 6 | Position |
| 7 / 8 | GapFront / GapBehind |
| 9 | Pit |
| 10 | LapTime |
| 11 | BestLapTime |
| 13–21 | HelpF1..F9 |
| 22 | Steer |
| 23 | Throttle |
| 24 | Brake |
| 26 | PO |
| 27 | Label |
| 28 | SuggestedGear |
| 29 | GameTime |
| 31 | LatAcc |

Tipos de controle (C):
- ControlType 1 = linha giratória (0x9e01000, config em +0x10e). É calculada como `dx = len·cos(θ)`, `dy = −len·sin(θ)`, com 0° apontando para a direita e o sentido anti-horário positivo.
- ControlType 2 = imagem.

---
## Lacunas

1. **Instrumentos nativos do GP3** (painel 3D do cockpit, LEDs de troca, mostrador do volante) não foram analisados: os dados e o exe do GP3 base não estão nesta instalação, que só tem o GPxPatch.
2. **Pit Closed.tga** não está em `pnl\Lo2kTheme\`. H: o CInfo cai no diretório `gfx`. O momento exato em que aparece e a ordem PitImage0/1 não foram confirmados (só PitImage1 está definido).
3. **Mapeamento real 1024x768 → resolução**: Scaling=0 e "Move to edges" estão em P/H, sem confirmação de proporção em telas wide.
4. **LED de combustível**: não está confirmado se a comparação com Threshold é `≤` ou `<`.
5. **Segunda agulha de rpm** (Revs, 4 px): leio como marcador de pico (TopFreeze). P: pode ser apenas a agulha "grossa" sob a fina.
6. As **fórmulas de export** (seção 7) vêm de leitura parcial do bloco 0x9e020b5. O fator de rpm não foi conferido contra um valor real.
