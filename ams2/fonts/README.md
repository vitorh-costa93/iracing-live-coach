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

## Barlow Semi Condensed (era do tema 2010s, substituído pelo 2018)
- `BarlowSemiCondensed-Regular.ttf` (400) e `BarlowSemiCondensed-SemiBold.ttf` (600): cópia dos arquivos já usados no V3 (`v3\src\IracingLiveCoach.OverlayHost\Assets\Fonts`). Família DirectWrite: "Barlow Semi Condensed".
- Licença: SIL OFL 1.1 (`OFL-Barlow.txt`). Copyright 2017 The Barlow Project Authors.

## Tema 2018 (F1 2018–2021): Verdana, fonte do sistema
- **Aviso:** a fonte original do gráfico de TV da F1 2018–2021 é a **Formula1 Display** (Regular/Bold/Wide), **proprietária** da
  Formula One; não está nem deve ser incluída neste projeto.
- Escolha: **Verdana** (instalada em todo Windows; não é copiada para `fonts\` e não é redistribuída). Comparada contra os quadros de
  `reference\f1-2018-*.jpg` junto com as candidatas desta pasta (Barlow Semi Condensed 600, Open Sans 700, Reddit Sans 800) e outras
  do sistema (Bahnschrift, Segoe UI, Tahoma, Trebuchet, Corbel): "Sebastian VETTEL" e as siglas da torre são largas e firmes, e só a
  Verdana chega perto da largura (≈ 95 % da largura do vídeo para a mesma altura de maiúscula; Open Sans/Segoe ≈ 80 %, Bahnschrift ≈ 75 %,
  Barlow bem mais estreita). Tem os pesos que o tema usa: regular (gaps, nome próprio), negrito (siglas, SOBRENOME) e itálico (número do carro).
- Diferença conhecida: a Formula1 Display é geométrica (O quase quadrado, R de perna reta); a Verdana é humanista. Se no futuro houver
  uma fonte livre larga e geométrica, basta trocar a família nos tokens de `Themes.F1_2018` (Theme.cs).
- O renderizador procura primeiro a coleção desta pasta e depois as fontes instaladas no Windows (`FontLibrary.SystemHas`); sem a
  família, cai em Segoe UI.