# GPxSAT no AMS2

Os temas F1 1999, F1 1999 Digital, F1 2004 e F1 2012 usam a camada `gpxsat`, respectivamente Classic, Digital, Digital 2004 e Digital 2010. F1 1993 e F1 2018 mantêm suas implementações.

## Uso

- No Control Center, escolha o tema e abra as opções de **GPxSAT (telas do GPxPatch)**. ShowMode, standings, splits/gaps, Director's Cut, contador e duração das páginas são configuráveis ali. Os atalhos do GP4 descritos nas especificações são evidência do programa original; não são atalhos globais adicionados ao AMS2.
- Digital 2004 usa wipe e flip sequencial com duração padrão duas vezes a original. Classic, Digital e Digital 2010 aparecem imediatamente, conforme as especificações extraídas.
- O relógio Classic é exclusivo e fica desligado por padrão para permitir as demais placas. Nos outros modos ocupa uma camada independente.
- Standings por evento (`rotation`, ligada por padrão; sem ciclo contínuo): na corrida, quando nenhuma tela de prioridade alta (vencedor, sky messages, pit timer, Director's cut) ou janela de gap/trap ocupa o slot principal, ele fica vazio até o líder fechar a volta; aí os standings (ou pit stops, se `raceStandings=pitstops`) aparecem estáticos por `2×pageSeconds` por página (uma passagem; só pagina se houver mais pilotos que linhas) e somem. Desligada, vale a escolha fixa de `raceStandings`/`speedSplit*`. Na classificação a tabela segue `qualiStandings` (estática). A opção `interval` de `raceStandings` só vale no tema 2010.
- Digital 2010 oferece torre lateral, faixa inferior, standings e intervalos. Microtime e lista dos modos ativos são opcionais nos quatro temas.
- **Instrumentos GPxCInfo (Lo2k)** é um widget independente: escala original de 360 km/h, 18.000 rpm, temperatura da água, combustível estimado em voltas e marcador de pico por 1,5 s. Valores ausentes não são inventados.
- Os velocímetros antigos de 2004/2012 ficam desligados; o gráfico de pedais independente e o radar conservam suas configurações. Os boards anteriores continuam disponíveis, desligados para evitar duplicidade. A migração de perfil ocorre uma vez no esquema 9 e preserva personalizações e sessões do gráfico/radar.

## Fontes e imagens pessoais

Fontes primárias: Mañana (Classic), Futura (Digital), Digital Standard (Digital 2004), DIN (Digital 2010). A coleção DirectWrite carrega os arquivos sem instalá-los no Windows; desenho com alfa usa antialiasing em tons de cinza e baseline alinhada aos pixels.

As fontes em `ams2/fonts/gpxpatch/` e imagens em `Assets/GpxPatch/` ficam fora do Git. São copiadas na publicação pessoal. A descoberta de fontes também aceita `AMS2_GPX_FONTS` (lista de pastas separada por ponto e vírgula) e a instalação local de GP3. Sem as originais, usa Barlow/Segoe UI; sem arte, usa geometria procedural. A reprodução visual com a arte original depende desses arquivos locais.

## Limites dos dados e da referência

- O AMS2 não fornece tipo/motivo de penalidade, causa de abandono nem subdivisão Q1/Q2/Q3. As telas sem gatilho confiável existem na prévia, mas não são acionadas com informações inventadas.
- Speed traps usam passagens pelos setores observadas pelo overlay; não são os pontos próprios do GP4. O erro de amostragem pode chegar a um quadro do provider. Comparações só aparecem com referências observadas.
- O mapa aprende a linha percorrida pelas posições reais: exige pelo menos 90% dos pontos e só conecta amostras consecutivas. Não há malha oficial, largura da pista nem localização das zonas amarelas no SDK; o modo automático usa a bandeira amarela global, sem pintar zonas fictícias.
- Fotos e bandeiras nacionais opcionais do Digital 2010 não fazem parte desta camada. Os assets pessoais existentes dos demais widgets permanecem intactos.
- As especificações preservam os níveis de confiança e lacunas da leitura estática. Algumas larguras do Classic usam estimativas em FH quando a referência depende da medição tipográfica; a fidelidade quadro a quadro e o comportamento em corrida real precisam de conferência no jogo.

## Verificação desta entrega

Build Release de Host e Control Center; testes Core, Shared e Integration; 68 prévias de telas e painel de instrumentos; conferência visual dos quatro modos e confirmação das quatro famílias originais. Nenhum jogo foi iniciado/controlado. Resultados finais e publicação estão registrados em `docs/CODEX_CONTINUIDADE.md` na raiz.

## Validação em jogo (17/10/2026)

AMS2, Formula V10 Gen2, Donington GP, 5 oponentes, dirigido por piloto automático visual (linha de pilotagem, ~3:10/volta). Conferido por capturas de tela:

- **1999 Classic (f1-1998):** velocímetro GPxGauges, Inputs, banners de diferença ("L. BADOER +1 Lap") e de nome/equipe ("M. SCHUMACHER / Ferrari").
- **1999 Digital:** banners azuis com tempos (1:14.515, 1:10.700), "V. COSTA 6", "+ 1 Lap".
- **2004:** painel de clima/pneus/combustível, "Laps remaining", banner "5 L. BADOER +35.700 V. COSTA".
- **2012:** torre de posições (1–6) e painéis laterais, volta completa até a linha de chegada.

Ainda não conferido em jogo: parciais S1/S2/S3 com cores, speed trap e as telas de classificação (exigem sessão de classificação). Barrichello/Stewart sem aparecer no overlay é problema separado (livery/nomes).

## Validação em jogo com piloto automático (17/10/2026, 2ª rodada)

Corrida de 6 carros em Donington GP, carro dirigido por script via memória compartilhada (~20 m/s, sem bater).
- **1999 Classic (f1-1998):** volta completa. Banners Intermediate 1/2 e Finish Line com diferença (+38.633, +1 Lap, +2 Laps), Lap/Fastest Lap com nome, equipe e tempo.
- **1999 Digital:** volta completa. Barras com diferença (+1:09.951, +1 Lap, +2 Laps) e faixa Fastest Lap/Lap.
- **2004:** volta completa. Barra de diferença (+5 Laps, +6 Laps), "RACE n Laps remaining", clima, pneus e combustível. Nomes em caixa mista ("V. Costa") em 2004/2012 (GpxOptions.MixedCaseNames); letras largas eram a ATL2004.ttf (monoespaçada, mesmo nome "Digital Standard") colidindo com a digital2004.ttf na coleção DirectWrite: FontLibrary ignora a ATL2004.
- **2012:** overlay renderizado (torre, Fastest Lap, "3 Laps remaining", "Final lap", bandeiras), mas o carro começou fora da pista e não completou volta limpa; parciais/speed trap deste tema não foram conferidos.
- Parciais S1/S2/S3 coloridas e speed trap não foram identificados nos recortes amostrados.
