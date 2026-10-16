# GPxSAT no AMS2: o que era e o que é agora

## Antes
- Temas F1 1993, 1999 (id f1-1998), 2004, 2012 e 2018, desenhados a partir de prints e mockups. O layout e as regras de aparição eram aproximações.
- Cada widget (board, standings, relative, pitstops, winner, qualitower etc.) tinha visual e gatilhos próprios por tema.
- Animação por dois motores simples (Broadcast04Pulse, Broadcast18Motion), sem relação com o GPxSAT.
- Antialiasing: padrão do Direct2D/DirectWrite, sem ajuste. Fontes livres (Barlow, Open Sans, Reddit Sans).
- Velocímetros do 2004/2012 inventados. Não existia o tema "Digital".

## Agora
- Nova camada `gpxsat` (Widgets/GpxSat, Core/Calc/GpxSat) com um modo por tema, com layout e regras lidos do código do GPxSat.gxm:
  - F1 1999 = Classic; F1 1999 Digital (tema novo, id f1-1999-digital) = Digital; F1 2004 = Digital 2004; F1 2012 = Digital 2010.
  - F1 1993 e F1 2018 não mudaram.
- Geometria em função de W e FH, cores ARGB, alinhamentos, prioridades e gatilhos conforme as specs em `ams2/reference/gpxsat/`.
- Animação: só o Digital 2004 anima (wipe branco + flip por linha, ou por coluna nos standings), com o dobro do tempo original (20 ms/passo). Classic, Digital e Digital 2010 aparecem de uma vez, porque o código original não anima.
- Fontes originais (Mañana, Futura, Digital Standard, DIN) carregadas em runtime, com antialiasing em tons de cinza e baseline alinhada a pixel. Cópia local fora do git; sem elas cai em Barlow/Segoe UI.
- Widget independente "Instrumentos GPxCInfo (Lo2k)": 360 km/h, 18.000 rpm, temperatura, combustível em voltas, pico de rpm por 1,5 s. Os velocímetros antigos ficam desligados por padrão.
- Também: mapa da pista, microtime, lista de modos ativos e speed traps (por passagem de setor observada).
- Input graph e radar intactos. Boards antigos continuam no código, desligados por padrão para não duplicar. Migração de perfil no esquema 9.

## Limites
- FH real (~17/18) e RH (25) são hipóteses. Larguras do Classic são estimativas.
- AMS2 não fornece motivo de penalidade, causa de abandono nem Q1/Q2/Q3. Essas telas existem, mas não disparam sem dado confiável.
- Mapa da pista aprendido pela trajetória; não há zonas amarelas no SDK.
- Validação foi por testes (821 aprovados) e previews offscreen; falta conferir em jogo.
