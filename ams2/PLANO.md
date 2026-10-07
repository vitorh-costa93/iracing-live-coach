# App de overlay para Automobilista 2 — plano técnico

Estado: rascunho para aprovação. Base: levantamento do V3 (02/10/2026) e decisões do usuário nas conversas de mockup/fontes.

## 1. Escopo
- App novo e separado, em `ams2/`, com tudo que o Control Center do iRacing tem hoje (widgets, reordenar, escala, fonte, linhas visíveis, colunas, opacidade, perfis nomeados, editor de layout), adaptado aos dados do AMS2.
- Um tema visual por época da F1: **1998–2001**, **2004–2008**, **2018** (substituiu o 2010s; gráfico F1 2018–2021). O tema muda aparência (fontes, cores, formas), não a lógica dos widgets.
- Widgets do MVP: Standings, Relative, Fuel, Tyres, Weather, Inputs (gráfico de pedais). Radar e StartHelper depois.
- Fora do escopo do MVP: iRating, licença, SOF, P2P (não existem no AMS2).

## 2. Decisões já tomadas
| Tema | Decisão |
|---|---|
| Local | Pasta `ams2/` neste repositório; V3 não é alterado |
| 1998–2001 | Textos em Reddit Sans 800; números com `F1Broadcast98-Box.ttf` e `F1Broadcast98-Values.ttf` (já em `ams2/fonts/`); sombra preta nos valores (~2 px à direita e abaixo, ~60–65%) aplicada pelo renderizador |
| 2004–2008 | Open Sans Bold; velocímetro analógico com Inputs embutido |
| 2018 | Gráfico de TV F1 2018–2021 (ref. `reference/f1-2018-analysis.md`); fonte Verdana do sistema (a original é proprietária) |
| Mockups | `gpt-image-2.5-flare` em `medium`, referências do usuário em `ams2/reference/` |

## 3. Arquitetura proposta
Três projetos .NET 9 em `ams2/src/`, espelhando a divisão do V3 mas sem depender dele:

1. **Ams2.Core** (sem UI)
   - `SharedMemoryReader`: lê o mapa `$pcars2$` (`MemoryMappedFile`, somente leitura), com polling de ~60 Hz, detecção de conexão, versão da estrutura e contador de sequência para evitar leitura rasgada.
   - **Modelo neutro**: `SessionSnapshot`, `CarSnapshot`, `PlayerSnapshot` (records imutáveis). Widgets só conhecem o modelo, nunca a estrutura crua. Isso evita repetir o acoplamento do V3 (um leitor por widget, 1914 linhas misturando leitura e cálculo).
   - Cálculos puros e testáveis: gaps em tempo, Relative, combustível por volta e voltas restantes, desgaste, classe, estado de pit.
2. **Ams2.OverlayHost** (Windows, Direct2D via Vortice como no V3)
   - Janela transparente e click-through sobre o jogo (modo janela sem borda). Em tela cheia exclusiva não aparece.
   - **Motor de temas**: um tema é um pacote de tokens (fontes, cores, cantos, bordas, sombra) mais templates por widget. Os widgets são os mesmos para os três temas.
   - Fontes carregadas de arquivos (DirectWrite, coleção de fontes própria), para embutir Reddit Sans, Open Sans e as `F1Broadcast98-*`.
   - Um único provider de dados compartilhado por todos os widgets (o V3 abre um leitor por widget).
3. **Ams2.ControlCenter** (WPF)
   - Lista de widgets com arrastar para reordenar, ligar/desligar, escala, fonte, linhas visíveis, opacidade, colunas.
   - Editor de layout (arrastar e redimensionar, grade, encaixe, área segura), perfis nomeados, escolha de tema.
   - Fala com o OverlayHost por IPC (named pipe), como no V3.

Perfis em JSON por tema e por jogo (`%AppData%\ams2-live-coach\`), com versão de esquema.

## 4. Dados do AMS2 (a confirmar contra a estrutura real)
Conhecimento da estrutura de Shared Memory do Project CARS 2 que o AMS2 usa (versão 9). **Não verifiquei contra o header oficial nesta sessão; o primeiro passo é conferir.**
- Pilotos: até 64 `mParticipantInfo` (nome, posição, volta, distância na volta, posição no mundo, setor, melhor volta e última volta).
- Inputs: aceleração, freio, embreagem e volante "sem filtro", marcha, rotação, velocidade — alimentam o gráfico de Inputs.
- Pneus: temperatura, desgaste, composto, pressão por roda. Combustível: nível e capacidade.
- Clima: temperatura do ar e da pista, chuva, vento. Sessão: estado, tempo restante, voltas do evento, bandeira, modo de pit.
- **Não há `EstTime`/gap pronto.** O gap em tempo precisa ser calculado: guardar, por carro, o instante em que passou por cada faixa da pista (por exemplo a cada 10 m) e, para cada vizinho, comparar com o instante em que o jogador passa pela mesma faixa. É o mesmo princípio de um Relative por tabela de passagem.
- Ausentes: iRating, licença, SOF, P2P, país, ID de classe. Classe vem do nome da classe do carro.

## 5. Fases e critérios de aceite
| Fase | Entrega | Aceite |
|---|---|---|
| 0. Spike | Console que lê `$pcars2$` e imprime pilotos/velocidade/combustível | Valores batem com o HUD do jogo numa sessão real; confirmar a estrutura (versão e offsets) |
| 1. Core | Modelo neutro, cálculo de gaps/Relative/combustível, testes com um escritor falso de memória | `dotnet test` verde; Relative estável em corrida com 20+ carros |
| 2. Host + 1 tema | Janela transparente, fontes embutidas, widget Relative no tema 1998–2001 | Visual comparado ao mockup e à captura de referência |
| 3. Widgets | Standings, Fuel, Tyres, Weather, Inputs nos três temas | Cada tema revisado por widget (um recorte por widget) |
| 4. Control Center | Reordenar, escala, fonte, linhas, colunas, perfis, editor de layout | Toda a personalização do iRacing equivalente funcionando |
| 5. Acabamento | Publicação por script (`publish-ams2.ps1`), espelho no Desktop, ícone e logo | Instalação limpa abre e conecta ao jogo |

## 6. Riscos e perguntas em aberto
1. **Validar a estrutura da memória** (versão, offsets, tamanhos) antes de escrever o Core; um deslocamento errado invalida tudo.
2. **Gaps calculados** podem divergir do que o jogo mostra em pit/volta de largada; precisa de teste em sessão real.
3. **Licença das fontes construídas**: vêm de gráficos de TV de terceiros. Bom para uso pessoal; revisar antes de distribuir.
4. **Fonte do tema 2018**: Verdana do sistema (a original, Formula1 Display, é proprietária); ver `fonts/README.md`.
5. **Tela cheia exclusiva** não mostra overlay; documentar para o usuário.
6. **Regra do projeto**: nunca abrir o overlay enquanto o iRacing está aberto; vale também para testar com o AMS2 aberto (testar com escritor falso e logs).
7. Reuso futuro: se a duplicação com o V3 incomodar, extrair uma biblioteca de infraestrutura (render, IPC, layout) depois da Fase 3, com o que realmente se repetiu.

## 7. Próximo passo sugerido
Fase 0: criar `ams2/src/Ams2.Spike` (console) e conferir a estrutura contra uma sessão real do AMS2 com a opção de Shared Memory ligada. Isso exige o jogo aberto e um carro na pista; fora disso, eu preparo o escritor falso para validar o resto.
