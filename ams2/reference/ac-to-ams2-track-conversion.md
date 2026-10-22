# Prompt completo: converter uma pista do Assetto Corsa para o AMS2 (passo a passo)

Documento escrito em 19/10/2026 a partir do que foi feito para gerar `bahrain_ac`. Serve como prompt reutilizável: cole em uma sessão nova e substitua os caminhos.
Os formatos de arquivo estão em [ams2-track-format-spec.md](ams2-track-format-spec.md). Os scripts estão em [../tools/track-converter/](../tools/track-converter/).

Legenda: **[V]** verificado por byte/parse; **[H]** hipótese, só o jogo confirma.

## 0. Objetivo e restrições

Gerar um mod de pista para o Automobilista 2 (engine Madness) a partir de uma pista do AC (`.kn5` + `ai\fast_lane.ai`), no formato que o **AMS2CM** instala (`.7z` com raiz `Automobilista 2\`).

- Não existe ferramenta oficial. Os formatos `.bff`, `.meb`, `.bmt`, `.csm`, `.gcl` e `.aiw` foram descobertos por engenharia reversa **somente leitura** em um mod comunitário já funcional (Sochi, feito com Open Madness Track Tools 0.1.0) e em paks oficiais. Nada do jogo foi modificado.
- Python 3 com numpy, scipy e Pillow. Rodar sempre com `python -I`.
- Nunca testar com iRacing aberto. O teste no jogo só acontece quando o usuário avisar.
- Enquanto o usuário joga iRacing, não usar automação de interface nem alterar foco. Instale pelo backend do AMS2CM, preservando sua sessão.

## 1. Entradas

| Entrada | Caminho usado | Para quê |
|---|---|---|
| Pista AC | `C:\Users\Vitor\Downloads\bahrain_10_osrw_24pit\bahrain` (`bahrain.kn5`, `ai\fast_lane.ai`, `ai\pit_lane.ai`, `data\cameras.ini`, `data\surfaces.ini`, `ui\outline.png`, `ui\preview.png`) | geometria, texturas, linha de IA, pits, câmeras, superfícies, GUI |
| Mod de referência Sochi | `C:\Users\Vitor\Downloads\Sochi Autodrom\Automobilista 2` | modelo de estrutura; templates de trd, enx, cameras, triggers e os 5 paks sazonais |
| Sochi extraído | pasta `x_sochi` (conteúdo dos paks do Sochi extraído com `bff.py`: `tracks\sochi_ams\*`, `cameras\*`, `physics\*`) | templates lidos por `convert.py` |
| AMS2AVX.exe | instalação do AMS2 | só para achar o hash dos uids (ver passo 2) |

## 2. Passo a passo (o que foi feito, em ordem)

### Passo 1 — Entender o contêiner BFF (`bff.py`, `bff_write.py`)
1. Ler o Sochi: cabeçalho `' KAP'` (0x130 bytes), TOC de 42 bytes por entrada, bloco EXT (nomes), bloco DHSA (grupos) e dados alinhados em 16.
2. Descobrir o **uid**: Bob Jenkins lookup8 (64 bits) sobre o nome normalizado (minúsculo, `/` vira `\`). Achado no `AMS2AVX.exe` (hash em 0x140c5ebe0). Conferido em 2737/2737 nomes do Sochi (`uidhash.py`).
3. Descobrir o **CRC** da entrada (`~crc32` dos bytes armazenados) e a ordenação do TOC (byte a byte, sensível a maiúsculas).
4. Validar o leitor e o escritor: `validate.py` (4299/4299 CRCs) e `bff_write.py repack` reproduz os paks do Sochi **byte a byte** [V].

### Passo 2 — Formatos de conteúdo
- **MEB** (malha, `meb.py`): todos os 2227 arquivos do Sochi parseiam com tamanho exato; o escritor reproduz 2205/2207 [V].
- **BMT** (material binário 'BLMY', `bmt.py`, `bmtdump.py`): compilado a partir do `.mtx` (XML). 289/297 idênticos ao Sochi [V]. Hash de nomes `h = rotl(h,5)*31 + c`.
- **CSM** (colisão, `csm.py`, `csm_write.py`): PhysX 3.2 cooked triangle mesh com R-tree. Os 1493 meshes do Sochi parseiam exato; o cooker próprio foi validado com raios verticais contra força bruta (0 divergências) [V]. Que o PhysX do jogo carregue a árvore própria é **[H]**.
- **GCL** (limites de pista e velocidade do pit, `gcl.py`): re-codifica o Sochi byte a byte [V].
- **AIW** (linha de IA, `aiw_pit.py`): estrutura de `[Waypoint]`, `WP_PTRS`, ramo de pit com bifurcação, junção e ramais por box [V no Sochi].
- Triggers de largada e setores (`trig_roles.py`, `trigcrc.py`): papéis dos CRCs inferidos pela posição [H, forte].

### Passo 3 — Ler o KN5 do AC (`kn5.py`)
Lê o `bahrain.kn5` v5 até o fim: 141 texturas DDS, 143 materiais, 1215 meshes, 169 dummies [V].

### Passo 4 — Converter (`convert.py`)
Comando:

```
python -I convert.py <ac_track_dir> bahrain_ac <out_dir> <sochi_ref_dir> <x_sochi_dir>
```

Dentro do script, na ordem:
1. **Espelho em Z**: `(x,y,z)_AMS2 = (x,y,-z)_AC`. Evidência [H, forte]: a área XZ assinada do loop da IA tem sinal oposto entre o Bahrain do AC e o Sochi do AMS2, embora as duas pistas reais corram no sentido horário. A ordem dos índices de cada triângulo é invertida para manter `cross(b-a, c-a)·n > 0`.
2. **Materiais**: um `.mtx` + `.bmt` `rz_basic` por material do kn5 (só `txDiffuse`). Shaders AT/Tree/Grass viram `USE_ALPHATEST` sem culling; blends viram alpha blending sem z-write. DDS copiados como estão. Materiais `groove`, `skidmark_01/02` e `bah_skid_decals_a` (camadas alpha pretas de borracha) não são exportados: viravam uma faixa cinza escura larga no asfalto. `ksMultilayer` (tarmac!1, textura 1024x4096 de faixas) usa o tile `tarmac_diff_a_07_d` com UV x1,5/x3,5, para o asfalto ficar uniforme.
3. **Meshes**: um `.meb` por mesh visível e renderizável (ignora `AC_*`): 1001 meshes, 994 mil triângulos. Tangentes recalculadas, bounds em float32.
4. **Cena**: `bahrain_ac.sgx` (um `OBJ_ID` por mesh e uma partição com AABB), `.enx` e `trackproperties.bin` copiados do Sochi [H], `cameraconfig.xml` do Sochi.
5. **AIW**: de `fast_lane.ai` (v7, 18 floats extras por ponto; sideLeft/Right em [5]/[6]), reamostrada a cada 5 m (1071 waypoints, volta de 5357 m). Girada para o wp0 cair na linha `AC_TIME_0`; setores de `AC_TIME_1/2`. Layout igual ao do Sochi [V]: `wp_pos` = centro da pista, `wp_width` = metade da largura útil (esq+dir)/2 (cada lado limitado a 14 m, suavizado), `wp_path` = deslocamento da linha de corrida (linha = pos + path·perp; a fast_lane vai em `path`, não em `pos`). Antes (path=0, largura = min(esq,dir)) os carros andavam no meio; grid de `AC_START_n`; boxes de `AC_PIT_n`. Ramo de pit de `pit_lane.ai`: trecho fora da linha rápida, reamostrado a 5 m, bifurcação no wp 1020, junção no 148, 196 waypoints (978 m) e 24 ramais, um por box.
6. **Câmeras de TV**: de `data\cameras.ini`. Uma `CTrackingCamData` por câmera do AC (20) e uma `CSphereArea` por waypoint (1071, raio máx(6.5, meia largura + 2)); cada waypoint pertence à câmera cujo [in,out) o contém. FOV/DOF do AC não são carregados.
7. **Triggers**: caixas de largada/chegada e fim dos setores 1 e 2 entre os dummies `AC_TIME_n_L/R`, usando os CRCs do Sochi.
8. **Paks**: `bahrain_ac.bff` (meshes, materiais, texturas, sgx/enx/trd, câmeras), `bahrain_ac_Physics.bff` (aiw, csm, triggers, gcl, object_properties) e `AUT/SNO/SPR/SUM/WIN_bahrain_ac.bff` copiados dos sazonais do Sochi, renomeados.
9. **CSM**: meshes do kn5 com nome `<dígitos><CHAVE>` e CHAVE em `surfaces.ini`; espelhados, com vértices duplicados fundidos. Ids [H]: TARMAC/PITLANE 0, KERB 10, CARPET/OFFTRACK/SAND/GRASS 7 (não há id de cascalho), WALL 13 (12 se pneu, 14 se armco/rail/barrier). 158 meshes, 197 mil triângulos.
10. **GCL**: faixa flag 1 (pista dirigível, 0,5–30 m dos lados da linha) a cada 5 m; faixa flag 2 (±5 m) sobre a zona do limitador do pit, e quadrados flag 10 e 4 no início e no fim dela [H]. 2300 triângulos.
11. **Soltos e GUI**: `.mtx`/`.bmt`/`.trd` em `Tracks\bahrain_ac\`; DDS DXT5 em `GUI\tracklogos` (512, outline), `trackmaps` e `trackmaps3d` (1000, outline) e `trackphotos` (1920x1080, preview).
12. `debug_aiw.txt` é só diagnóstico e **não** vai no pacote.

### Passo 5 — Verificar (`check_out.py`)
`python -I check_out.py <out_dir>\bahrain_ac bahrain_ac` relê tudo com leitores independentes. Resultado do Bahrain:
- 0 erros de CRC/uid; todo meb parseia; 141 materiais, 131 texturas e todos os recursos do sgx presentes.
- Loop da AIW com a mesma orientação do Sochi; erro de rumo do grid < 1°; linha de IA 0,26 m (mediana) acima do vértice da pista.
- Ramo de pit: topologia de ponteiros igual à do Sochi, score monotônico.
- CSM: 158 meshes, 0 erros de árvore; 148/148 raios verticais acertam o id 0.
- GCL idêntico no re-encode; 100% dos waypoints principais dentro da flag 1.
- Câmeras: 1071 referências, únicas e em faixa; 100% dos waypoints cobertos.

### Passo 6 — Empacotar
```
cd <out_dir>\bahrain_ac
"C:\Program Files\7-Zip\7z.exe" a -t7z -mx=5 "C:\Users\Vitor\Downloads\Bahrain AC (AMS2).7z" "Automobilista 2"
```
Resultado: 61 MiB, 294 arquivos, estrutura igual à do Sochi. Instalar pelo AMS2CM.

## 3. O que só o jogo confirma (checklist do teste)
1. A pista aparece **sem espelhamento** (se estiver espelhada, trocar o sinal do espelho em `MIRROR` no `convert.py` e regerar).
2. O jogo carrega o `.csm` (R-tree próprio) e o carro dirige em cima dele; grip e som por superfície (cascalho não tem id).
3. A IA usa o ramo de pit (entra no 1020, para nos boxes, volta no 148).
4. Limites de pista e limitador do pit (`.gcl`); disparo de volta e setores (triggers).
5. As câmeras de TV trocam ao longo da volta.
6. Itens ainda copiados do Sochi como placeholder: `trackproperties.bin`, `cameraconfig.xml`, `enx`, seasonal paks, campos do trd.

## 4. Pegadinhas
- Os scripts `hashtest*.py`, `keytest.py`, `l2test.py` e `exe_re.py` são exploratórios e têm caminhos fixos do scratchpad; o fluxo de geração usa só `kn5`, `meb`, `bmt`, `bff`, `bff_write`, `csm`, `csm_write`, `gcl` e `convert`.
- Em `convert.py`, os templates do Sochi são lidos de `x_sochi\tracks\sochi_ams`, `x_sochi\cameras` e `x_sochi\physics`.
- Os paks oficiais têm EXT criptografado e TOC RC4 (chave índice 2); o formato comunitário não usa isso.
- Textura sem DDS no kn5 cai em `WHITE.DDS`.

## 5. Pistas com vários kn5 (Magny-Cours 2025, 20/10/2026)
- `convert.py` agora funde todos os `.kn5` listados em `models.ini` (exceto `*groove*`); os dummies `AC_*` podem estar em `actime.kn5`. Materiais/texturas reindexados; textura homônima com bytes diferentes ganha nome único.
- Shader `ksMultilayer_fresnel_nm` usa o `txDiffuse` direto (UV já repete); só o `ksMultilayer` puro usa o tile de asfalto.
- Superfícies: `ROFF`/`SAND`/`GRASS` = 7, `ROAD`/`PIT` = 0; zona do limitador do pit vem de meshes `^\d+pit`.
- Resultado `magnycours_ac`: 2829 meshes, 935 mil tris, AIW 878 wps (4391 m), pit 152 wps + 26 ramais, 22 câmeras, `check_out` sem erros. Pacote: `Downloads\Magny-Cours AC (AMS2).7z` (88 MiB). Árvores do CSP (`extension\trees`) não são convertidas.

## Licoes do Magny-Cours (20/10)
- **Nome de malha/material que comeca com digito (`1road_01`) = AMS2 nao renderiza a malha** (pista inteira sem asfalto). O conversor prefixa `m`/`M`.
- Malha satelite `exterior` (kn5, 4096^2 esticada em 11 km) fica borrada: usar detalhe tileado (`grass03`, 6 m/tile).
- Materiais `ksMultilayer_fresnel` (ROAD*/PITR*/DROAD*): a superficie real e `txDetailR`, nao `txDiffuse`; UV reescalado para metrico (4 m/tile). Spec capado a 0.05.
- Como testar: copiar o 7z em `Mods\Enabled`, abrir AMS2 CM, Apply; abrir o jogo (`Start-Process steam://rungameid/1066890`), Corrida simples > pista > sessao de treino ligada > Iniciar. Mouse no jogo so responde com mouse_move + down + up.

## Lições da IA e do asfalto (20/10, Phoenix/Bahrain)
- Originais do AMS2 (224 AIWs analisados): `wpd_CornerState` 1 = 1 waypoint de início, 2 = curva até o ápice (~76 m mediana), 3 = saída (~52 m). `CornerType` 3 = esquerda, 4 = direita, 5/6 = chicane. Sem marcadores a IA freia mal.
- `times` fixo em 110 s estava errado (Phoenix ~25-60 s); agora vem de `est_laptime()` (massa pontual, alat 12, vmax 80). Calibração ruidosa (dispersão ~40%).
- Linha da IA: margem >= ~1 m da borda do asfalto (original p5 = 1,4 m); `wp_width` usa só asfalto, `wp_dwidth` inclui zebra.
- Asfalto original ~RGB 75 neutro; texturas do AC (~90-140, amareladas) são escurecidas por `tone_asphalt()`.
- `--dump-aiw <arquivo>` grava o AIW em texto para diagnóstico.

## Pesquisa de IA (20/10)
**AIWs originais (224 pistas):** cabeçalho quase sempre igual ao nosso (Late Braking 1.0 em 226/230, Anticipation 40/80/160, GrooveWidth 6-7.5, UseLineBlendSpeed 1); só Montreal 1.15, Kyalami 1.05 e Nordschleife 1.02 diferem. Os valores que realmente variam são por waypoint: `wp_galpha` = 0.45 em curvas (R < ~200 m) e 0 nas retas; `wp_groove_lat` ~ -0.27*`wp_path`; `wp_event=(1,2,1.05-1.2)` em ~12% dos waypoints (multiplicador, função não confirmada). Implementado galpha/groove no conversor.
**AMS2, por piloto/carro** (UserData\CustomAIDrivers\<classe>.xml, fórum Reiza): `race_skill` (faixa estreita por carro), `line_skill` (aderência à fastline, v1.6.9.95+), `avoidance_of_mistakes`, `weight/power/drag_scalar` 0.9-1.1 etc. Não existe parâmetro de frenagem: a frenagem vem da fastline + marcadores + física do carro. `Vehicles\physics\aeroreductiontableai.mrdf` = tabela de redução de arrasto/aero p/ IA (draft).
**iRacing:** sem documentação técnica de treino; patch notes citam "lições" da IA (ovais, velocidade ideal por posição, recuperar ao trocar de linha, só levantar o pé em ovais) e rosters com Skill, Aggression, Optimism, Smoothness por piloto; skill ~60-120% varia por carro/pista. Conclusão: ambos separam *linha por pista* de *perfil de piloto por carro*; o nosso conversor só controla o primeiro.

## Lote e layouts (21/10/2026)

Leia o guia inteiro e confira no AMS2CM o que já está instalado antes de gerar novos IDs. Preserve os pacotes existentes; entradas AC, extrações, saídas, logs e arquivos `.7z` ficam fora do Git.

```
python -I convert.py <source> <id> <output> <sochi_ref> <x_sochi> --layout <layout> --require-complete-ai --dump-aiw <diagnostic.aiw>
python -I check_out.py "<output>/<id>/Automobilista 2" <id> --require-pits --json <validation.json>
python -I batch_convert.py <manifest.json> [--only <id>] [--force]
python -I tests_track_conversion.py
python -I tests_validation.py
python -I tests_wall_clearance.py
```

No Windows, passe caminhos com espaços entre aspas. `--layout` seleciona `models_<layout>.ini`, AI e câmeras daquele layout e `ui/<layout>`; apenas `surfaces.ini` pode ser compartilhado com a raiz. Nenhum KN5 declarado pode faltar. Os metadados e a capacidade vêm da fonte e dos dummies reais, com overrides explícitos quando necessários.

O manifesto JSON de `batch_convert.py` contém `work`, `packages`, `reference`, `extracted` e `tracks` (cada entrada: `id`, `name`, `source`, `layout` opcional, `meta` opcional e `street` opcional). A execução sequencial registra fases em `status.json`, logs separados e um relatório JSON independente por layout. O cache inclui scripts, entradas e templates; `--force` regenera a conversão. Somente uma validação explicitamente aprovada permite criar o pacote. Cada arquivo é criado do zero, testado com `7z t` e substituído atomicamente no volume de destino.

Use `--ground-pits` (ou `ground_pits: true` na entrada do manifesto) somente quando os dummies de boxes precisarem de ajuste vertical. Reprojeta todos os `AC_PIT` no asfalto da fonte + 0,3 m, preservando XZ e orientação, e recusa piso ausente/ambíguo ou correção acima de 3 m. Foi necessário nos layouts Magny-Cours 1992/National; conferir a colisão gerada depois continua obrigatório.

Correções do pipeline nesta etapa:
- Chaves de superfícies completas, incluindo dígitos e `_`, com correspondência pelo maior prefixo; a mesma regra serve a colisão, limites laterais, paredes e pits.
- Cada seção MODEL conserva sua própria instância, posição e yaw em graus, inclusive quando repete o mesmo KN5. A ordem yaw/posição foi conferida com o [TrackMapRenderer do Content Manager](https://github.com/gro-ove/actools/blob/master/AcTools.Render/Kn5SpecificSpecial/TrackMapRenderer.cs); pitch/roll não zero é recusado até confirmar a convenção completa.
- Suavização da largura nunca alarga um gargalo local. O otimizador exige convergência, valores finitos, limites respeitados e resíduo KKT pequeno; usa L-BFGS-B quando o método inicial não converge.
- Distância lateral de paredes considera a faixa vertical do triângulo: paredes de um piso inferior/superior não fecham artificialmente a linha sobre/sob uma ponte. Paredes altas que atravessam a altura do carro continuam contando.
- Suavização da colisão usa vizinhança 3D, conservando os níveis de pontes/túneis. `PAVEMENT` recebe física de asfalto mesmo quando `IS_VALID_TRACK=0`: validade da volta e material físico são conceitos distintos; os limites continuam definidos no GCL.
- Áreas das câmeras usam o centro e a largura efetivamente escritos na AIW, cobrindo também corredores assimétricos; a checagem exige cobertura do centro e da linha de corrida.
- Portais TIME_1/TIME_2 invertidos na fonte são renumerados pela ordem ao longo da fast lane, conservando as posições. Portais coincidentes são recusados; AIW e verificador exigem os três setores na ordem de circulação. Abu Dhabi 2021 precisou dessa renumeração.
- Detecção do ramo dos boxes usa o lado correto das larguras AC. Quando a largura da fonte inclui o apron inteiro, o `pit_lane.ai` explícito, ancorado nos `AC_PIT`, pode identificar o ramo pela distância à linha principal. A fonte e as distâncias aparecem no log.
- Zona do limitador usa geometria PIT/PITLANE; sem ela, aceita somente pares explícitos `pitlightin`/`pitlightout` cuja projeção no ramo seja próxima, não ambígua e ordenada. Baku usa essa segunda origem. A posição exata do acionamento ainda deve ser confirmada no jogo.
- KN5 classe 3 é lido respeitando os vértices de 76 bytes e a tabela de bones. Decorações ficam estáticas na pose da fonte, sem portar animações do AC. Formato conferido no [Kn5Writer do AcTools](https://github.com/gro-ove/actools/blob/master/AcTools/Kn5File/Kn5Writer.cs).
- `check_out.py` retorna código diferente de zero diante de falha. Confere a linha real (`wp_pos + wp_path * wp_perp`), inclusive ramo de pit e posições dos boxes, contra colisão; exige superfície dirigível, continuidade, limites/GCL, topologia dos boxes e cobertura por câmeras efetivamente referenciadas. Raios respeitam a altura do carro para não confundir pontes com o piso inferior.

Um pacote aprovado por esses leitores continua exigindo o checklist no jogo da seção 3: carregamento, condução, IA entrando/parando/saindo dos boxes, limitador, setores e câmeras. A aprovação estrutural não comprova esses comportamentos ao vivo.

Adaptação da fonte Hungaroring 1988 deste lote: a tabela original declara `KEY=1ASPHALT`, sem qualquer mesh correspondente; as malhas físicas são `1roadtrack*`, com materiais ROAD/ROADX/ROAD2X. Na cópia extraída foi corrigida apenas essa chave para `ROAD`, conservando os parâmetros físicos. O `.7z` original permanece intacto; hashes e alteração ficam no registro privado `source-repairs.json` junto do lote.

### Instalação sem interface

Destino final de **toda pista nova aprovada**: `C:\Program Files (x86)\Steam\steamapps\common\Automobilista 2\Mods\Enabled`. Copiar o `.7z` aprovado para essa pasta, conferir seu SHA256 e aplicar pelo backend do AMS2CM. Extrações, pacotes temporários e arquivos reprovados permanecem fora de `Enabled`.

O [CLI oficial do AMS2CM 0.3.0](https://github.com/OpenSimTools/AMS2CM/blob/v0.3.0/src/CLI/Program.cs) chama `Config.Load(args)`, `Init.CreateModManager(config)` e `InstallEnabledMods()`. Esse mesmo fluxo pode ser executado por um pequeno console sem janela usando `AMS2CM.Core.dll` e as dependências da **mesma versão instalada**. Não automatize a GUI quando o usuário estiver jogando. Registre logs, mantenha backup prévio de `Mods/state.json` e confira depois os IDs instalados, arquivos no jogo e hashes dos pacotes. O retorno do backend não substitui essas verificações nem o teste ao vivo.

### Revisão de pistas já convertidas: colisão e escala visual

- Suavização do asfalto: usar ajuste de plano local (`road_surface.py`), com raio 3D, pesos contínuos e correção limitada. A média simples das alturas cria ondulações quando a triangulação é irregular em subidas/cambagens. Vértices coincidentes entre meshes são tratados juntos; vizinhanças insuficientes preservam a fonte. Pontes continuam separadas pelo raio 3D. Testes: `python -I tests_road_surface.py`.
- Escala de texturas de detalhe: `metric_uv_scale` usa mediana **ponderada pela área 3D**. Em Shanghai, triângulos pequenos cobrindo somente 2% de uma malha ditavam a escala para os outros 98%, esticando o detalhe em dezenas/centenas de metros. Testes: `python -I tests_material_uv.py`. Essa correção de escala não implementa o shader multilayer completo do AC; macro/máscara/normal continuam uma limitação do material básico.
- Ao corrigir uma pista com IA já aprovada no jogo, preservar AIW/GCL/triggers/câmeras e trocar somente os recursos afetados: CSM de asfalto para colisão; stream UV dos meshes correspondentes para escala visual. Conferir igualdade dos demais recursos antes de empacotar. Revalidar a saída e conservar o pacote anterior fora de `Mods/Enabled` para rollback.
- Para conversões antigas sem a fonte AC, [AIW_REPAIR.md](../tools/track-converter/AIW_REPAIR.md) documenta o reparo limitado à linha principal usando CSM/GCL. O reparador recusa centros fora do corredor; nesse caso, regenerar pela fonte ou por uma AIW de outro layout comprovadamente equivalente em geometria/timing/boxes, sempre validando contra a colisão e os limites do pacote de destino.
