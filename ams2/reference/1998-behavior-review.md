# Revisão 1998–2001 — 05/10/2026

Referências assistidas no Chrome, no mudo. Coordenadas do desenho em 1920×1080; rodapés em janela de 1920×300, origem (0,780).

| Widget | Referência e observação |
|---|---|
| Torre | [3:28–4:00](https://www.youtube.com/watch?v=AXIdNpq8uxs&t=208s): CLASSIFICATION antes das linhas; duas colunas de quatro posições, caixas amarelas, nomes brancos, diferenças amarelas sem sinal; páginas seguintes. |
| Setor | [14:18–14:28](https://www.youtube.com/watch?v=ix21M4CYmvQ&t=858s): frente à esquerda, atrás à direita, caixas externas grandes e barras internas; contador em décimos só depois da primeira passagem. |
| Legenda / Driver Caption | [4:18–4:22](https://www.youtube.com/watch?v=AXIdNpq8uxs&t=258s): faixa inferior, número ciano, nome branco, equipe ciana, fornecedor de pneus e posição amarela à direita. Patrocinador não fornecido pelo AMS2. |
| Voltas | [6:27–6:33](https://www.youtube.com/watch?v=ix21M4CYmvQ&t=387s): três voltas recentes, tempos dos dois pilotos em amarelo, LAP n branco ao centro; sem coluna de deltas. |
| Winner | [19:53–19:56](https://www.youtube.com/watch?v=ix21M4CYmvQ&t=1193s): legenda do vencedor, bandeira quadriculada e WINNER grande à direita. |
| Pit Timer | [16:16–16:50](https://www.youtube.com/watch?v=ix21M4CYmvQ&t=976s): nome à direita e PIT STOP / tempo amarelo abaixo. Cronometrar parado, não o deslocamento pela pit lane. |
| Quali Lap | [0:10 ao final](https://www.youtube.com/watch?v=Hh5CTgHddes&t=10s): jogador à direita, cronômetro em décimos; referência à esquerda perto das marcas, milésimos e delta na passagem; INTERMEDIATE / FINISH LINE. |
| Quali Tower | [41:17–41:22](https://www.youtube.com/watch?v=LEnATGb2BU0&t=2477s): título antes das linhas; revelação em ordem, duas colunas de quatro; sem relógio permanente no exemplo. |

## Contrato do Setor aprovado pelo usuário

Na passagem do jogador pelo setor anterior, escolher o carro imediatamente à frente ou atrás pela menor distância física longitudinal, interpolada nessa passagem. Travar esse par para o próximo setor. Frente escolhido: abrir quando ele passar pela próxima marca. Atrás escolhido: abrir quando o jogador passar. Congelar a diferença quando o segundo passar. Sem referência no primeiro marcador observado, sem tempo estimado antes da primeira passagem, sem troca oportunista durante o setor. Referência inválida (pit, identidade, desconexão, teleporte ou inversão do par): aguardar nova seleção. Se uma nova marca abrir antes do término da anterior, a janela mais recente assume a apresentação.

## Composição

Board de corrida concentra torre, setor, voltas, legenda, Pit Timer e Winner. Prioridade: vencedor, parada, torre, setor, voltas, legenda. Quali Board concentra torre e volta: torre na volta de saída; em cada volta rápida, torre entre 5 e 12 segundos (7 segundos), depois volta. Sem relógio observado não se fabrica início.

Os widgets independentes permanecem disponíveis. Padrões 1998 desativam Winner/Pit Timer e Quali Tower/Lap independentes para evitar repetição. Migração v4 move somente posições antigas padrão; formatos e fontes são preservados. Quali Board herda configurações compatíveis do Quali Lap anterior; posições personalizadas de classificação mantêm o compositor desligado.

## Animação e limites de evidência

O vídeo de classificação foi inspecionado quadro a quadro em aproximadamente 41:18–41:23: título, primeira linha, segunda linha e lista completa. Implementação: título 0,6 s, entrada de cada linha a intervalos de 0,45 s, fade de 0,15 s e páginas de 7 s. Corrida usa entrada de 0,16 s, respeitando também a passagem efetiva de cada carro. Esses valores são aproximações medidas da referência, não uma garantia de equivalência em todas as transmissões.

Validar estados e sequência de quadros em prévia/fake; teste no jogo permanece necessário. Quali Lap antecipa a referência por uma janela temporal de 5 s, pois seu modelo público não oferece distância às marcas. Referências parciais ausentes/inconsistentes ficam `--`. Velocidade disponível é a atual do jogador, não a velocidade de speed trap do vídeo; não inventar a velocidade do piloto de referência. Logos e patrocinadores históricos não são fornecidos pelo SDK.

## Tipografia e Qualy Board — 06/10/2026
Vídeo Hh5CTgHddes conferido no Chrome, no mudo: aproximadamente 25 s mostra referência à esquerda, delta ciano central e tempo do jogador à direita; aproximadamente 70 s troca a referência pela posição no resultado. Fontes menores e mais leves aplicadas à apresentação 1998. Referência de chegada permanece até completar a volta, inclusive quando mais lento, conforme requisito do usuário (esse caso não é demonstrado pela volta rápida do vídeo).

Todos os temas permitem escala do texto independente das dimensões, largura/altura separadas e tamanho/peso por função textual. Perfis anteriores à versão 6 migram preservando a aparência. Famílias incluídas receberam variantes de peso; os números desenhados de 1998 mantêm a fonte original por padrão e usam Barlow Semi Condensed quando há peso personalizado. Geometria não estica as letras. Prévia e IPC aplicam as mesmas configurações.

Capturas de teste validam referência persistente, delta central e posição no resultado. Teste no jogo permanece pendente; esta etapa não iniciou nem controlou o jogo.
