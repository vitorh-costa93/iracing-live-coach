# GPxPatch (GPxSAT) — especificação para os perfis do overlay AMS2

Fontes: instalação local `C:\Microprose\Grand Prix 3\gpxpdata` (só leitura; docs EN offline, `gpxsat.ini`, TGAs), help oficial
https://www.sdidit.nl/docs/gpxpatch/gpxsat.html (+ gpxcinfo, gpxlap), capturas oficiais Digital 2010 (https://imgur.com/a/gpxsat-digital-2010-ct0ei)
e uma captura do ATL (https://up.picr.de/39776456kq.jpg). Cópia dos gráficos extraídos: scratchpad `gpx/gfx` (não redistribuir, não commitar).
LIMITAÇÃO: a posição/animação exata do Classic, Digital e Digital 2004 está no código do exe (não há captura online acessível; fotos
do fórum em Photobucket estão mortas). Para esses, o desenho vem dos TGAs + das telas reais de F1 da época (já analisadas em `reference/`).

## 1. Mapa de perfis (GPxSAT `Mode`)
| Perfil GPxSAT | Época TV | Tema AMS2 existente | Ação |
|---|---|---|---|
| Classic ("terrestrial feed", logos animados Tag Heuer e Siemens) | ~1999 | f1-1998 (Broadcast98/99) | comparar e ajustar |
| Digital ("subscriber channels", 6 carros/página, caixas A–G/M/P coloridas) | ~1999-2001 alternativo | NOVO tema alternativo | criar |
| Digital 2004 (tudo animado, 8 carros/página, caixas itálicas, logo Siemens) | 2004 | f1-2004 | comparar e ajustar |
| Digital 2010 (torres, 5 carros embaixo, LG, logo F1, timers de pit) | 2010-2012 | f1-2012 (Broadcast12) | comparar e ajustar |

## 2. Telas do GPxSAT (todos os modos)
- **Qualifying timing**: recria as telas de classificação da F1. **Race timing**: em cada split mostra o gap para o carro à frente e atrás (e tenta mostrar gaps anteriores).
- **Speed traps** (corrida): 2 splits + linha; calculados dos eventos de corrida (não existe no Digital 2010). `SpeedTrapWidth` 50 (usuário usa 75): janela antes/depois do ponto.
- **Race positions**: blocos de N carros por página (Classic/2004: 8; Digital: 6; 2010: 5 embaixo), aposentados saem da lista. `V` navega as páginas.
- **Mensagens Sky substituídas**: Fastest lap, Out of the race, In the pits, Race won, Black flag, "Riding with" (câmera).
- **Track map** (pista desenhada, carros, CCs, zonas de bandeira amarela; auto-mostra só em amarela, `AutoShowEnabled`).
- **Microtime**: janelinha de tempo de volta no canto sup. esq. (todas as sessões), relativo (+0.123) / absoluto (1:23.456), comparação melhor da sessão × sua melhor.
- **Info screen (tecla 1)**, **Director's cut (D)**, **Time left (T)**, contador de voltas (T na corrida; `LapsLeftMode` voltas restantes × volta atual).
- **ATL (At The Line, GPxLap)**: painel de texto com 4 páginas (corrida: ordem na pista, voltas mais rápidas, tempos de volta, ordem de corrida; fora de corrida: melhores voltas e tempos). Colunas: Posição (+ nº magenta = rank de volta rápida), L/S (volta/setor), Status (box/falhas/aposentado); cores: branco = mesmo setor do líder, cinza claro = 1 setor atrás, cinza = 2+, vermelho = fora; "P" = jogador, "> <" amarelo = carro selecionado.

Teclas (todas remapeáveis): K liga/desliga (tudo/só sky/nada), S split/speed trap, B posições (cicla posições / posições com pits / off), F torre inferior (2010), E torre esquerda (2010), V navega carros, T tempo/voltas, M/H/J microtime, D director, 1 info, 8 track map.

## 3. Digital 2010 (o que as capturas mostram) — alvo do tema f1-2012
Elementos (TGAs em `gfx\digital2010`): barras inclinadas (paralelogramo) com cantos arredondados e brilho; logo **F1** embaixo à esq. e **LG** (patrocinador) à dir. do número do carro; fonte DIN-Bold.
- **Torre esquerda de classificação**: lista vertical posição + código 3 letras + tempo (líder em vermelho escuro `bg_first`, demais preto `race_bg`/`positions_neutral`); bandeirada quadriculada ao lado de quem terminou a volta/sessão (`finished.tga`); relógio de sessão em pílula no topo ("Q1 5:58"). `QualTowerLeftCars` 22.
- **Torre esquerda de corrida**: posição + código + gap ao líder (+2.570...), setas verdes/amarelas/pretas de ganho/perda de posição (`pospositive/negative/neutral`), ícone "P" no pit (`InPit`), 10 carros (`RaceTowerLeftCars`), mostra volta atual (`ShowLapInStandings`).
- **Caixa do piloto** (embaixo-esq.): nome (inicial + sobrenome), tempo da volta, delta verde/vermelho/amarelo; número do carro em losango grande (`big_position_first` vermelho para P1, `neutral` cinza) + LG.
- **Pit**: timers `pitlane_timer` (tempo no pit lane, ex.: 20.611) e `pitstop_timer` amarelo (tempo parado, ex.: 7.7) sobre a caixa do piloto; fim = `timer_green/finished`.
- **Barra inferior de corrida**: 5 carros `bottomscreenfirst` (P1 vermelho "Leader") + `bottomscreenpos` (posição, código, gap +1.575, +2.967...). `CarsPerBottomPage` 5, `F` cicla, `AlwaysShowTowerStandings`.
- **Chegada**: caixa **WINNER** (`data_winner` quadriculada vermelha, tempo total 5:16.671), nome do vencedor, equipe, velocidade média (157.786 km/h); à direita caixa de gap ("R SCHUMACHER (+2.417) / M HAKKINEN"); barra inferior com resultado final.
- **Contador de voltas** em pílula amarela no topo ("Lap 3 / 5"); `data_fastest` verde (volta mais rápida), `data_faster_medium` roxo (melhor da sessão), `gap_faster` verde/`gap_slower` amarelo/`gap_first` vermelho.
- `race_control.tga` (1024x32) = faixa larga de controle de prova (bandeiras/mensagens). `timing_background_1..3` = fundos de tempos de setor.
- Opções: `ShowF1Logo` 0/1/2 (2 = só câmera externa), cores `ColorTextPit` vermelho, `PitExit` verde, `Faster` verde, `Retired` cinza translúcido; fotos de piloto e bandeiras opcionais.

### 3b. Evidências do fórum GP4 (grandprixgames.org, 16/10/2026)
Fonte: tópicos "GPxPatch 4.10 RELEASED" (read.php?4,1031223) e "GPxPatch 4.11 W.I.P." (read.php?4,1031777), capturas imgur J7KQQvR e XqCj4yL (17/05/2013).
- **Standings "normais" (torre inferior, 2010)**: 8 carros em 2 colunas de 4 (esq. 1-4, dir. 5-8), cada linha = posição + nome "M SCHUMACHER" + gap "+3.297"; líder em barra vermelha, demais pretas, paralelogramo inclinado; **logo LG** centralizado acima, **logo F1** embaixo à esquerda; pílula "Lap 2 / 55" no topo central. Só aparecem na corrida, 8 carros/página (como na TV real).
- **Modo Interval** (4.11, tecla B): gap para o carro da frente em vez do líder; na barra de 5 carros a linha mostra "1 SCH Interval | 2 HIL +2.506 | 3 BRU +0.512 | 4 BEG +0.976 | 5 VER +0.446". Aparece com mais frequência conforme a corrida avança (a cada 2-3 voltas no modo diretor). Setas verde/vermelha de posição ganha/perdida só nas torres, não nas standings normais.
- **Caixa do piloto (embaixo-esq.)**: "J VERSTAPPEN / Benetton Ford" (nome + equipe), número em losango (5), logo LG ao lado; **caixa de gap à direita**: "4 G BERGER (+0.446) / 5 J VERSTAPPEN" (carro à frente com intervalo, piloto seguido).
- **Menu de teclas** no canto sup. direito (lista vermelha/branca: Standings, Director's cut, Pit stops, Next page, Gap car behind, ...), visível quando se pressiona a tecla de ajuda.
- **Marcadores de pit**: pequenos triângulos vermelho (carro entrou no pit, quem está atrás sobe) e verde (carro saiu) nas torres; só corrida.
- **Rótulos de sessão** editáveis em gpxsat.ini `[String]`: Short_FriPrac1=P1, Short_FriPrac2=P2, Short_SatPrac=P3, Short_Qualify=Q (padrão Q3 na 4.10), Short_PreRaceWarmup=PR. A pílula do relógio mostra "Q1 5:58".
- **Bandeira quadriculada na quali**: aparece ao lado do piloto quando laps<2, ou laps<3 estando no pit (carro sem mais voltas lançadas); deve permanecer até o fim da sessão (pedido da comunidade).
- **Classificação final** (planejada, não entregue na 4.10): título com ano + nome do GP, ao fim da sessão, para cada tipo de sessão. `three_letter_codes.ini` fornece os códigos de 3 letras; nomes são capitalizados (acento não é, bug conhecido).
- `CarsPerPage=5` em [Digital2010]. Mods de 3ª parte ("New F1 Graphics", Lo2k 2004 TV, siemens_2004.tga + LG) só trocam TGAs em `gpxpdata\gfx` e escolhem "Digital 2004" no menu GPxSAT.
- Capturas de Classic/Digital/2004 continuam sem fonte: as imagens do fórum são Photobucket/ImageShack mortas, os demais imgur dos tópicos são assinaturas/sprites.

### 3c. Changelog oficial (sdidit.nl/gpxpatch.html) — regras de cada modo
- **Origem**: Classic e Digital por Marc Aarts (2003, GP4 gráficos); Digital 2004 por Markus Krötz (v3.70, 24/04/2004) com fonte `digital2004` de Lo2k/Laurent Rousseau (3.80); Siemens no Digital 2004 (3.90, 12/2004); Digital 2010 "baseado em 2013" (4.10).
- **Classic**: logos Tag Heuer (novo por Kev Bell) e Siemens animados (tempo real, não de jogo); sombra na fonte; blue.tga cobre números 31-39 e 65-99; razão de abandono "accident" quando não é falha; tela de penalidade (bandeira preta) com logo do pneu; "Riding with" **só no Classic** aparece ao trocar de câmera (nos outros modos desligado por padrão).
- **Digital / Digital 2004**: tela de penalidade na corrida; `LapsLeftMode=1` mostra volta atual em vez de voltas restantes; Digital 2004: contador de volta/tempo **fica amarelo sob bandeira amarela**, indicador de nome curto da sessão (P1/Q...) fora da corrida, Siemens opcional (`ShowSiemensLogo`), fonte padrão tamanho 17; Digital: formato do nome do piloto configurável; fix texto de volta de aquecimento.
- **Todos**: `S` alterna gap carro à frente → gap atrás → speed traps; `B` classificação (tempos ↔ pit stops, 3.90); `D` director's cut (lembra on/off; B/F/V/S saem dele); tecla `1` mostra mapa de telas; tecla `8` track map (rotaciona com o carro; jogador usa a 2ª metade de trackmapbuttons.tga; 3 modos; zonas amarelas); microtime lembra on/off; classificação em treino livre se nº de carros > 1; `ReplaceSessionOver=1` troca a mensagem "session over".
- **Digital 2010**: 'normal' standings em `B` (8 carros/página), torre inferior em `F` (estado lembrado), torre esquerda de corrida em `E` (com progressão, mostra volta atrás do líder, `ShowLapInStandings`), página interval em `B`/`F`, pit stops no director's cut só após o 1º pit; **splits sempre na quali** (S desativado); posição durante volta voadora só se melhorou o PB; ícone "P" no pit assim que o carro entra no pit lane (quali/treino); `QualTowerLeftCars`; mensagem de controle de prova com motivo de abandono e corte de pista fora da corrida (em vez de bandeira preta); bandeira de país opcional atrás do nome; foto do piloto opcional acima do nome; logos de pneu por carro (até 11 fabricantes); todas as cores de texto configuráveis; tela de jump start.

## 4. Classic (~1999) e Digital (alternativo)
- **Classic**: faixa preta "TAG HEUER TIMING" (logo TAG vermelho/verde + wordmark) e logo Siemens alternados (animação: show 1500ms, fade out 1500/500, idle 500). Posições em blocos de 8; caixas coloridas redondas A–G/M/P (círculos 32px com letra: A preto, B vermelho, C laranja, D amarelo, E azul, F vermelho-claro, G dourado, M azul, P amarelo/vermelho) marcam pneus/estado; grade de quadrados amarelos com nº 1–22 para bandeira amarela (zona de pista); bandeira quadriculada ondulada na chegada (`classic_flag`). Fonte "manana". Sombra de fonte ligada.
- **Digital**: mesmos A–G/M/P porém como quadrados 64px (A preto, B vermelho, C laranja, D amarelo, E roxo-azul, F vermelho, G dourado, M azul, P amarelo/vermelho); 6 carros/página; fonte "futura"; xadrez digital no final; mesma grade amarela de nº. É o "alternativo ao de 1999" (canais por assinatura).
- **Digital 2004**: mesmos códigos em itálico com leve bisel, cores um pouco mais fortes (A preto, B vermelho, C laranja, D amarelo, E azul, F vermelho, G dourado, M azul-marinho, P amarelo); logo SIEMENS (branco sobre preto ou ciano `siemens.tga`); 8 carros/página; fonte "digital2004"; todas as telas animadas (fade 10ms/passo, rotação 10ms/passo); `ShowSiemensLogo`, `ShowShortSessionName`.

## 5. Quando cada widget aparece (regra de visibilidade)
- Qualifying/Practice: torre de posições (B), split/speed (S), tempo restante (T), microtime, ATL 2 páginas.
- Race: contador de voltas, gaps a cada split (à frente, depois atrás), speed traps, posições paginadas (B), torre inferior (F, 2010), torre esquerda (E, 2010), pit timers, vencedor, director's cut.
- Todas: track map (auto só em amarela), info, sky messages, microtime.
- Cockpit: `ShowInCockpit` 0 (usuário) — widgets somem na visão de cockpit; `ShowF1Logo=2` só externa.

## 6. Próximos passos propostos (a validar contra os temas AMS2 existentes)
1. Diff widget a widget: f1-2012 × seção 3; f1-1998 × Classic; f1-2004 × Digital 2004; criar f1-digital como alternativo ao 1998 (ver mockups/2000s).
2. Mapear dados do AMS2 (shared memory) para cada widget; apontar lacunas (sem Q1/Q2/Q3; sem SC distinto de amarela).
3. Capturar frames reais do Classic/Digital/2004 se o usuário instalar o GP4 ou fornecer vídeos.
