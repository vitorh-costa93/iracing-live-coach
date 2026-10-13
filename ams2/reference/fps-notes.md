# Notas de FPS: Inputs do AMS2 x Radar/Start Helper do V3 (03/10/2026)

Feedback do usuario: o grafico de Inputs do AMS2 tinha de rodar a mais fps e ter a mesma base do Start Helper e do Radar do V3.

## 1. Como o V3 faz (so leitura de `v3/src/IracingLiveCoach.OverlayHost` e `.Core`)

| Aspecto | V3 (Radar, Start Helper) |
|---|---|
| Taxa de dados | `IRacingSdk { UpdateInterval = 1 }` (IRSDKSharper): `OnTelemetryData` a cada tick de simulacao do iRacing = 60 Hz. `RadarTickInterval = 1`, `ProximityTickInterval = 1`: Radar e Start Helper reagem em todo tick. Clima 10 Hz, resto por contador de ticks. |
| Thread de dados | A do proprio SDK (evento). Publica `RadarStatus` etc. por evento; o widget guarda o ultimo valor. |
| Render | **Uma thread so** (a da UI) com `PeekMessage` em laco **sem Sleep**; cada widget tem seu `DeviceResources` (D3D11 + D2D + DirectComposition, `CreateSwapChainForComposition`, `FlipSequential`, `BufferCount = 2`). |
| Ritmo | **`Present(1)`** (vsync) em cada swap chain: o bloqueio do Present e o relogio. Nao ha timer proprio por widget, nem `DwmFlush`, nem `timeBeginPeriod`, nem prioridade de thread. Medicao de pacing em `v3-phase3-pacing.log` (p50/p95/p99/max do tempo de quadro). |
| Desacoplamento | Dados (60 Hz do jogo) e apresentacao (refresh do monitor) sao independentes: o laco redesenha o ultimo estado a cada vblank. Radar/Start Helper nao interpolam: desenham o `Status` mais recente (limite pratico = 60 Hz dos dados). |
| Jitter | Evitado por: vsync no Present (quadro alinhado ao vblank), flip model (sem copia), `Commit()` do DComp a cada quadro, tudo na mesma thread (sem lock entre dados e render: leitura de um record imutavel/ultimo valor). |

Conclusao: a "base" do V3 = render dirigido por vblank (nao por timer), um quadro por refresh, dados publicados de forma atomica e lidos pelo render sem espera. Nao existe uma classe de scheduler reutilizavel; o padrao e o proprio laco + `Present(1)`.

## 2. Estado do AMS2 antes (medido em 03/10/2026, monitor 165 Hz, AMS2 aberto)

* Memoria do jogo (`$pcars2$`, Ams2.Spike `rate`, so leitura): **~164 escritas completas/s** (SequenceNumber par; p50 6,1 ms entre escritas) com a GPU livre; ~134/s (p95 12 ms) com o jogo a 98% de GPU. Ou seja, o jogo atualiza a >120 Hz: da para amostrar a ~165 Hz.
* `OverlayDataProvider`: loop de 60 Hz com `Thread.Sleep` sem `timeBeginPeriod` (granularidade de 15,6 ms: intervalos de 15,6/31 ms) e **amostra de entrada a 30 Hz nominal, 21/s efetivo** (arredondamento ao tick de 15,6 ms; intervalo p50 46,6 ms), reconstruindo e copiando um array de ate 300 itens a cada amostra.
* Render (`HostController.Run`): todas as janelas a 60 Hz por `Sleep`, `Present(0)`; o grafico usava `model.Now` (instante do ultimo passo do provider), entao rolava em degraus e alocava 4 arrays por quadro.
* Gargalos: (1) amostragem 21 Hz, (2) render preso a 60 Hz de relogio com jitter de timer, (3) tempo do grafico quantizado ao passo do provider.

## 3. O que foi feito

* `InputSampler` (Ams2.Core): thread propria (AboveNormal, espera de ~1 ms), le **so 4 campos** do mapa (seq + acelerador/freio/volante, protocolo seqlock: seq par e igual antes/depois) e grava uma amostra por escrita do jogo, com teto de 240 Hz. Nao monta o snapshot de 64 carros.
* `InputRing` (Ams2.Core): buffer circular fixo de 4096 amostras (>= 10 s a 240 Hz), um escritor, leitores sem lock (contador publicado depois do slot; leitor descarta o prefixo possivelmente sobrescrito). Sem alocacao por amostra nem por quadro. `Clear` quando o relogio volta.
* Widget Inputs: `HighFrequency = true`; "agora" = relogio do render (`InputRing.Now`), colunas de pixel interpoladas no instante da coluna (scroll continuo por tempo), valor mantido por ate 50 ms alem da ultima amostra; buffers do grafico reutilizados. Vale para os 3 temas (inclui o mostrador analogico do f1-2004 e seu grafico opcional).
* Host (superado pela secao 5): `IWidget.HighFrequency`; widgets marcados rodavam a cada vblank, com `DwmFlush`/relogio por prazo, `Present(0, DoNotWait)` e `BufferCount = 3`. Esse texto sobre `DwmFlush`/`Sleep` ficou obsoleto.
* Medicao: `RateStats`, `Ams2.OverlayHost --fake --widget inputs --seconds 6 --measure` (imprime `[FPS] ...`), `ams2\tools\measure-fps.ps1`, `Ams2.Spike rate [s]` (memoria do jogo, so leitura), teste de integracao `FpsMeasureTests`.

## 4. Numeros antes/depois (`--fake`, widget Inputs, monitor 165 Hz)

| Metrica | Antes (33416b2) | Depois, GPU livre | Depois, AMS2 a 98% de GPU |
|---|---|---|---|
| Render do Inputs | 60,1 fps (p50 15,6 ms, p95 30,7 ms) | **164,6-165,0 fps** (p50 6,1 ms, p95 6,2 ms) | 63-89 fps (antes, mesma carga: 54-59 fps) |
| Amostragem das entradas | **21,0 /s** (p50 46,6 ms) | **195-197 /s** (p50 5,0 ms; teto 240) | 195-197 /s |
| Passo do provider | 60,1 /s | 60,0 /s (nao e mais gargalo) | 60,0 /s |
| CPU do processo (1 janela) | n/d (nao medido na epoca) | 2,6-4,9% | 6-8% |

Observacao honesta: com o AMS2 usando 98% da GPU, o `Present` espera a GPU/DWM (8-10 ms em media, picos de ~30 ms) e o fps de render do overlay fica em 60-90 independentemente do desenho (testado: 2-6 buffers, `Present(1)`, prioridade de GPU +7, pular o `DwmFlush`; nada passou disso). Mesmo assim o scroll e continuo porque o grafico e desenhado pelo tempo real do quadro, nao por amostra; e a amostragem nao depende do render.
Os valores `--fake` usam a fonte falsa em processo (nao leem o jogo); a amostragem do fake e limitada pelo teto de 240 Hz (o valor ~196 vem da espera de 1 ms do laco).

## 5. Modelo V3 replicado no AMS2 (11/10/2026)

Diagnostico: o V3 e fluido porque o `Present(1)` bloqueante e o relogio do laco (BufferCount 2, `Commit` do DComp por quadro, sem Sleep no caminho quente) e todas as janelas visiveis sao renderizadas a cada volta. O AMS2 usava relogio por deadline/`SleepUntil`, `Present(0, DoNotWait)`, `BufferCount = 3`, descartava quadro em `WAS_STILL_DRAWING` e fazia `Commit` so na criacao: quadros entregues fora do vblank e descartes viravam jitter. O teste anterior "Present(1)" (secao 3, ultima observacao) so trocou o `SyncInterval`; o resto do modelo (relogio por timer, DoNotWait/3 buffers, sem Commit por quadro) continuou diferente, por isso nao mostrou ganho.

Mudancas:
* `HostController.RunLoop`: widgets HighFrequency (nao ociosos) sao desenhados em sequencia a cada volta; nenhum Sleep/SleepUntil quando ha HF ativo. Sem HF ativo: dorme ate o proximo passo de 60 Hz (nao-HF a 60 Hz por acumulador, nao bloqueiam o Present por nao encherem a fila); sem janela visivel: 50 ms.
* `DeviceResources`: `BufferCount = 2`, `Present(1, None)` bloqueante, sem descarte em `WAS_STILL_DRAWING`, `Commit()` a cada quadro; device lost em Present/Commit recupera como antes.
* `WidgetWindow`: nao forca mais `PresentInterval = 0`.
* Inputs: o traco extrapola o ultimo valor ate "agora" com teto de 100 ms (antes 50 ms, que gerava degrau).
* Mantidos: `InputSampler` 240 Hz, `InputRing`, `SetGPUThreadPriority(7)`/D3DKMT, `timeBeginPeriod(1)` e thread AboveNormal (ainda uteis para o Sleep do caminho nao-HF e sem dano; nao sao usados no caminho quente).
* Risco/pendencia: validar ao vivo (fps, pacing, CPU) com jogo aberto; com varias janelas HF cada Present(1) bloqueia em sequencia (igual ao V3), o que pode limitar a taxa por janela se houver muitas HF visiveis.
