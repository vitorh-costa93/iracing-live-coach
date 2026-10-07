# Análise dos overlays da transmissão FORMULA 1 – GP da China 2006
Fonte: vídeo oficial "Extended Race Highlights | 2006 Chinese Grand Prix" (canal FORMULA 1, https://www.youtube.com/watch?v=jM3XpyX-0cQ, 23 min), analisado quadro a quadro em 02/10/2026. Imagem 4:3 (pillarbox em 16:9). Os recortes estão nesta pasta (`f1-2006-china-*.jpg`).

## Vocabulário comum
- Células retas, empilhadas coladas (1–2 px de separação escura), sem painel de fundo, sem cantos arredondados.
- **Célula de nome**: branca, gradiente vertical (branco -> lavanda/cinza claro embaixo), texto escuro, fonte sans humanista (aprovada: Open Sans Bold).
- **Célula de valor**: preta, texto branco (gaps "+20.199", "0 Stops", "Lap 56", "3.0").
- **Caixa de posição**: quadrada à esquerda; **líder vermelho (#c61300)**, demais **azul-ardósia escuro (#424058)**, número branco. Na chegada, o líder mostra bandeira quadriculada no lugar do número.
- **Cabeçalho patrocinador**: célula branca com o logotipo "SIEMENS" em teal (#009aa6 aprox.), alinhada ao topo do bloco (patrocinador da legenda). Usar texto "SIEMENS"-like? NÃO reproduzir marca registrada de terceiros: usar um cabeçalho branco com o nome do widget/tema em teal no mesmo lugar.
- **Logo do canal**: canto inferior esquerdo "F1 / Formula 1" (branco com sombra) e "F1 TV" no canto superior direito. NÃO reproduzir marcas; opcional/omitir.

## Elementos (com timestamps no vídeo)
1. **Contador de voltas** (topo-centro, sempre visível; ex. 56/56 em t=60, "0 /56"): caixa branca com texto escuro "N/56" (volta atual/total ou restantes), sombra leve. Em t=120 e t=1262.
2. **Legenda de piloto** (t≈330, t=1300): bloco à esquerda-inferior: linha 1 cabeçalho branco com patrocinador (teal); linha 2 célula branca com nome ("Alonso"); linha 3 célula azul-ardósia com a equipe ("Renault", texto branco); à direita do bloco: **bandeira do país** (em cima) + **caixa vermelha com a posição** (número "1" branco) e embaixo uma caixinha com fornecedor de pneus (azul "M" Michelin / vermelha "B" Bridgestone). Duas legendas lado a lado para duelos (um à esquerda, outro à direita, o da direita com a posição em azul-ardósia).
3. **Barra de gap** (t=421): faixa horizontal [caixa pos vermelha "1"][célula branca "Alonso" alinhada à direita][célula preta "+20.199" centralizada][célula branca "M Schumacher" alinhada à esquerda][caixa pos azul-ardósia "3"].
4. **Lista de paradas/pit stops** (t=380, t=540): grade de 2 colunas × até 4 linhas: cabeçalho branco (teal) em cima; cada linha = caixa de posição azul-ardósia + nome (célula branca) + "0 Stops"/"1 Stop" (célula preta). Colunas separadas por ~35 px.
5. **Cronômetro de box** (t=500): barra [célula branca com nome "Alonso"][célula preta com o tempo "3.0"], fina, sobre o vídeo do pit stop.
6. **Mini-torre** (t=1200, t=1262): canto superior esquerdo: linhas [caixa de posição][sigla 3 letras em célula branca]; a linha do líder tem célula preta extra "Lap 56"; na bandeirada, caixa quadriculada no lugar do número 1.
7. **Legenda do vencedor** (t=1342): cabeçalho vermelho "Winner" + bandeira quadriculada, nome (branca) + bandeira do país, equipe (azul-ardósia) + fornecedor de pneus (caixa vermelha "B"); coluna preta à direita com tempo total "1:37:32.747", distância "305.066 Km", média "187.644 Km/h".
8. **Rótulo "Team Radio"** (t≈320): pequena caixa branca com texto escuro.
9. **Tela de resultado/pódio** (t=1380): gráficos em tela cheia (fora do escopo).

## Mapeamento para o AMS2 (dados disponíveis)
- Nome, posição, volta, gaps, voltas totais (LapsInEvent), última/melhor volta, número de paradas (PitState/contagem), bandeira do país (`Nationalities[]`, offset 20316 da estrutura v14; o menu de pausa/resultados mostra as bandeiras), classe, composto do pneu. Não há fornecedor de pneus nem equipe como texto separado (a equipe pode vir do nome do carro `CarNames[]`).
