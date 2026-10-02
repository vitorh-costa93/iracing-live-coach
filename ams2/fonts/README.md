# Fontes de número do tema 1998–2001

- `F1Broadcast98-Box.ttf`: números das caixas amarelas (Futura Condensed Extra Bold). Dígitos 0–8 desenhados à mão em `build/glyphs_box.py`; 9 = 6 girado 180°.
- `F1Broadcast98-Values.ttf`: valores amarelos (Futura Bold). Dígitos 0–8 e ponto desenhados em `build/glyphs_vals.py`; 9 = 6 girado; `:` `+` `-` derivados.
- Letras do tema: Reddit Sans 800 (OFL). A família também tem Reddit Sans Condensed e Reddit Mono, pesos 200–900.

## Como foram feitas
1. `build/sr.py`: super-resolução (registro de subpixel das amostras repetidas + deconvolução Richardson-Lucy) sobre `reference/f1-1998-hires-tower.png`.
2. `build/measure.py` e `build/measure_vals.py`: vértices-chave de cada dígito.
3. `build/glyphs_*.py`: contornos desenhados com retas e arcos de elipse; `build/overlay_box.py` e `build/overlay_vals.py` sobrepõem o desenho à reconstrução para conferir.
4. `build/make_fonts_v5.py` (caixas) e `build/make_fonts_v6.py` (valores) geram os TTF com fontTools.

Os desenhos vêm de gráficos de TV de terceiros. Uso pessoal; revisar licença antes de distribuir.

## Sombra dos valores amarelos
Nos valores, a captura tem uma sombra preta projetada: deslocamento de cerca de 2 px à direita e 2 px abaixo (na escala da captura, com cerca de 23 px de altura de dígito), preto com opacidade em torno de 60 a 65% e borda quase dura. A sombra NÃO faz parte da fonte; o renderizador do overlay deve desenhar o texto duas vezes (preto deslocado e depois amarelo) ou usar um efeito de sombra equivalente, proporcional ao tamanho do texto.

## Otimização contra os pixels (versão atual)
Os contornos desenhados à mão (`glyphs_*.py`) são refinados por síntese: `optpix.py` renderiza o contorno do mesmo jeito que a captura foi gerada (cobertura de pixel e desfoque de cerca de 0,4 px) e ajusta vértices e pontos de controle para minimizar a diferença com os pixels reais, com penalização para não se afastar do desenho limpo. Rodar `run_optpix_box.py 12345678 3.0 0.4` e `make_box_v8.py` para as caixas, e `run_optpix_vals.py 012345678. 2.0` e `make_vals_v8.py` para os valores. `pixel_diff.py` compara o glifo e o original na mesma grade de pixels.

## Reddit Sans (texto do tema)
- `RedditSans-ExtraBold.ttf`: instância estática do peso 800, gerada da fonte variável oficial (`google/fonts`, `ofl/redditsans/RedditSans[wght].ttf`) com `fontTools.varLib.instancer.instantiateVariableFont(font, {'wght': 800})`. Família DirectWrite: "Reddit Sans", peso ExtraBold.
- Licença: SIL OFL 1.1 (`OFL-RedditSans.txt`). Copyright 2020-23 Reddit, Inc.

## Open Sans Bold (tema 2004–2008)
- `OpenSans-Bold.ttf`: instância estática (wght 700, wdth 100) gerada da fonte variável oficial (`google/fonts`, `ofl/opensans/OpenSans[wdth,wght].ttf`) com `fontTools.varLib.instancer.instantiateVariableFont(font, {'wght': 700, 'wdth': 100}, updateFontNames=True)`. Família DirectWrite: "Open Sans", peso Bold.
- Licença: SIL OFL 1.1 (`OFL-OpenSans.txt`). Copyright 2020 The Open Sans Project Authors.

## Barlow Semi Condensed (tema 2010s)
- `BarlowSemiCondensed-Regular.ttf` (400) e `BarlowSemiCondensed-SemiBold.ttf` (600): cópia dos arquivos já usados no V3 (`v3\src\IracingLiveCoach.OverlayHost\Assets\Fonts`). Família DirectWrite: "Barlow Semi Condensed".
- Licença: SIL OFL 1.1 (`OFL-Barlow.txt`). Copyright 2017 The Barlow Project Authors.
