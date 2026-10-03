# Notas do Radar: V3 (iRacing) x AMS2 (03/10/2026)

O usuario disse: "O unico widget do AMS2 que eu quero e o Radar". Este documento registra (1) o que o Radar do V3 faz (so leitura de
`v3/src/IracingLiveCoach.OverlayHost/Widgets/RadarWidget.cs`, `v3/src/IracingLiveCoach.Core/Telemetry/RadarSideAssigner.cs`, `RadarSideOffsets.cs`,
`TelemetryReader.UpdateRadar`, `Widgets/SimulationData.cs`), (2) o que o AMS2 entrega de diferente e (3) as decisoes da replica.

## 1. O Radar do V3

| Aspecto | V3 |
|---|---|
| Janela | 120 x 190 dip (`WidgetPlacement(0, 900, 640, TopLeft, 120, 190, ...)`), posicao padrao x=900, y=640 (tela 1920x1080): centrada na horizontal (960 - 60), um pouco abaixo do meio |
| Escala vertical | `RangeMeters = 15`: +/-15 m cabem na altura. `pxPerMeter = (h/2 - 10) / 15` = 5,67 dip/m. Carro = 4,8 m x 5,67 = 27 dip de altura, 16 dip de largura (fixo) |
| Faixas | O jogador no centro (cx, cy). Carros da esquerda em `cx - 30`, da direita em `cx + 30`, sem lado em `cx`. 30 dip = faixa lateral fixa (nao ha posicao lateral real) |
| Fonte dos dados | iRacing nao publica posicao lateral: so `CarIdxLapDistPct` (distancia na volta, exata) e o flag `CarLeftRight`. O lado e INFERIDO (`RadarSideAssigner`); a posicao vertical e exata |
| Alcance de deteccao | `RadarMaxRangeMeters = 30` no leitor (carros alem disso nem entram); o desenho so mostra +/-15 m (clamp) |
| Janela "lado a lado" | `RadarSideAssigner.OverlapMeters = 7,0 m`: so carros com abs(distancia) <= 7 m recebem lado (o flag do iRacing dispara um pouco antes dos corpos se sobreporem) |
| Cores por proximidade | abs(d) <= 7 m e lado != centro: **Critical #FF5252** (vermelho). abs(d) <= 12 m: **Warning #FFCC00** (ambar). Alem: TextSecondary #A6B0BB (cinza). Carro do jogador: PlayerHighlight #00C9E8 (ciano) preenchido |
| Preenchido x contorno | Cheio, exceto carro "no centro" com abs(d) > 12 m: so contorno (alfa 0,55, traco 1,6) |
| Indicadores laterais | Barras vermelhas (Critical) de 4 dip, arredondadas, coladas nas bordas (x+3 esquerda, x+w-7 direita, de y+8 a y+h-8), acesas quando o iRacing reporta o lado ocupado (`CarLeftRight` 2/4/5 esquerda, 3/4/6 direita) |
| Guias | Linhas de 1 dip em +/-5 e +/-10 m (cor PanelDivider #2F4B5E 0,85), de x+10 a x+w-10 |
| Painel | Fundo `RadarPanelBackground` #0A1520 alfa 0,45 (mais translucido que os outros widgets), borda PanelBorder #36586D |
| Visibilidade | O widget so desenha enquanto ha carro lado a lado (`RadarSideOffsets.IsVisible`: CarLeftRight 2..6, como o Kapps); fora disso nada (nem painel). Sem pista/telemetria: nada |
| Pit lane | Carro na pista de boxes so e vizinho se o jogador tambem esta nos boxes (`CarIdxOnPitRoad` igual) |
| Pace car | Ignorado (`IsPaceCar`) |
| Taxa | `RadarTickInterval = 1`: reage a cada tick de 60 Hz do SDK; render por vblank (ver `fps-notes.md`) |
| Opcoes persistidas | `RadarRangeMeters` (55, nao usado pelo desenho atual), `RadarShowDistanceLabels` (true, idem). Sem modo de aviso configuravel alem de mostrar/ocultar, escala, opacidade |
| Simulacao | `SimulationData.Radar`: um carro se aproxima 18 m por tras na esquerda, passa ao lado e se afasta; depois o mesmo na direita; ciclo de 14 s |

## 2. O que o AMS2 entrega (memoria compartilhada `$pcars2$`, v14)

Diferente do iRacing, o AMS2 publica por participante `WorldPosition` (x, y, z, metros), `CurrentLapDistance`, `Orientations[64][3]` (radianos), `Speeds[64]`.
Ou seja, a posicao lateral e REAL: nao precisa inferir lado. Para o jogador (participante visto) ha tambem `Orientation[3]`, `LocalVelocity`, `WorldVelocity`.

### 2.1 Orientacao (CONFIRMADA empiricamente, 03/10/2026)

* Fonte: dump real `tests/Ams2.Core.Tests/Data/ams2-v14-interlagos.bin` (18 carros, pista de ~4,3 km) e a memoria viva do jogo (26 carros, so leitura, sem teclas nem cliques).
* Metodo: para cada carro em movimento, rumo real = `atan2(dx, dz)` do deslocamento da `WorldPosition` entre duas leituras 0,3 s depois; comparado com `Orientations[i][1]`.
* Resultado: **rumo = yaw + pi** (erro medio 0,058 rad em 24 carros; sem o pi o erro e 3,08 rad). Vetor de avanco no plano (x, z) = `(-sin yaw, -cos yaw)`.
  No dump, carros vizinhos (20 a 40 m de distancia na volta) tem `atan2` do vetor ate o proximo carro = yaw + pi com erro < 0,05 rad nas retas.
* O eixo vertical e Y (`LocalVelocity.y` e a velocidade vertical; yaw = `Orientations[i][1]`, pitch/roll = indices 0 e 2 com valores ~ -0,05).
* `Orientation[1]` do jogador e `Orientations[viewed][1]` sao o mesmo numero (1,327 e 1,327 na leitura viva).
* Lateralidade (direita/esquerda): com rumo h = atan2(dx, dz), a direita do carro e `(cos h, -sin h)` (sistema com x a direita, z a frente, y para cima).
  Confirmado indiretamente: a soma das variacoes de yaw ao longo de uma volta em Interlagos (sentido anti-horario) deu -6,28 rad (-2 pi), isto e, curva a esquerda diminui h.
  Ficou **inferido** (nao ha um teste direto com um carro conhecido a esquerda): a lateralidade depende de Interlagos ser anti-horario e do jogo nao espelhar a pista.

### 2.2 O que ficou aproximado

* Comprimento e largura do carro: fixos em 5,0 x 2,0 m (o AMS2 nao expoe dimensoes; `ExtentsCentre` e so o centro de massa do jogador).
* `Speeds[]` e a velocidade escalar; a velocidade relativa usa `Speeds` e o rumo relativo (nao a velocidade de mundo de cada carro).
* A posicao e a do centro do carro (origem do modelo), nao do ponto medio da carroceria.

## 3. Replica no AMS2 (decisoes)

* **Tracker** puro (`Ams2.Core/Calc/RadarTracker.cs`): para cada carro, vetor jogador -> carro rotacionado pelo rumo do jogador: `Forward` (m, + a frente) e `Right` (m, + a direita);
  rumo e velocidade relativos; zona de proximidade; carros mais proximos primeiro (maximo 16). Saida imutavel (`RadarFrame`), sem alocacao por quadro (anel de 4 frames reaproveitados).
* **Alcance**: +/-15 m na vertical (como o V3), configuravel de 10 a 40 m. Lateral: +/-7,5 m (60 dip de meia-largura a 8 dip/m, como as faixas de 30 dip do V3 = 3,75 m).
* **Zonas** (iguais ao V3, agora com lateral real): `Alert` (vermelho) = lado esquerdo/direito (abs(lateral) >= 1,2 m) com abs(frente) <= 7 m (e abs(lateral) <= 4,5 m);
  `Warn` (ambar) = abs(frente) <= 12 m; `Far` (cinza) alem disso. Sensibilidade = multiplicador (0,5x a 1,5x) das janelas de 7 e 12 m.
* **Barras laterais** vermelhas (esquerda/direita) acendem quando ha carro em `Alert` daquele lado.
* **Pit lane / garagem**: carro na garagem e ignorado; carro nos boxes so conta se o jogador tambem esta (igual ao V3). Retardatarios (volta diferente) contam normalmente: o radar e geometrico.
  Trechos paralelos da pista (viaduto/pista proxima) ficam fora por um filtro de distancia na volta (> 3 x alcance + 20 m) e por diferenca de altura (> 3,5 m).
* **Visibilidade**: so em sessao (`InSession`), com pose do jogador valida e ao menos um outro carro na pista. Modo padrao = aparece quando ha carro dentro do alcance (com 0,6 s de permanencia
  e esmaecimento); coluna "Sempre visivel" = painel fixo enquanto a sessao tem carros ao redor. (O V3 so aparece com carro lado a lado; aqui o gatilho e o alcance configuravel.)
* **Desenho**: igual ao V3 (painel 120 x 190 dip, jogador no centro, carros como retangulos arredondados, guias a cada 5 m, barras laterais), carros posicionados pela posicao REAL (nao em faixas fixas).
  Cores/tipografia por tema (1998, 2004, 2010s); alertas sempre vermelho/ambar/cinza.
* **Posicao padrao**: igual ao V3 em x (900, centrado), y = 585 (o V3 usa 640, mas o Pit Timer padrao ocupa y 780-831 e o teste de layout proibe sobreposicao).
* **Render**: `IWidget.HighFrequency = true` (a cada vblank, ver `fps-notes.md`); entre dois passos de 60 Hz do provider as posicoes sao extrapoladas pela velocidade relativa (ate 50 ms).

## 4. Validacao (03/10/2026)

* Fake: `AMS2_FAKE_RADAR=1` (so `--fake`/`--png`): o jogador segue em reta e 4 carros orbitam numa elipse de 20 x 3,6 m (12 s por volta, defasados de 90 graus). Em t = 3 s um esta a direita e outro a esquerda (os dois em Alert, barras vermelhas); os de +/-20 m ficam fora do alcance de 15 m. Exemplo:
  `AMS2_FAKE_RADAR=1 Ams2.OverlayHost.exe --png radar.png --widget radar --theme f1-2010s --sim 3 --scale 1.5` (imprime `[RADAR] ...` com frente/direita/zona de cada carro). `--cols none` = modo "so com carro proximo"; `--radar-range 10..40` e `--radar-sens 1..5`.
* Contact sheet (3 temas x 4 instantes, 1,5 / 3 / 4,5 / 7,5 s) conferida visualmente; nao versionada (preview-*.png nao entra no repositorio). O V3 nao tem print/mockup do Radar no repositorio, a comparacao foi pela geometria e cores da secao 1 (painel 120 x 190, jogador no centro, guias a cada 5 m, barras de 4 dip, cores #FF5252 / #FFCC00 / cinza).
* Taxa (`--fake --widget radar --seconds 5 --measure`, monitor de 165 Hz): render do radar ~153 fps; provider 60 passos/s; CPU ~4% de 1 nucleo. Sem carro por perto (modo padrao) o widget e "ocioso" (`IWidget.IsIdle`): o host o redesenha so a 60 Hz (limpando a janela) e nao pede o ritmo de vblank.
* Testes: `RadarTests` (esquerda/direita, atras, a frente, rotacao do jogador em varios yaw, retardatarios, pit lane/garagem, alcance e sensibilidade configuraveis, pista paralela/viaduto, sem dados, ordem/capacidade, zero alocacao em 5000 chamadas, quadro imutavel no anel, dump real: rumo = yaw + pi e carro a 27 m a frente), `RadarSettingsTests`, `RadarFakeTests` (--png com o fake) e as tabelas de layout/tamanho de projeto.
