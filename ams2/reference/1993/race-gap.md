# Gap de corrida — contrato de 1993

Revisão do usuário em 07/10/2026: há uma placa de gap de corrida em 1993; o levantamento inicial de cinco cenas está incompleto. Incluir essa placa no tema, sem tratá-la como comparação a cada setor.

- Selecionar entre o carro fisicamente à frente e o fisicamente atrás aquele com menor distância ao jogador. Não usar gap para o líder nem posição no ranking para escolher o vizinho.
- Medir o intervalo em **um ponto da volta**, não em S1, S2 e chegada. A localização desse ponto ainda precisa de confirmação do usuário/trecho.
- Comparar a passagem dos dois carros pelo mesmo ponto. Se o vizinho está à frente, sua passagem inicia a medição interna; se está atrás, a passagem do jogador inicia a medição interna. O segundo cruzamento fecha o valor. A placa exibe o resultado fechado, sem presumir um contador visível de setor.
- Manter o mesmo par durante a medição. Não substituir o vizinho no meio da contagem; cancelar uma comparação invalidada por ultrapassagem, box, descontinuidade ou troca de identidade.
- Não apresentar intervalo calculado de velocidade instantânea como se fosse uma medição de passagem.
- A amostra física usada para escolher o mais próximo, o empate, a retenção do resultado, a entrada/saída e a composição gráfica ainda precisam ser definidos. Não copiar automaticamente o marcador anterior de setor do contrato 1998.

## Evidência visual recuperada — Mônaco

[Referência fornecida pelo usuário](https://www.youtube.com/watch?v=rLnIOkXCrSo&t=1283s), observada no navegador integrado, no mudo, em 07/10/2026. Em 21:18.417 a placa está ausente; em 21:20.594 já aparece completa. Em 21:22.927 e 21:23.417 mostra o mesmo resultado **12.668**, SCHUMACHER / posição 1 à esquerda e SENNA / posição 2 à direita. Também foi vista em reprodução por volta de 21:27; em 21:28.417 está ausente. Essas amostras delimitam entrada e saída, mas não certificam o quadro exato nem a curva de opacidade.

Faixa inferior cinza translúcida, miniaturas laterais dos carros, sobrenomes e posições brancos com sombra, gap amarelo central com três casas. Barra horizontal clara com seta verde apontando à direita. Significado da seta ainda não confirmado: não usá-la automaticamente para inferir aproximação/afastamento ou ganho do jogador. Os mesmos valores nas amostras sustentam uma placa de resultado fixo, não uma contagem contínua visível.

O vídeo confirma a composição; não prova o algoritmo de escolha do par, um marcador fixo da pista ou gatilho em todos os setores. A seleção do vizinho físico mais próximo é a regra de produto solicitada pelo usuário. Instante de seleção proposto: primeira passagem do par pelo marcador, preservando identidades até completar. Ponto do circuito e retenção final ainda pendentes de definição; empate deve manter a referência anterior válida, sem alternância.

Mockup `gap`: miniaturas originais demonstrativas; nenhum sprite/logotipo extraído da transmissão. Sequência de 2 s de espera / 7 s de resultado / 2 s sem placa é demonstrativa e não uma medição histórica. Runtime do aplicativo ainda não recebe um tema 1993 nesta etapa.

## Correções gerais de classificação solicitadas junto desta revisão

Todos os temas devem mostrar a primeira volta lançada desde o início, mesmo sem referência. Quando a referência ficar disponível durante a volta, incorporá-la sem interromper/reiniciar o cronômetro. A tabela aparece uma única passagem na volta de saída e troca para uma legenda de classificação com posição e gap para o líder; não reaparece entre 5 e 12 segundos de cada volta rápida. A prévia permite desmarcar/marcar a referência para demonstrar essa atualização.
