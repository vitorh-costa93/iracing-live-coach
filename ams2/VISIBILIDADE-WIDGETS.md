# Quando cada widget do AMS2 aparece (corrida e classificação)

Três filtros em sequência; o widget só desenha se passar nos três (`WidgetWindow.Visible`):

1. **Ligado** no Control Center (caixa do widget). Desligado = nunca.
2. **Sessão**: caixas "Treino / Classificação / Corrida" de cada widget (`WidgetSettings.Sessions`; vazio = padrão do widget).
   Treino = Practice/Test/TimeAttack, Classificação = Qualify, Corrida = Race + volta de formação.
3. **Jogador no carro** (`PlayerDriving`): jogo em GameState 2, fora da garagem, câmera no seu carro. Menu, pausa, replay ou câmera de outro carro escondem tudo (volta em 0,15 s). Exceção: opções "Sempre visível" de alguns widgets.

## Padrões por sessão
| Grupo | Treino | Classificação | Corrida |
|---|---|---|---|
| Standings, Board, Lap Counter, Pit Stops, Driver Caption, Winner, Race Start, Race Control, Battle, Pos. Caption, Ticker | sim (alguns só corrida) | não | sim |
| Quali Tower, Quali Lap, Quali Board, Quali Result | não | sim | não |
| Relative, Fuel, Tyres, Weather, Inputs, Radar, Live Speed, Pit Timer, **Lap Delta** | sim | sim | sim |

Na classificação o Quali Tower ocupa o lugar da Standings, e o Quali Lap o do Board (grupos exclusivos em `WidgetLayout.ExclusiveGroups`).

## Widgets que aparecem só em eventos ("rotação")
- **Driver Caption / Winner / Pit Stops / Pit Timer / Race Control**: só no evento (nova volta, bandeirada, entrada nos boxes...). A coluna "Sempre visível" os fixa na tela.
- **Quali Lap**: só em volta lançada (fora do box e da volta de saída) e por `showFor` s após cruzar a linha; "always" fixa.
- **Quali Result**: só ao fim da sessão de classificação.

## Tema com GPxSAT (1999, 1999 Digital, 2004, 2012)
O GPxSAT é UMA camada de tela inteira que substitui vários widgets e escolhe **uma tela por slot**, por prioridade (maior vence):
- Corrida: carro em foco nos boxes (cronômetro) > mensagens do alto (vencedor, volta mais rápida, abandono) > Director's cut > tabela de corrida (se ligada) > gap à frente/atrás/speed trap, que aparecem só na janela de cada passagem.
- Classificação: mensagens do alto > **parcial/resultado da volta** (janela `splitSeconds`, padrão 8 s depois de cada setor e da linha) > tabela de classificação (se `qualiStandings` ligado e houver tempos) > cartão do carro/gap.
- Relógio da sessão (ShowTimeLeft) fica em slot próprio e convive com as telas acima.

### Bug corrigido (19/10/2026)
Com `qualiStandings` ligado (padrão) e pelo menos dois tempos na tabela, os modos Digital 2004 e Classic/Digital (1999) devolviam a tabela e saíam antes de avaliar a parcial. A tabela ficava 100 % do tempo e as parciais com comparação nunca apareciam. Agora a parcial/resultado tem prioridade durante a janela e a tabela volta depois.

## Lap Delta (novo, temas 2004, 2012 e 2018)
Desligado por padrão: ligue em "Lap Delta" no Control Center (posição/escala/sessões como os demais). Mostra o delta ao vivo contra a melhor volta **observada pelo overlay** (a primeira volta limpa vira a referência; melhor volta feita antes de abrir o overlay não tem traçado), as parciais S1/S2/S3 (tempo e diferença para o melhor setor pessoal, "BEST" quando é o melhor) e a última volta.
