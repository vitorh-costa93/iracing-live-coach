# Implementação 1993

Tema F1 1993 disponível no Control Center. Board de corrida com gap fixo do vizinho físico mais próximo, volta mais rápida e legendas completa/onboard. Quali Board com cronômetro desde a primeira volta lançada, referência retida, parcial e resultado em ciano. Tabela de tela inteira excluída. Configuração de tipografia por elemento e dimensões independentes reutilizada.

O ponto de comparação da corrida é configurável (0–99,9%); padrão linha de chegada. Resultado permanece 7 s, configurável entre 3–15 s. Esses padrões são escolhas do produto; o vídeo não comprova o gatilho exato. A seta verde pode ser ocultada e não indica tendência calculada. Carros são silhuetas originais, fontes/opacidade aproximadas; não foram extraídos retratos ou assets da transmissão. Transições por corte, conforme trechos observados, sem inventar animação deslizante.

Validação por telemetria sintética e renderização do aplicativo. Não houve teste de pilotagem real nesta etapa.

## Correções de 14/10/2026

Race Board mantém a legenda do jogador com foto (ou silhueta), nome, equipe e posição entre os eventos. A prioridade continua pit → fastest → voltas restantes → gap → legenda. Gap medido e voltas restantes conservam sua duração visível quando encobertos por um evento prioritário; uma volta rápida de sete segundos não pode consumir toda a janela do gap ou das voltas restantes. Menu/pausa, troca de sessão ou identidade e descontinuidade do relógio descartam os eventos pendentes.

A identidade dos adversários é consultada por piloto **e carro/classe**, sem selecionar a primeira temporada do catálogo. A equipe vem da pintura do carro, independentemente do tema visual: Schumacher num carro de 1999 é Ferrari mesmo no tema de 1993; a pintura de 1993 continua Benetton. Cache de pintura de outro carro/fornecedor não é reutilizado. Nomes ambíguos sem carro compatível permanecem sem identificação; não se inventa uma equipe.

## Alinhamento com a lógica de aparição de 1999 — 17/10/2026

Qualy Board usa `QualiBoard99Presentation` (sem duplicar cálculo): out lap (`FIRST LAP OUT OF PITS` sobre a legenda), relógio desde a volta lançada, aproximação até 5 s antes da referência, intermediária 3 s (delta ciano, `INTERMEDIATE n`) e resultado por `showFor` (6 s padrão). Volta mais rápida (7 s) continua depois do resultado. Race Board: pit → fastest (7 s) → gap → voltas restantes → legenda; gap e voltas restantes 6 s (`gapHoldSeconds` padrão 6). Tabela de início de volta segue fora de escopo. Visibilidade por sessão: Board/Standings treino+corrida, Qualy Board/Standings classificação, Radar treino+corrida em perfis novos, Inputs em todas.

