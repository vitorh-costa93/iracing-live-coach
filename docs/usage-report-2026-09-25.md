# Relatório de uso de tokens e modelos (25/09/2026)

Fonte: transcritos das sessões do projeto em `~/.claude/projects/...` (mensagens do assistente, sem duplicatas por id) e o uso do plano no momento da leitura. O corte "antes/depois" é 25/09 às 09:08, quando `CLAUDE.md` global e o agente `monitor-reader` foram criados.

## Plano agora
- Plano Pro. Limite de 5 h: 23% usado (renova em 4 h 6 min). Semanal: 40% usado (renova em 5 d 18 h).
- Uso extra desativado; já foram gastos R$ 138,29 em uso extra.
- Esta sessão: 225 mil tokens de contexto (22% da janela de 1 M). Mensagens = 164 mil, ferramentas e MCP = 49 mil.

## Modelos mais usados
| Onde | Modelo | Mensagens | Observação |
|---|---|---|---|
| Conversa principal (24/08 a 25/09) | Sonnet | 23.656 (98,7%) | 15,3 M tokens de saída |
| Conversa principal | Opus | 309 (1,3%) | 0,25 M tokens de saída |
| Subagentes | Opus 5.5 | 507 | maior consumidor entre os subagentes: 76 mil de saída, 110 M de cache lido |
| Subagentes | Sonnet 5 | 218 | 22 mil de saída |
| Subagentes | Haiku 4.5 | 16 | quase não usado |

O que domina o custo é a **leitura de cache** (12,4 bilhões de tokens no total, contra 15,5 milhões de saída), ou seja, sessões longas relendo contexto grande a cada mensagem. Os dias mais pesados foram 29/08, 11/09, 14/09 e 18/09 (1,0 a 1,4 bilhão de cache lido por dia).

## Antes e depois das mudanças de hoje
| | 24/09 09:00 a 25/09 09:08 | Depois de 25/09 09:08 |
|---|---|---|
| Mensagens (principal) | 199 em 25,6 h (8/h) | 89 em 7,6 h (12/h) |
| Tokens de saída por hora | 5.060 | 7.394 |
| Cache escrito por hora | 87.900 | 46.500 (-47%) |
| Cache lido por mensagem | ~519 mil (média de todo o histórico) | ~197 mil |
| Subagentes usados | vários (a maioria Opus) | **nenhum** |

## Leitura honesta
- **Não dá para concluir que as regras reduziram o consumo.** A amostra "depois" é pequena (89 mensagens) e mistura outro tipo de trabalho. A queda de cache escrito e de cache lido por mensagem acompanha principalmente uma sessão nova, com contexto menor, não necessariamente a regra.
- **A delegação a Haiku/Sonnet ainda não foi exercitada:** desde as 09:08 nenhum subagente foi disparado, então não há como medir o efeito da regra de modelo por subagente. O histórico anterior mostra o oposto do que a regra pede: a maioria dos subagentes rodou em Opus.
- **Maior alavanca para poupar:** a conversa principal. Sessões que passam de centenas de milhares de tokens pagam a releitura do contexto em toda mensagem. Fechar a etapa e começar sessão nova (`/clear`) custa menos que continuar.
- Esta sessão está em 225 mil tokens; um bom ponto para `/clear` é depois de fechar este relatório.

## Limitações
- Os transcritos não trazem custo em reais, só tokens; a conversão para custo depende do preço de cada modelo.
- Os números do plano são do momento da leitura, não do histórico.
- Contagem de subagentes: só os transcritos existentes na pasta de cada sessão.
