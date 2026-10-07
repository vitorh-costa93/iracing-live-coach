# Referências 2004–2008 — primeira etapa

Pesquisa em 05/10/2026. Vídeos observados no mudo, um por vez, pelo navegador integrado: Chrome não estava disponível após o reinício. Tempos abaixo pertencem ao vídeo, não ao relógio da corrida. Não houve teste no jogo.

## Acervo localizado e evidência observada

| Ano | Transmissão | Trecho observado / utilidade |
| --- | --- | --- |
| 2004 | [Brasil, completo](https://www.youtube.com/watch?v=uuV55dS3hDs) | 20:16: contador branco 64/71 no alto. |
| 2004 | [Spa, melhores momentos oficiais F1](https://www.youtube.com/watch?v=PCFv0KZ2VpU) | 4:37: torre inferior esquerda, uma coluna com posições 9–12, sobrenomes pretos em células brancas e gaps brancos em células pretas, sem sinal +. Contador 38/44. 4:52: identificação simples Trulli em faixa branca; contador 34/44. |
| 2005 | [Austrália, completo](https://www.youtube.com/watch?v=xpEyqXowMxY) | Localizado nos resultados; ainda não analisado. |
| 2005 | [San Marino, melhores momentos oficiais F1](https://www.youtube.com/watch?v=M1EDiybhT7E) | 5:22: contador 38/62; identificação simples Button em faixa branca durante parada. Não confundir essa faixa com a placa completa de piloto. |
| 2006 | [Malásia, completo](https://www.youtube.com/watch?v=ven1DuiNkgM) | Cerca de 30:10: contador branco 41/56. |
| 2007 | [Japão/Fuji, completo](https://www.youtube.com/watch?v=tfZWDX2P-wg) | Cerca de 40:22: imagem onboard na chuva, sem placa de tempos nesse quadro. Fonte disponível para procurar eventos específicos; nenhum comportamento deduzido desse quadro. |
| 2008 | [Austrália, completo](https://www.youtube.com/watch?v=BaMnuJzrVn4) | 40:05: contador branco 36/58. |
| 2008 | [Japão, volta Q1 de Alonso](https://www.youtube.com/watch?v=AZY3_fHe624) | 0:46–0:51: torre superior esquerda com líder HAM/1:18.232 e posições 11–20; posições de eliminação em vermelho. Relógio Q1 separado no alto. Barra inferior com F Alonso alinhado à direita, cronômetro 38.4 → 43.4 e referência 1/49.998 no quadro posterior. |

## Ajustes implementados nesta etapa

- Board/Torre: primeira entrega interpretou o quadro isolado como uma coluna fixa; essa interpretação foi corrigida na terceira etapa abaixo. Estado atual: duas colunas de quatro linhas, preenchidas progressivamente pelas passagens reais, sobrenomes pretos e gaps sem + por padrão.
- Projeção que substituía a primeira coluna pela segunda foi removida junto com seus testes obsoletos. A apresentação mantém a página real de oito carros do Core e a janela de tempo existente.
- Contador do tema 2004–2008: total menos voltas completas do líder, inclusive zero ao terminar; outros temas mantêm a volta atual. Corridas por tempo mantêm Lap N.

## Limites e próximos widgets

Fidelidade integral não está concluída. Fade/slide e duração exata não foram medidos quadro a quadro; mantidos por enquanto. Setor, comparativo de voltas, legenda completa, Winner e Pit Timer precisam de trechos com início, atualização e saída visíveis. A identificação simples observada é uma variante distinta da placa completa existente.

Classificação deve considerar as mudanças de regulamento dentro do período: a referência Q1 de 2008 não prova o formato de 2004–2005. Próxima etapa: selecionar trechos de classificação de ambos os formatos e revisar Quali Tower/Lap, inclusive relógio separado e alinhamento das barras. A regra de referência física do Setor definida pelo usuário permanece requisito para a revisão desse widget; não foi aplicada ao tema 2004 nesta etapa.

Validação: Core 171, Shared 140 e integração sem GPU 7 testes aprovados. Build/publicação dos dois executáveis pelo script AMS2. Sem suíte gráfica completa, sem overlay sobre jogo; restrição após congelamento anterior, cuja causa continua desconhecida.

## Segunda etapa — Quali Lap

Na mesma referência Q1 Japão 2008, quadros adicionais observados no mudo:
- 1:11: nome F Alonso, cronômetro 1:02.8, sem faixa colorida de setores.
- 1:26: cronômetro 1:17.8 e referência inferior [1 vermelho][1:18.232 preto].
- 1:31: resultado 1:18.917, posição obtida 7 em caixa cinza à direita das duas primeiras linhas; comparação inferior [1 vermelho][+0.685 laranja]. A posição inferior identifica a referência, não o resultado do piloto.

Quali Lap 2004 corrigido: posição obtida separada da referência; removida posição final projetada a partir de delta parcial. Referência preta antes da marca usa somente tempos confiáveis; janela de aproximação de 5 s é decisão de implementação, ainda não medição exata do vídeo. Sem dados confiáveis, não aparece referência estimada. Faixa de setores desativada por padrão, opção explícita preservada. Largura de projeto passa de 278 para 350 para comportar a posição final; localização personalizada mantida.

Testes: Core174, Shared140, Integration7 sem GPU aprovados. Publicação dos dois executáveis e conferência do espelho Desktop. Não realizada renderização gráfica nem validação no jogo; animação ainda não certificada. Próxima etapa: Quali Tower/relógio separado e Setor 2004, além dos demais widgets de corrida listados acima.

## Terceira etapa — auditoria de todos os widgets e animações

Escopo: os 16 widgets do catálogo f1-2004 e os quatro modos do Board. 1998/2018 preservados. Fontes assistidas no mudo, uma por vez. No Spa 2004, 4:38.37 e 4:40.77 mostram posições9–12 à esquerda; 4:43.17 acrescenta13–15 à direita mantendo a primeira coluna. Isso confirma montagem progressiva em duas colunas, em vez de troca de páginas de quatro. No Q1 Japão2008, ~1:27.4 mantém cronômetro/referência e ~1:28.4 já mostra resultado/posição/delta sem mover toda a placa. Busca no Brasil2004 cobriu também os quadros de Setor em ~1:30:21 e os minutos finais, sem encontrar sequência completa da entrada/saída de Winner nesses pontos; não inferir duração de Winner desses quadros.

| Widget/modo | Revisão e estado atual |
| --- | --- |
| Board/Torre | Duas colunas com intervalo28; cada passagem revela sua linha. Coluna esquerda não reinicia ao chegar a direita. Mantido slide horizontal curto por linha; duração ainda aproximada. |
| Board/Setor | Contrato físico solicitado conectado à apresentação2004; gap legado estimado não abre a placa. Identidade estável da janela mantém atualização imediata mesmo quando a segunda passagem refina o horário interpolado. |
| Board/Voltas e Legenda | Trocas sequenciais: saída antiga antes de entrada nova, sem sobreposição das duas placas nem slide vertical. Atualização de dados dentro da mesma placa preserva sua entrada. |
| Driver Caption | Transição curta; evento novo durante placa visível prolonga a janela sem reiniciar entrada. |
| Pit Timer | Entrada curta desde início da parada, contador contínuo; saída do carro mantém a placa sem apagar/reentrar; desaparece ao fim da janela após parada. |
| Winner | Entrada/saída curtas, janela10s preservada. Sem medida exata da transmissão para essa duração. |
| Pit Stops | Entrada/saída curtas; janela8s preservada. |
| Quali Lap | Cronômetro/tempo/delta mudam sem reiniciar a placa; posição final entra separadamente. Comparação negativa fica verde; positiva/zero laranja, para líder ou melhor pessoal. Resultado inválido não fica verde. |
| Quali Result | Entrada/saída curtas; extensão do resultado não reinicia entrada. |
| Quali Tower | Atualizações diretas de posição/relógio preservadas; nos quadros consultados a lista muda sem deslocamento da torre inteira. Entrada/saída completa não medida; relógio ainda integra a janela da torre. |
| Lap Counter | Atualização direta de voltas restantes preservada, sem efeito que atrase o novo número. |
| Standings e Relative | Atualização direta de posições/gaps preservada; não acrescentado movimento sem evidência da transmissão. |
| Fuel, Tyres e Weather | Painéis auxiliares de telemetria; leitura/desenho revisados e renderização validada. Sem equivalente direto nas referências históricas; comportamento preservado. |
| Inputs | Atualização contínua dos pedais/velocímetro preservada, sem transições sobre a telemetria. |
| Radar | Transições próprias de proximidade preservadas; função auxiliar AMS2, não equivalência com gráfico F1 da época. |

Durações .12s de saída e .16s de entrada das placas são aproximações de apresentação. Não há certificação de equivalência quadro a quadro: buffering e cortes dos vídeos impediram medir todas as entradas/saídas. Classificação2004–2005 e relógio separado ainda exigem revisão específica de formato/layout.

Validação final: Core186, Shared140, Integration10 focados aprovados (incluem3 testes gráficos serializados, um dispositivo por teste, sem janelas/jogo). Cobertura gráfica de todos os widgets do tema; comparação de pixels confirmou continuidade das colunas e verde/laranja na parcial e resultado. Revisão independente encontrou reinício de animação na reinterpolação do Setor; corrigido e coberto por passagem real no simulador de teste. Builds/publicação Release Host e Control Center, hashes Desktop conferidos, overlay reaberto por autorização do usuário. Não executada suíte completa de integração nem validação em sessão real AMS2.

### Inputs e radar — revisão de fluidez (06/10/2026)
O tema 2004–2008 possui agora widgets independentes Velocímetro (`inputs`) e Inputs Graph (`inputgraph`). A migração preserva os ajustes do gráfico anterior e sua posição visual. O desenho acompanha o monitor, separado do provider de60Hz; as entradas usam sampler dedicado (teto240Hz, somente sequências novas). A comparação com StartHelper/Radar do iRacing confirmou a importância de evitar trabalho pesado por leitura e desenhar o último estado disponível. No AMS2, o painel já extrapolava movimento entre leituras; o radar nativo passou a extrapolar também a distância lateral, no máximo50ms, mantendo a condição real de carro ao lado.

Medição fake Release por3s, monitor165Hz, sem jogo: Inputs Graph165fps/p95 6,4ms/max6,5ms; Radar165fps/p95 6,4ms/max6,7ms. Amostragem167/s; provider60/s. A medição não demonstra Hz reais do SDK nem desempenho com GPU ocupada pelo jogo.
