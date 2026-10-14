# AMS2 — transmissão 1999

Implementação solicitada em 14/10/2026 a partir de onze referências visuais fornecidas pelo usuário. O tema é exibido como **F1 1999**; o identificador persistido `f1-1998` permanece compatível. As famílias de fontes existentes foram preservadas; tamanhos e pesos foram ajustados.

## Widgets e sessões

- Qualy Board: classificação; out lap, relógio, aproximação, intermediária e resultado. Sem tabela de classificação ou paginação.
- Race Board: treino/corrida; legenda no treino, eventos transitórios na corrida.
- Inputs: todas as sessões padrão.
- Radar: treino/corrida, sujeito à proximidade e aos gates existentes.

Schema 7 remove widgets não pertencentes ao catálogo de 1999. Preserva geometria, personalizações de fontes e opções dos widgets mantidos. Outros temas continuam com seus próprios catálogos.

## Classificação

Out lap apresenta `FIRST LAP OUT OF PITS`, piloto/equipe da identidade existente e emblema de pneus quando disponível. Entre intermediárias, relógio próprio à direita. A aproximação usa referência disponível; a passagem congela tempo, delta e velocidade, com `INTERMEDIATE 1/2` e TAG Heuer. Fechamento apresenta posição, tempo, delta e velocidade na linha.

Referência padrão: melhor volta do líder; opção de comparação pessoal mantida. Tempos cumulativos e velocidades pertencem à mesma volta observada. A correção oficial da mesma volta reconcilia seu tempo final. Uma melhor volta não observada não reutiliza velocidades ou deltas de uma referência antiga. Dados ausentes permanecem ausentes.

Aproximação: até cinco segundos antes da referência; intermediária: três segundos; resultado: seis segundos, configurável em `showFor`.

## Corrida

Prioridade: pit parado → volta mais rápida → tabela de início de volta → gap periódico → voltas restantes → legenda própria com posição.

Tabela iniciada pelo avanço da volta corrente do jogador, paginada por oito pilotos/quatro segundos por padrão. A cada terceira volta, comparação medida na linha com o vizinho físico escolhido no marcador anterior; parênteses contêm o gap do marcador anterior do mesmo par, nunca tempos de voltas. A cada quinta volta, voltas restantes e líder com faixa SIEMENS. Gap e voltas restantes têm seis segundos visíveis; fastest lap tem sete. Eventos coincidentes ficam em fila. Ausência de cruzamentos válidos não fabrica comparação.

Pit timer aparece somente com `PitState.InPit` e velocidade inferior a 1 m/s. Menu, pausa, desconexão e troca de identidade reiniciam a apresentação. O modo de edição tem legenda própria de exemplo sem depender de corrida real.

## Validação

Testes de cálculos, perfis, migração, IPC e renderização com fonte de dados simulada. Capturas de todos os onze modos ficam fora do Git. Esta etapa não comprova disparos e medidas contra uma sessão real de 1999; essa validação continua pendente no jogo. A identificação automática de pintura e os assets pessoais usam o fluxo existente, anteriormente verificado com Senna/MP4/8.
