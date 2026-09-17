# Prompt de implementação — Live Coach Overlays — especificação consolidada v3

Construa um aplicativo Windows de overlays para iRacing, do zero, extraindo as funcionalidades válidas do projeto iracing-live-coach. O objetivo é obter a UI compacta dos mockups aprovados e alta responsividade, principalmente no Radar e no Start Helper. Entregue uma implementação funcional; não apenas uma reprodução estática.

Use os mockups multiclasse e SF23 anexados como referência visual. Este documento é a referência funcional: eventuais omissões, números incoerentes e rótulos incorretos nas imagens não devem virar comportamento do produto. Em particular, mantenha ΔiRating no Standings, comparação de última volta contra o jogador, marcas no Relative e todas as opções descritas abaixo mesmo que a imagem omita alguma delas.

## 1. Inspeção inicial e reaproveitamento

- Acesse o repositório vitorh-costa93/iracing-live-coach e identifique branch, commit e estado atual antes de alterar arquivos. Preserve trabalho existente não commitado.
- Inventarie as funcionalidades dos seis widgets, cálculos, fontes de telemetria, configurações e correções existentes. Classifique o que será reaproveitado, adaptado ou substituído.
- O usuário informou que P2P provavelmente foi resolvido no último commit. Inspecione a implementação atual e a versão efetiva da biblioteca SDK antes de concluir que há erro ou propor substituição.
- Verifique a semântica dos acessores de arrays por CarIdx: não multiplique índices por tamanho em bytes se a biblioteca já realiza essa operação. Valide diferentes índices, incluindo maiores que 15.
- Não replique limitações de layout da UI antiga. Preserve regras de negócio comprovadas e configurações úteis.
- Documente capacidades reais por carro e sessão, inclusive dados próprios, de adversários, ausentes, desatualizados e derivados.

## 2. Escopo fechado

Implemente somente: Standings, Relative, Weather Report, Fuel Calculator, Radar e Start Helper. Não inclua mapa, widget de inputs, resumo de sessão ou widget separado de delta.

Todos os widgets usam um único sistema visual e de componentes. SF23 e multiclasse não são interfaces independentes. A diferença estrutural principal é o agrupamento por classe no Standings. Campos como overtake aparecem condicionalmente conforme a capacidade do carro e a configuração.

## 3. Arquitetura

Adote como arquitetura-alvo:

- Core em C#: modelos de domínio, cálculos, capacidades, configurações e regras independentes da UI e do SDK.
- Adaptador de telemetria para o SDK do iRacing com snapshots consistentes, timestamp, identificação da sessão e validade por campo.
- Overlay Host próprio com renderização acelerada por GPU: Direct3D 11, Direct2D, DirectWrite e DirectComposition, utilizando bindings mantidos e compatíveis, após verificar a documentação e versões atuais.
- Control Center separado, podendo usar WPF para formulários e configuração. O caminho crítico de desenho dos overlays não depende dos controles WPF.
- Comunicação local tipada e versionada entre processos, com reconexão, validação e aplicação de configurações sem reiniciar a corrida.
- Motor de layout declarativo comum a todos os widgets: definição de colunas, medição de texto, padding, linhas, cabeçalhos de sessão e estilos. Não desenhe cada tabela com posições fixas espalhadas pelo código.
- Preview e overlay real devem compartilhar o mesmo motor de layout e renderização. Não mantenha uma segunda implementação visual divergente.
- Persistência local versionada, migrações, gravação atômica, importação/exportação e restauração de padrões.

Primeiro construa uma prova técnica de transparência, click-through, fontes, DPI, múltiplos monitores, pacing de frames e atualização de Radar/Start Helper. Confirme a viabilidade no Windows antes de expandir todos os widgets. Registre limitações reais, sem alegar que uma tecnologia por si só garante desempenho igual ao Kapps.

Separe aquisição, cálculos e apresentação. Renderize usando o estado válido mais recente, evitando filas crescentes e trabalho pesado na thread de renderização. Cacheie fontes, ícones, geometrias e textos estáticos. Refaça layout apenas quando conteúdo estrutural ou configuração exigir. Preveja device lost, desconexão do simulador, mudança de sessão e monitor removido.

## 4. Posicionamento completamente livre

Cada um dos seis widgets pode ser posicionado independentemente em qualquer ponto de qualquer monitor. A composição do mockup é apenas um preset inicial.

- Arrastar diretamente na tela no modo de edição e também no preview do painel.
- Editar X/Y, monitor, ponto de ancoragem, escala e dimensões aplicáveis por widget.
- Suportar coordenadas negativas no desktop virtual, diferentes DPI e restauração segura após mudança de resolução ou monitor.
- Alinhamento entre widgets, guias, grade e snap opcionais; nenhuma grade obrigatória.
- Bloqueio individual ou global de posição; ordem de sobreposição configurável.
- Modo corrida com click-through, sem roubar foco do iRacing. Atalho para entrar/sair do modo edição, exibindo limites e alças apenas durante edição.
- Fuel começa à esquerda do Relative, mas ambos são independentes. Ofereça vínculo opcional, desligado por padrão, para movê-los juntos com espaçamento configurável.
- Desfazer/refazer mudanças de layout, restaurar posição e recuperar widgets fora da tela.
- Salvar posição, tamanho, visibilidade e aparência por perfil. Fechar o painel não fecha os overlays.

Não comprima a fonte para caber numa largura. Redimensionar uma tabela deve respeitar a política explícita de colunas fixas/flexíveis e seus mínimos, sem distorção ou perda silenciosa de informação.

## 5. Design visual e tipografia

Reproduza o estilo de transmissão de automobilismo dos mockups: superfícies grafite translúcidas, alto contraste, linhas compactas, bordas discretas e pouco arredondamento. Evite cartões grandes, sombras intensas, títulos decorativos e espaços vazios excessivos.

Use a paleta normativa da seção 16. As cores são decisões de design, não amostras exatas das imagens geradas nem alegações de cores oficiais de terceiros. Centralize todos os tokens; não espalhe valores HEX pelo código.

Use Barlow Semi Condensed como fonte principal candidata; Rajdhani apenas como opção para números, se mantiver legibilidade e proporções naturais. Inclua recursos de fonte licenciados adequadamente e fallback. Não aplique scaleX/scaleY para estreitar ou alongar letras. Use numerais tabulares e alinhamento decimal quando aplicável.

Ponto de partida em 1080p, escala 100%: texto de linha 12–13 px, linha 22–24 px, padding horizontal 2–4 px por célula, bandeiras/emblemas 14–18 px. Esses valores são configuráveis e devem ser verificados em captura real, não considerados medidas exatas das imagens geradas.

Nome completo com primeiro e último nome por padrão. Reserve largura suficiente; ofereça abreviação/reticências explicitamente configuráveis. Não reduza automaticamente a fonte de cada nome. Símbolos de marca e bandeiras devem ter proporções corretas e recursos locais de qualidade; não deduza nacionalidade pelo nome nem trate clube/região como país sem fonte válida.

Sem títulos nos widgets durante a corrida: não escrever “Standings”, “Relative” ou “Fuel Calculator” como cabeçalho decorativo. Não exiba nomes de colunas em Standings e Relative no preset. Exiba somente os cabeçalhos de informações da sessão descritos na seção 15. Rótulos funcionais e unidades dos outros widgets permanecem quando necessários. O painel de controle usa os nomes dos widgets normalmente.

O centro da pista deve permanecer livre na composição inicial. Ofereça opacidade de fundo e de conteúdo separadamente, escala por widget e ajuste de altura de linha.

## 6. Standings

Colunas configuráveis: posição na classe/geral, faixa de cor da classe, número do carro, bandeira, marca, nome do piloto, licença e Safety Rating, iRating, ΔiRating estimado, gap, intervalo, última volta e comparação da última volta com a do jogador. Inclua estados relevantes como pit, desconectado e volta de diferença quando sustentados pelos dados.

- iRating e ΔiRating ficam no MESMO badge retangular de cantos arredondados, na MESMA linha: iRating à esquerda e ΔiRating à direita, por exemplo `3.694 +12`. Não empilhe os valores. Use fundo neutro, iRating claro, delta positivo verde e negativo vermelho. A licença/SR tem badge separado. O badge combinado é uma unidade de layout com largura configurável; não duplique o delta em outra coluna no preset.
- ΔiRating é estimativa de resultado, não valor oficial garantido; documente fórmula, elegibilidade e limitações. Não produza estimativas em sessões/categorias sem suporte válido.
- Última volta usa formato m:ss.sss, com precisão configurável.
- Delta de volta padrão = última volta válida do adversário menos última volta válida do jogador. Negativo significa adversário mais rápido; positivo, mais lento; jogador mostra 0.000. Ausência de volta válida gera “—”. Não confunda esse valor com gap de corrida.
- Gap de corrida e intervalo têm referência definida e configurável, com tratamento correto de classes e voltas de diferença.
- Multiclasse: cabeçalhos compactos de classe, ordenação das classes configurável, destaque da classe do jogador e limites independentes para a própria classe e as demais; permita ajuste por classe específica.
- SF23: tabela única, com os mesmos componentes e colunas. Overtake também pode ser habilitado como coluna opcional quando disponível.
- Top N fixo configurável por classe, combinado com janela em torno do jogador, sem duplicações. Top N conta dentro do limite total de linhas daquela classe; valide configurações incompatíveis.
- Com top N = 1 e cinco linhas da própria classe, mostre líder e mais quatro linhas da janela, incluindo o jogador. Use separador discreto quando houver posições omitidas. Cabeçalhos e separadores não contam como pilotos.
- Preset multiclasse: uma linha GTP e cinco GT3, assumindo jogador GT3. Preset SF23: cinco pilotos. Não fixe quantidade de fabricantes/carros; derive do grid e catálogo validado.

## 7. Relative e overtake/P2P

Relative mostra proximidade temporal em pista, não simplesmente uma segunda classificação. Trate volta de diferença, pit lane, carros parados e cruzamento da linha conforme regras documentadas.

Preset: sete pilotos, três acima, jogador e três abaixo. Número acima/abaixo e centralização configuráveis; não preencha ausências com pilotos fictícios. Inclua posição, faixa de cor da classe, número do carro, bandeira, marca, primeiro e último nome, licença/SR, iRating e gap relativo. Não inclua ΔiRating neste widget.

Cabeçalho superior configurável, distribuído por toda a largura: brake bias atual do jogador, temperatura da pista, emborrachamento quando disponível e relógio local do PC. Não reserve um rodapé vazio ou duplique esses dados na base da tabela. Esses dados podem coexistir com o Weather Report. Campos ausentes mostram “—” ou ficam ocultos conforme configuração; não invente nível de borracha a partir do tempo de sessão.

Overtake SF23:

- Exiba saldo em segundos, não porcentagem. Use 200 s como referência do preset solicitado; valide a semântica e o limite efetivo na integração, evitando extrapolar essa regra para outros carros.
- Separe saldo restante e estado: disponível, acionado, bloqueado/cooldown, esgotado, não suportado ou desconhecido.
- Visual aprovado: SOMENTE saldo em segundos e uma barra fina proporcional ao saldo, com cor indicando estado. Disponível = prata; acionado = verde; cooldown = âmbar, conforme seção 16. Sem palavras PRONTO/ATIVO/RECARGA, ícones, legendas ou contagem de cooldown na célula durante a corrida. A explicação das cores fica no painel de configuração.
- Use o saldo/limite validado para o comprimento da barra; nunca use cooldown como saldo. A barra pode representar uma proporção internamente, mas o texto sempre é em segundos. Saldo positivo não implica disponibilidade para acionamento.
- O estado permanece separado do saldo no modelo de dados. Esgotado mostra `0 s` e trilho vazio; desconhecido/não suportado mostra `—`, sem preencher uma barra que pareça válida.
- A coluna OT compartilha exatamente a altura das linhas, separadores e grade de todas as outras colunas. Sem contorno colorido em cada célula ou cartões independentes. Barra e número devem caber na linha configurada.
- Trate disponibilidade dos campos do jogador e dos adversários separadamente. Dados ausentes não viram zero, pronto ou estado estimado sem indicação.
- Preserve a correção atual de P2P se validada. Não conclua que o recurso falha com base em versões antigas do código.
- SF23 e multiclasse compartilham o mesmo Relative. OT é capacidade/coluna condicional, não um widget diferente.

## 8. Weather Report e relógio

Weather Report compacto com track wetness obrigatório, temperatura do ar e pista, vento/ direção e campos de chuva/umidade realmente disponíveis. Separe chuva atual e probabilidade/previsão: nunca rotule uma intensidade medida como probabilidade. Não fabrique previsão.

Mapeie valores/categorias oficiais de track wetness; não converta categorias em porcentagem sem fundamento. Suporte unidades configuráveis. Emborrachamento é um campo independente de umidade/molhamento.

Relógio nos cabeçalhos superiores: opção por widget, habilitada inicialmente no Relative e disponível no Standings, inclusive na faixa do nome da classe. Fonte é o relógio local do sistema operacional, respeitando fuso do PC; não hora virtual da sessão. Formatos HH:mm e HH:mm:ss, com segundos opcionais. Atualize só na frequência necessária. Rótulo curto “LOCAL”.

## 9. Fuel Calculator

Exiba combustível atual, consumo da última volta válida, média, valor conservador, autonomia, voltas/tempo restantes, combustível necessário para terminar, margem e quantidade a adicionar quando calculável. Dê destaque a combustível e autonomia, com métricas auxiliares menores.

Configure unidades, casas decimais, janela de média, modo conservador, margem em litros/voltas e quais campos aparecem. Defina tratamento de saída dos boxes, abastecimento, volta incompleta, reset de sessão e amostra insuficiente. Corridas por tempo exigem estimativa explícita das voltas restantes; não finja precisão absoluta.

Qualquer automação de abastecimento deve ser uma opção explícita, suportada e validada, nunca um efeito colateral do cálculo. O escopo mínimo é calcular e apresentar.

## 10. Radar

Audite a telemetria realmente disponível antes de definir a representação. Se houver somente estados de proximidade lateral, implemente indicador simbólico esquerda/direita/ambos e multiplicidade quando disponível. Não desenhe carros em coordenadas XY precisas nem distâncias laterais em metros a partir de dados insuficientes.

Posição e tamanho livres; cor, intensidade, orientação e ocultação quando livre configuráveis. Atualização prioritária e responsiva. Suavização visual opcional não pode atrasar alertas de ocupação ou inventar trajetória. Diferencie pista livre de telemetria desconectada/desconhecida.

## 11. Start Helper

Widget compacto para largada: RPM atual e faixa-alvo, embreagem atual e referência configurada/calibrada, estados de preparação e condições de largada quando disponíveis. Não invente luzes oficiais nem momento ideal a partir de temporizador arbitrário.

Perfis por carro, calibração, tolerâncias, cores e visibilidade automática configuráveis. Exiba na preparação, oculte após largada conforme regra/atraso configurado e ofereça preview forçado no painel. Deve responder na mesma arquitetura rápida do Radar, sem converter controles de inputs em outro widget.

## 12. Painel de controle completo

Painel grafite consistente com overlays, sidebar dos seis widgets com liga/desliga, preview e abas Layout, Cabeçalhos, Colunas, Aparência, Cores, Regras e Perfis. Alterações devem aparecer imediatamente no preview e no widget, sem reiniciar.

Por coluna:

- Ativar/ocultar, reordenar por arrastar ou teclado e restaurar padrão.
- Largura individual, largura mínima, modo fixo/flexível quando aplicável, alinhamento e padding.
- Casas decimais por campo numérico aplicável, formato de tempo/unidade e sinal explícito em deltas.
- Configuração de cabeçalho, cores condicionais e representação de dados ausentes.

Largura automática de tabela = soma das colunas visíveis + paddings + separadores + bordas. Alterar uma coluna redimensiona o widget de forma previsível; não estica fonte ou todas as outras colunas. Cabeçalhos de sessão devem se adaptar à largura resultante, respeitando a seção 15 e os limites físicos da seção 17. Não oculte campos selecionados silenciosamente. Nenhum controle pode ser meramente decorativo.

Por widget: posição, monitor, âncora, escala, visibilidade, opacidade, fonte, altura de linha, bordas, cabeçalho de sessão e regras por sessão. Para Standings, Top N, linhas por classe e janela do jogador; para Relative, linhas acima/abaixo, filtros de classe e OT; para os demais, todas as opções específicas anteriores.

Perfis global, por carro, classe/série e sessão quando identificáveis. Defina precedência explícita, ofereça override manual, duplicar, renomear, importar/exportar, desfazer, restaurar e salvar como. Evite mudança inesperada de layout durante a corrida. Inclua preview com dados fictícios claramente identificado como simulação, disponível sem iRacing aberto.

## 13. Performance e validade

Referência de teste: Ryzen 5 5500X3D, GTX 1660 6 GB, 16 GB RAM, monitor 1080p 165 Hz; iRacing em janela sem bordas. Compatibilidade com outros modos só deve ser anunciada após teste.

Renderização configurável com pacing consistente, incluindo 60/120 Hz e taxa do monitor quando viável. Frequência de desenho não equivale à frequência de dados do SDK. Não invente amostras nem prometa 165 leituras/s.

Meça latência entre recepção do snapshot e apresentação, frame times p50/p95/p99, CPU, GPU, memória, GC, perdas de atualização e impacto no FPS do jogo. Compare jogo sem overlay e com os seis widgets no mesmo cenário, incluindo chuva/grid cheio. Defina orçamento após prova técnica e reporte resultados reais; não declare equivalência com Kapps sem medição comparável.

Teste DPI 100/125/150%, mudança de resolução, monitor secundário, nomes longos, colunas mínimas, dados ausentes, reconexão, troca de classe/sessão, reabastecimento, OT ativo/cooldown/esgotado e CarIdx altos. Testes devem cobrir regras e riscos reais; logs técnicos ficam em diagnóstico, não poluem a UI durante a corrida.

## 14. Sequência e critérios de aceite

1. Inventário do código atual e mapa de telemetria/capacidades, incluindo último fix P2P.
2. Prova técnica GPU com texto nítido, transparência, posicionamento livre e Radar/Start Helper responsivos.
3. Motor compartilhado de layout, configuração e preview; sistema visual fiel às referências.
4. Standings e Relative com colunas totalmente editáveis, agrupamento, Top N e dados válidos.
5. Weather Report, Fuel, Radar e Start Helper completos.
6. Control Center, perfis, importação/exportação e recuperação de layout.
7. Validação visual e de performance, pacote executável e instruções de uso.

Aceite obrigatório: seis widgets independentes e livremente posicionáveis; nenhum título decorativo durante corrida; nomes completos; marca e bandeira com origem válida; ΔiRating somente no Standings; deltas de volta com referência correta; OT em segundos e barra colorida, com estado separado internamente; track wetness; relógio local; sete linhas no Relative do preset; 1 GTP + 5 GT3 no preset multiclasse; cinco SF23 no preset monoclasse; Top N sem exceder limite; Fuel inicialmente à esquerda com vínculo desligado; todas as larguras/ordens/precisões persistidas; preview e render real coerentes; ausência de dados não mascarada.

Entregue capturas reais do painel e dos overlays nos dois cenários, incluindo edição de posições e colunas. Distinga claramente mockup, preview simulado e captura em corrida. Liste limitações remanescentes com evidências e não declare testado em Windows/iRacing se esse ambiente não tiver sido utilizado.


## 15. Cabeçalhos, identidade e densidade visual — decisões finais

Estas regras substituem as composições inconsistentes de qualquer mockup. Não reproduza erros de dados, colunas omitidas ou cores por carro presentes nas imagens.

### Cabeçalhos de sessão

- Standings e Relative têm faixa superior compacta de informações, sem título decorativo e sem linha de nomes das colunas.
- No multiclasse, cada faixa de classe combina o nome da classe à esquerda e os itens selecionados no espaço restante, na MESMA linha. No monoclasse, use a mesma estrutura, por exemplo SF23 + volta + SoF + horário.
- Cada widget possui lista independente de itens ativados e ordem personalizada. No Standings, permita seleção comum às classes e override por classe; diferencie campos da sessão, da classe e do jogador.
- Catálogo: tipo/estado da sessão, volta atual/total, tempo restante, bandeira, SoF da classe/geral quando calculável, total de pilotos da classe/geral, incidentes do jogador/limite quando disponível, brake bias, temperatura do ar/pista, track wetness, emborrachamento e horário local do PC. Não invente campos sem fonte confiável.
- Meça o texto efetivamente renderizado. Distribua o espaço livre entre os itens com padding e separadores consistentes, ocupando a largura útil inteira. Não aumente arbitrariamente as fontes nem estique letras para preencher espaço.
- Recalcule a distribuição ao mudar largura, ordem, precisão, unidade ou conteúdo. Reserve espaço para os formatos esperados para evitar saltos a cada atualização numérica.
- Ofereça rótulos curtos e seleção dos itens. Se a soma mínima não couber, explique isso no painel e mantenha a última configuração válida; não ultrapasse o limite do widget, não corte valores e não esconda itens sem escolha explícita.
- Sem rodapé obrigatório no Standings/Relative. As informações antes colocadas no rodapé passam para o topo.

### Posição, classe e número do carro

- Uma faixa vertical fina, inicialmente 3 px físicos em 1080p, fica imediatamente junto à posição, em TODAS as linhas de Standings e Relative.
- A chave é o identificador de CLASSE da sessão. Não use fabricante, carro, equipe, posição, licença, país ou ordem das linhas para escolher essa cor.
- GTP: todas as faixas vermelhas, iguais à cor de destaque do cabeçalho GTP. GT3: todas amarelas, iguais ao cabeçalho GT3. SF23: todas vermelhas no preset monoclasse, como seu cabeçalho. Compartilhar vermelho entre dois presets separados é intencional; em sessões com classes distintas, mantenha distinção entre as classes presentes.
- Um único mapa `ClassId -> ClassColor` alimenta cabeçalho, faixa e legenda do painel. Alterar a cor de uma classe atualiza os dois widgets e todos os seus pilotos atomicamente.
- O destaque ciano do jogador não recolore nem cobre a faixa da classe. A licença tem seu próprio esquema; o emblema preserva sua arte original.
- Use posição de corrida real, não índice da linha exibida. Número do carro é um campo separado, com `#` por padrão, preservando zeros à esquerda quando existirem na origem. CarIdx não é número do carro.
- Ordem inicial: faixa/posição, número, bandeira, marca, primeiro e último nome, licença/SR, iRating, métricas. Permita reordenação das colunas; mantenha a faixa vinculada à célula de posição.

## 16. Paleta normativa em HEX

Valores RGB no formato `#RRGGBB`, com alpha informado separadamente. Alpha se aplica somente à superfície indicada; não reduza a opacidade do texto por herança. Os HEX são cores em sRGB. Recursos multicoloridos de marcas e bandeiras preservam as cores do arquivo original e não recebem tint global.

### Superfícies, texto e controles

| Token / aplicação | HEX | Alpha padrão |
|---|---|---|
| Fundo dos overlays | #101820 | 88% |
| Faixa do cabeçalho de sessão | #0B1219 | 94% |
| Fundo de badge iRating | #17232E | 100% |
| Borda do badge neutro | #536777 | 100% |
| Borda externa do widget | #405A6B | 85% |
| Grade horizontal/vertical | #2A3D4B | 70% |
| Texto principal / números | #F2F5F7 | 100% |
| Texto secundário / unidades / número do carro | #A6B0BB | 100% |
| Texto desabilitado / dados ausentes | #73808C | 100% |
| Texto sobre fundo claro/amarelo | #081018 | 100% |
| Destaque do jogador: texto/borda | #00C9E8 | 100% |
| Fundo da linha do jogador | #00C9E8 | 18% |
| Fundo do Control Center | #0B1118 | 100% |
| Superfície do painel / sidebar | #121D27 | 100% |
| Campo de formulário | #17232E | 100% |
| Borda de campo | #405A6B | 100% |
| Hover de controle | #203342 | 100% |
| Seleção de menu / aba | #173C52 | 100% |
| Ação primária / switch ligado | #008BFF | 100% |
| Hover de ação primária | #0075D6 | 100% |
| Switch desligado | #40515F | 100% |
| Botão circular do switch | #F2F5F7 | 100% |
| Foco de teclado / alças de edição | #00C9E8 | 100% |
| Fundo de tooltip / mensagem | #17232E | 100% |

Sem zebra por padrão; fundo das linhas comuns transparente sobre o fundo do widget. Sem sombra/glow por padrão. Arredondamento inicial: widget 4 px; badge 3 px; não usar cápsulas exageradas. Contornos e grade com espessura visual de 1 px físico no preset.

### Classes, licenças e semântica

| Aplicação | HEX | Regra |
|---|---|---|
| Classe GTP | #FF3038 | Cabeçalho e TODAS as faixas GTP |
| Classe GT3 | #FFD400 | Cabeçalho e TODAS as faixas GT3 |
| Classe SF23 | #FF3038 | Cabeçalho e TODAS as faixas SF23 |
| Classe LMP2, se presente | #5B8CFF | Cor adicional do catálogo |
| Outras classes | #A78BFA, #FF8A3D, #2DD4BF, #F472B6 | Atribuição estável por classe; resolver colisões no perfil |
| Classe não identificada | #73808C | Não inferir pela marca |
| Licença Rookie | #B91C1C | Texto #F2F5F7 |
| Licença D | #F28C28 | Texto #081018 |
| Licença C | #FFD400 | Texto #081018 |
| Licença B | #168A45 | Texto #F2F5F7 |
| Licença A | #0057D9 | Texto #F2F5F7 |
| Licença Pro, quando aplicável | #1B1B1B | Texto #F2F5F7; borda #A6B0BB |
| Licença desconhecida | #40515F | Texto #F2F5F7 |
| Ganho de iRating / margem positiva | #32D583 | Sempre acompanhado de sinal/valor |
| Perda de iRating / margem negativa | #FF5252 | Sempre acompanhado de sinal/valor |
| Delta de última volta mais rápido | #32D583 | Delta negativo contra jogador |
| Delta de última volta mais lento | #FF5252 | Delta positivo contra jogador |
| Delta zero / gap normal | #F2F5F7 | Cor neutra |
| Melhor volta da sessão | #C084FC | Apenas com referência válida |
| Alerta / baixo combustível / radar ocupado | #FFCC00 | Conforme regra configurada |
| Crítico / radar em situação crítica validada | #FF5252 | Não inferir severidade sem dados |
| Informação / referência do Start Helper | #00C9E8 | Não confundir com alerta |
| Start Helper dentro da faixa | #32D583 | Conforme alvo calibrado |
| Start Helper fora da faixa | #FFCC00 | Limites configurados |
| Start Helper limite crítico | #FF5252 | Somente regra validada |
| Trilho vazio de barras | #253440 | Não implica saldo válido |
| Borda das barras | #536777 | Grade externa continua uniforme |
| OT disponível | #B8C4D0 | Saldo em segundos + barra prata |
| OT acionado | #22E66B | Saldo em segundos + barra verde |
| OT bloqueado/cooldown | #FFBF00 | Saldo em segundos + barra âmbar |
| OT esgotado | #73808C | 0 s; trilho vazio |
| OT desconhecido/não suportado | #73808C | Travessão; sem saldo fictício |
| Tempo seco / ícone de sol | #FFD400 | Track wetness mantém rótulo explícito |
| Chuva / molhado / ícone de água | #38BDF8 | Não transformar condição em previsão |
| Bandeira verde | #22C55E | Mostrar estado verdadeiro |
| Bandeira amarela | #FFD400 | Mostrar estado verdadeiro |
| Bandeira vermelha | #FF3038 | Mostrar estado verdadeiro |
| Bandeira azul | #3B82F6 | Mostrar estado verdadeiro |
| Bandeira branca | #F2F5F7 | Mostrar estado verdadeiro |
| Bandeira preta | #101010 | Borda clara para contraste |
| Quadriculada | #F2F5F7 + #101010 | Padrão bicolor, não cor única |

Licenças e cores de estado acima são o tema inicial do aplicativo, não uma reprodução declarada de códigos oficiais. Permita personalização e restauração por token, paleta de classe e perfil. Legendas de OT ficam no painel, não nas células. Cor de gap não deve implicar automaticamente que estar à frente/atrás é bom ou ruim: use sinal e texto neutro no preset.

## 17. Limites físicos e layout responsivo obrigatório

- Em 1920×1080, NENHUM widget ultrapassa 480 px físicos de largura (25% da tela). Um widget exibindo sete pilotos não ultrapassa 378 px físicos de altura (35%), incluindo cabeçalhos, separadores, bordas e qualquer rodapé ativado.
- Em outras resoluções, use o mesmo limite percentual da área-alvo selecionada. Nunca use a largura agregada de vários monitores para aumentar um widget num único monitor. Distinguir dimensão de viewport e dimensão do monitor nas configurações.
- Estes são tetos, não dimensões-alvo. Sete linhas de 24 px + cabeçalho de 24 px podem ocupar aproximadamente 194 px com bordas; não acrescente espaço vazio até 378 px.
- Valide após conversão de DIP para pixel físico e após escala do widget. Em 150% de DPI, 480 DIPs NÃO equivalem a 480 px físicos. Limite = floor(0,25 × largura física da área-alvo); altura máxima para sete pilotos = floor(0,35 × altura física).
- Alterar coluna, fonte, DPI, perfil, número de linhas, dados ou cabeçalho nunca pode ultrapassar silenciosamente o limite. Use medição real de texto e composição na dimensão final.
- Larguras individuais continuam livres dentro do orçamento disponível. Ajuste apenas colunas explicitamente flexíveis até seus mínimos. Caso ainda não caiba, rejeite a configuração excedente no painel, mostre quantos pixels faltam e ofereça reduzir padding/fonte global dentro do mínimo legível ou desativar um campo por escolha do usuário. Mantenha a última configuração válida em corrida.
- Não resolva excedentes deformando a tipografia, reduzindo cada nome separadamente, removendo informações obrigatórias silenciosamente ou rasterizando a tabela inteira e encolhendo como uma fotografia.
- Primeiro e último nome por padrão; nome excepcionalmente longo pode usar reticências conforme opção explícita. Não substitua todos por iniciais sem autorização.
- Configure presets completos dentro de 480 px com fonte regular/semi-condensada legível: comece em 12 px físicos para corpo, 11 px para metadados, 22–24 px por linha. Valide no monitor a 100%, não apenas ampliando capturas. Se os campos não couberem legivelmente, reporte o conflito com medidas e proponha opções sem alegar aceite.
- O painel exibe largura/altura físicas finais, percentuais, limite e largura disponível para colunas. Preview inclui modo 1920×1080 em escala real e opção de enquadrar sem alterar o layout original.
- Adicione verificações de geometria para todos os presets: 1 GTP + 5 GT3, 5 SF23 e Relative 7 linhas. Teste também fonte, DPI, números longos e nomes longos. Nenhum widget pode cobrir o centro da pista no preset inicial.

## 18. Emblemas, bandeiras e ícones

Reutilize PRIMEIRO os recursos e o mapeamento já presentes no Live Coach atual. Esta especificação não presume que os arquivos ou seus formatos tenham sido auditados nesta conversa: faça essa inspeção no repositório e registre caminhos reais, resolução, transparência, origem e problemas encontrados.

1. Inventarie emblemas de fabricantes, bandeiras, ícones e catálogo de carros; aproveite arquivos bons e a lógica de resolução existente. Não crie um catálogo paralelo incompatível.
2. Resolva `CarId -> fabricante -> asset` usando identificadores estáveis do catálogo/SDK validado. A marca não define a cor da classe. Um mesmo fabricante em GT3 e GTP usa o emblema correto, mas recebe a faixa da sua classe.
3. Prefira fonte vetorial existente e fiel (SVG/geometria vetorial compatível). Normalize viewBox, limites e padding sem redesenhar a marca. Rasterize vetores em recursos de GPU no tamanho físico requerido e cacheie quando necessário.
4. Para bitmap existente, preserve PNG com transparência e qualidade suficiente. Use variantes em resolução maior quando disponíveis e reduza com filtragem adequada. Não amplie miniaturas pixeladas nem utilize JPEG com fundo. Não vetorize automaticamente um emblema ruim inventando detalhes.
5. Caixa inicial de emblema 16×16 px físicos, configurável; ajuste proporcional `contain`, centralizado verticalmente, sem deformar ou recortar. Bandeira em caixa proporcional, por exemplo 18×12 px. Normalização óptica de padding por marca é permitida para equilibrar o tamanho aparente.
6. Preserve cores oficiais do recurso, vazados, contornos e transparência. Não aplique a cor da classe ou ciano do jogador ao emblema. Use variante clara/escura existente quando necessária para contraste; não altere a identidade da marca.
7. Não use emoji, letras substitutas, desenhos por IA ou aproximação geométrica como emblema final. Se faltar um recurso, use placeholder neutro local e registre a ausência. Não associe uma marca incorreta para preencher o espaço.
8. Bandeira depende de nacionalidade/código de país confiável. País ausente usa placeholder; não inferir por nome, equipe ou clube sem mapeamento validado.
9. Carregue e decodifique fora do loop de renderização; cache por asset, tamanho físico, DPI e variante. Recrie recursos dependentes da GPU após device lost. Não faça downloads durante a corrida.
10. Reutilize os mesmos recursos e medidas no preview e overlay. Valide fabricantes efetivamente presentes no grid, sem fixar número de carros por classe.

## 19. Antialiasing, nitidez e composição

Antialiasing é requisito de aceite, não somente uma opção visual marcada no painel.

- Texto com DirectWrite e antialiasing em escala de cinza nas superfícies transparentes do overlay. Não force ClearType sobre transparência: isso pode produzir resultados imprevisíveis. No painel opaco, o renderer pode usar o modo apropriado ao alvo. Base: [Microsoft — Alpha modes e ClearType](https://learn.microsoft.com/en-us/windows/win32/api/dcommon/ne-dcommon-d2d1_alpha_mode).
- Geometrias vetoriais, curvas, cantos arredondados e ícones desenhados com `D2D1_ANTIALIAS_MODE_PER_PRIMITIVE`. Não deixe o renderer globalmente em modo aliased. Base: [Microsoft — Direct2D antialias modes](https://learn.microsoft.com/en-us/windows/win32/api/d2d1/ne-d2d1-d2d1_antialias_mode).
- Configure um caminho de composição com alpha premultiplicado consistente para superfícies/bitmaps que o exijam. Não multiplique alpha duas vezes. Os valores fornecidos aos brushes Direct2D continuam em straight alpha; respeite a conversão feita pela API. Evite halos pretos/brancos nas bordas transparentes. Base: [Microsoft — D2D1_ALPHA_MODE](https://learn.microsoft.com/en-us/windows/win32/api/dcommon/ne-dcommon-d2d1_alpha_mode).
- Aplicação consciente de DPI por monitor; recrie targets e caches no tamanho físico correto ao trocar DPI/monitor. Não deixe o Windows esticar uma imagem de baixa resolução. Base: [Microsoft — Direct2D e High-DPI](https://learn.microsoft.com/en-us/windows/win32/direct2d/direct2d-and-high-dpi).
- Para grade horizontal/vertical, alinhe os limites efetivos ao pixel físico para evitar linhas borradas, duplas ou irregulares. Considere espessura e transformação: não aplique um deslocamento universal de 0,5 DIP a tudo.
- Texto deve ser medido e desenhado na escala final com a mesma fonte, peso e parâmetros. Não transforme bitmaps de texto a cada resize. Badge não deve cortar glifos, sinais, acentos ou descendentes.
- Bitmaps usam filtragem linear ou redução de alta qualidade suportada pelo backend; nunca nearest-neighbor para logos, bandeiras ou curvas. Se um recurso ficar ruim na redução, gere uma variante adequada àquele tamanho a partir do original de qualidade.
- Não adicione FXAA/TAA ou blur global para mascarar serrilhado de UI. Não imponha supersampling de todo o desktop: escolha qualidade por recurso e meça custo. Antialiasing de texto, geometrias e amostragem de imagem são responsabilidades distintas.
- Cores/alpha e ordem de composição devem ser equivalentes entre preview e tela. Fundo claro, escuro, céu, asfalto e chuva precisam manter boa leitura sem franjas coloridas.
- Verifique capturas PNG sem compressão destrutiva em 100%, 125% e 150% de DPI, e escalas de widget suportadas. Inspecione em tamanho real e ampliação para diagnóstico: letras, diagonais, emblemas de curvas finas, bandeiras, bordas de badges e barras OT. Repita durante movimento para detectar cintilação.
- Entregue evidência de antialiasing configurado no código e capturas reais; não declare ausência absoluta de serrilhado em qualquer escala com base apenas no mockup. Corrija artefatos perceptíveis nas escalas homologadas antes de aceitar.

## 20. Checklist final consolidado

- [ ] Seis widgets, mesmo sistema visual, posições livres, Fuel inicialmente à esquerda do Relative com vínculo desligado.
- [ ] Máximo 25% da largura por widget; sete pilotos no máximo 35% da altura, já considerando DPI, escala e bordas.
- [ ] Sem títulos decorativos e sem nomes de colunas nos presets Standings/Relative.
- [ ] Cabeçalhos de sessão selecionáveis/reordenáveis; informações distribuídas pela largura; classe e informações na mesma faixa no multiclasse.
- [ ] Posição real, número do carro separado e faixa de classe vinculada ao mesmo HEX do cabeçalho, inclusive no jogador.
- [ ] Primeiro/último nome, bandeira, emblema e licença/SR válidos. Recursos do Live Coach reaproveitados e mapeamentos auditados.
- [ ] Standings: iRating e delta lado a lado no mesmo badge; Relative: iRating sem delta.
- [ ] Gap de corrida e delta de última volta preservados como campos diferentes; não copiar valores inconsistentes dos mockups.
- [ ] OT com saldo em segundos e barra colorida, sem texto de estado; grade idêntica às demais colunas; dados indisponíveis tratados corretamente.
- [ ] Track wetness, relógio local no cabeçalho, Fuel com amostragem válida, Radar sem posição fictícia, Start Helper calibrável.
- [ ] Todas as cores HEX centralizadas e configuráveis; classe independente de marca, licença e destaque do jogador.
- [ ] Colunas e cabeçalhos com largura, ordem, precisão, unidades e visibilidade persistidas, respeitando limites físicos.
- [ ] Antialiasing de texto/geometria, escala correta de imagens e alpha consistente; QA real em diferentes DPI.
- [ ] Build executável, benchmarks reais no hardware de referência, capturas 1920×1080 da corrida inteira e capturas separadas do painel. Não montar o painel ocupando parte da captura usada para avaliar proporções dos widgets.

Implemente por etapas sem perder os requisitos anteriores. Faça as decisões de engenharia necessárias e documente suas evidências. Não substitua trabalho verificável por promessa de fidelidade ou performance.
