# GPxSAT no AMS2

Os temas F1 1999, F1 1999 Digital, F1 2004 e F1 2012 usam a camada `gpxsat`, respectivamente Classic, Digital, Digital 2004 e Digital 2010. F1 1993 e F1 2018 mantêm suas implementações.

## Uso

- No Control Center, escolha o tema e abra as opções de **GPxSAT (telas do GPxPatch)**. ShowMode, standings, splits/gaps, Director's Cut, contador e duração das páginas são configuráveis ali. Os atalhos do GP4 descritos nas especificações são evidência do programa original; não são atalhos globais adicionados ao AMS2.
- Digital 2004 usa wipe e flip sequencial com duração padrão duas vezes a original. Classic, Digital e Digital 2010 aparecem imediatamente, conforme as especificações extraídas.
- O relógio Classic é exclusivo e fica desligado por padrão para permitir as demais placas. Nos outros modos ocupa uma camada independente.
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
