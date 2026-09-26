# Spec: tipografia e fundo personalizáveis (V3)

Pedido de 26/09/2026. Estado no código em 26/09: uma família só (Barlow Semi Condensed, `DeviceResources.BuildPrivateFontCollection`), fixa nos widgets (`CreateTextFormat("Barlow", ...)`); `WidgetAppearance` tem só `FontScale` e `FontWeight` (0/400/600) por widget; `ColumnDefinition` e os campos de cabeçalho (`HeaderFields`) não têm fonte; fundo tem `Opacity` por widget (`WidgetPlacement`) e cor fixa em `PaletteTokens` (`PanelBackground` #0A1520).

## Fontes
- **Já baixadas e copiadas** para `Assets/Fonts` (OFL, com licença): Chakra Petch 500/600/700 e IBM Plex Sans 400/500/600 (estáticas, do repositório oficial do Google Fonts e da IBM). O csproj já publica `Assets\Fonts\*.ttf`.
- **Famílias oferecidas:** Barlow Semi Condensed (atual), Chakra Petch, IBM Plex Sans, Inter (já no repo, OFL) e **SF Pro**.
- **SF Pro:** a licença da Apple não permite embutir/redistribuir fora do ecossistema Apple, então NÃO se embute. A opção usa a fonte instalada no Windows (`SF Pro Display` / `SF Pro Text`, coleção do sistema) e cai em Inter se não houver. O usuário instala por conta própria.

## Fases
1. **Coleção e escolha por widget:** registrar as novas fontes na coleção privada; `WidgetAppearance` ganha `FontFamily` (chave: barlow, chakra, plex, inter, sfpro); o widget cria os formatos com a família e o peso escolhidos (pesos disponíveis por família: Chakra 500/600/700, Plex 400/500/600, Barlow 400/600, Inter conforme os arquivos). Mensagem IPC de aparência sobe de versão (campo opcional, compatível com perfis antigos).
2. **Por coluna e por campo de cabeçalho:** `ColumnDefinition` e os campos de cabeçalho ganham `FontFamily?` e `FontWeight?` (nulo = herda do widget). Isso exige um cache de `IDWriteTextFormat` por (família, peso, tamanho) nos widgets, em vez dos três formatos compartilhados (`_nameFormat`, `_statusFormat`, `_numericFormat`). UI no Control Center: seletor de fonte e peso nas abas Colunas e Cabeçalho. Migrar `ColumnConfigMessage`/`HeaderConfigMessage` (schema v1 -> v2, campos opcionais).
3. **Fundo:** cor de fundo (hex) e opacidade do fundo por widget, separadas da opacidade do conteúdo. Hoje `Opacity` multiplica tudo; o spec original pedia "opacidade de fundo e de conteúdo separadamente". Adicionar `BackgroundColor` e `BackgroundOpacity` em `WidgetPlacement`/IPC e trocar os usos de `PaletteTokens.PanelBackground`/`RadarPanelBackground`.
4. **Números tabulares:** valer para o app inteiro. Verificar se cada família tem dígitos tabulares por padrão (Chakra Petch e IBM Plex normalmente têm); onde não, aplicar a feature OpenType `tnum` via `IDWriteTextLayout`/`IDWriteTypography`, ou medir a largura de "0" a "9" e alinhar à direita.

## Uso sugerido (do usuário)
- Títulos e números de destaque: Chakra Petch 600/700, caixa alta, letter-spacing .02 a .06em nos títulos (tempo de volta, delta, velocidade, marcha: 700).
- Rótulos pequenos: IBM Plex Sans 11 a 12 px, caixa alta, letter-spacing .1 a .14em.
- Texto corrido, tabelas e legendas: IBM Plex Sans 400/500/600.
- `letter-spacing`: DirectWrite não tem por formato; precisa de `IDWriteTextLayout1::SetCharacterSpacing`. Entra na fase 2 ou como fase 5, se o usuário quiser.

## Verificação
Compilar e rodar os testes; conferir no log `[Fonts]` que as famílias entram na coleção; validar visualmente no Control Center (preview) e no overlay. Não abrir o overlay em corrida sem autorização (ver memória).
