# Board: widget rotativo inferior (especificação para a UI)

Lógica pronta em `ams2/src/Ams2.Core/Calc/BoardTracker.cs` (modelos em `BoardModels.cs`), testes em
`ams2/tests/Ams2.Core.Tests/BoardTests.cs`. O provider publica o resultado a cada quadro (~60 Hz) em
`OverlayModel.Board` (`BoardState`, imutável). O widget só desenha: não recalcula nada, não guarda histórico.
Inspiração: transmissão de F1 2003 (faixa translúcida, 2 colunas × 4 linhas) e 2006 (células brancas/pretas, barra de gap,
tabela "Lap 27/26/25"). Ver `f1-2003-brazil-analysis.md` e `f1-2006-china-analysis.md`.

## 1. Modos e prioridade

Um único widget na parte inferior. `BoardState.Mode` diz o que desenhar. Maior prioridade sobrepõe menor:

| Prioridade | `BoardMode` | Quando | Dados |
|---|---|---|---|
| 4 (maior) | `LineTower` | Corrida: da passagem do líder pela linha até o fim do hold da última página | `Tower` |
| 3 | `SectorGap` | Janela de passagem de uma marca de setor (S1, S2, S3 = linha) | `SectorGap` |
| 2 | `LapComparison` | Corrida, intervalo livre, voltas completas do jogador múltiplas de 3 (≥ 3) | `LapComparison` |
| 1 | `DriverPlate` | Qualquer outro intervalo livre com jogador | `Plate` |
| 0 | `None` | Fora de sessão / sem jogador e sem torre | — |

- Fora de corrida (treino, qualificação, warm-up, time attack, formation lap): só `SectorGap` e `DriverPlate`.
- Os blocos de dados podem vir preenchidos mesmo quando o modo é outro (ex.: `Plate` sempre que há jogador;
  `SectorGap` enquanto a janela existe, ainda que escondida atrás da torre). **Desenhe só o bloco do `Mode`.**

## 2. Máquina de estados

```
            leader cruza a linha (corrida)
  ┌──────────────────────────────────────────────────────────┐
  │                                                          ▼
DriverPlate ◄──► LapComparison        LineTower: pág 1 → (cheia + 4 s) → pág 2 → ... → última (completa + 4 s)
  ▲   (volta do jogador % 3 == 0)        │
  │                                      ▼ fim da rodada
  └──────── janela de setor fecha ◄── SectorGap (abre no 1º carro na marca, fecha 2 s após o último; teto 40 s)
```
O modo de cada quadro é simplesmente o de maior prioridade que está ativo; não há fila.

## 3. LineTower (torre da linha)

- **Rodada**: começa quando um carro fecha uma volta de número maior que qualquer anterior (o líder). Uma nova volta do
  líder descarta a rodada anterior, mesmo incompleta. Quem entra no meio da corrida espera a próxima volta do líder.
- **Entrada**: cada carro entra uma vez por rodada, no instante em que cruza a linha (tempo interpolado sub-quadro pela
  distância na volta entre duas amostras; sem amostra plausível, distância/velocidade).
- **Ordem** = ordem de passagem (`Position` 1..N). `RacePosition` = posição oficial do jogo no cruzamento.
- **Gap**: líder `GapKind=Leader`, `GapText="Lap N"` (N = voltas completas do líder, `GapLaps`);
  demais `Time`, `GapText="+0.239"` (tempo desde a passagem do líder, `GapSeconds`; ≥ 60 s → `"+1:02.345"`);
  retardatário `Laps`, `GapText="+1L"` (`GapLaps` = voltas atrás do líder).
- **Páginas** de `PageSize` = 8: slots 1–4 na coluna esquerda (`Column`=0, `Row` 0..3), 5–8 na direita (`Column`=1).
  `Tower.Entries` já é só a página exibida. `PageIndex` 0 = posições 1–8, 1 = 9–16, 2 = 17–24…
- **Tempo de página**:
  - página cheia: fica até `max(instante do 8º cruzamento, início da página) + 4 s`;
  - a próxima página começa com quem já cruzou durante o hold (pode começar vazia) e vai sendo preenchida;
  - última página (parcial ou não): até `max(último cruzamento, início da página) + 4 s`, quando todos os esperados cruzaram;
  - página ainda incompleta: fim = último cruzamento + `TowerStallSeconds` (30 s): carro parado na pista não trava o board.
- **Esperados** (`ExpectedCount`): carros que já cruzaram + elegíveis que faltam. Elegível = não está parado no box
  (`InPit`), nem na garagem (`InGarage`/`DrivingOutOfGarage`), nem Retired/DNF/DSQ. Carro que entra no box no meio da
  rodada deixa de ser esperado; abandonados nunca entram na torre.
- `Tower.Complete` = todos os esperados cruzaram. `PageCount` = páginas conhecidas até agora.

## 4. SectorGap (passagem de setor)

- Marcas: S1 (setor 0→1), S2 (1→2), S3 (2→0 = linha). `CurrentSector` do AMS2 é 0..2. A distância da marca não é
  exposta: é aprendida pela interseção dos intervalos [antes, depois] de todos os cruzamentos (converge em poucas passagens).
- **Corrida**: a janela abre quando o **primeiro carro do campo** (maior distância de corrida entre os elegíveis) cruza a
  marca; fecha `SectorCloseDelaySeconds` (2 s) depois que **o último elegível** cruzar, ou no teto
  `SectorMaxWindowSeconds` (40 s) contado da abertura. Uma nova marca do líder substitui a janela anterior.
- **Fora de corrida** (campo espalhado pela pista): a janela é do par jogador + vizinho — abre quando um dos dois cruza a
  marca e fecha 2 s depois que o segundo cruzar (mesmo teto). Cruzamentos de outros carros não abrem janela.
- **Anti-pisca**: janela que ficou escondida atrás da torre só aparece se ainda tiver ≥ `SectorMinShowSeconds` (1 s).
- **Conteúdo** (`BoardSectorGap`): `Sector` 1..3, `Player`, `Neighbor` (`BoardDriver`: posição, nome, sigla, equipe,
  bandeira, pneu), `GapSeconds` com sinal (**+ = vizinho à frente**, − = vizinho atrás), `GapText` ("+0.412"/"-0.412"),
  `NeighborAhead`, `IsSplit`.
  - `IsSplit=false`: o par ainda não cruzou a marca; gap ao vivo (GapTracker; sem dado, distância/velocidade).
  - `IsSplit=true`: os dois cruzaram; gap = diferença exata dos cruzamentos, reinterpolados com a mesma estimativa da marca
    (fica congelado até a janela fechar). Sugestão de UI: valor ao vivo mais apagado, split em destaque.
- `CloseT`: fim previsto. Enquanto o último carro não cruzou, é o teto (abertura + 40 s).

## 5. Regra do vizinho (SectorGap e LapComparison)

1. Candidatos: o carro imediatamente à frente e o imediatamente atrás do jogador.
   - Corrida: pela posição oficial (P−1 e P+1), pulando inelegíveis.
   - Fora de corrida: pela posição física na pista (lado mais curto, como o Relative).
2. Inelegíveis: o jogador, `InPit`, garagem, Retired/DNF/DSQ.
3. Escolhe o de menor |gap em tempo| (GapTracker; sem dado, |distância| / velocidade).
4. Empate ou diferença < `NeighborTieSeconds` (0,05 s) → **o da frente**.
5. Histerese: o vizinho escolhido na abertura da janela (ou no início da volta do comparativo) não muda até ela fechar; só é
   trocado se o carro sumir da sessão ou for para a garagem.

Por quê: o "gráfico de gap" da TV compara o piloto com quem ele está disputando; o mais próximo em tempo é a disputa real,
e preferir o da frente no empate segue a convenção da transmissão (quem persegue). A histerese evita o nome trocar no meio
do gráfico.

## 6. LapComparison (comparativo de voltas)

- Corrida, intervalo livre, `LapsCompleted` do jogador ≥ 3 e múltiplo de 3: vale durante toda a volta seguinte
  (ex.: depois da volta 27 até fechar a 28).
- `Laps` (`BoardLapRow`, mais recente primeiro: 27, 26, 25): `PlayerTime`, `NeighborTime`, `Delta` = jogador − vizinho,
  `PlayerFaster` (delta < 0 → verde). Tempo ausente = null (volta sem tempo, volta antes de o overlay abrir).
- Histórico: por carro, `LastLapTime` ao completar a volta (espera até 1 s pelo valor novo, pois o jogo pode atualizar
  depois de `LapsCompleted`); voltas com tempo ≤ 0 são descartadas. Guarda as 8 últimas.
- Só aparece se houver pelo menos um tempo do jogador nas 3 voltas.

## 7. DriverPlate (legenda)

`Plate` (`BoardDriver`): `Name` (completo), `ShortName` (sobrenome; "M Schumacher" com sobrenome repetido), `Code` (3
letras), `Team` (nome do carro sem "(M)"/"(B)" e sem o nome da classe), `Position`, `Nationality` (ISO alpha-2, "" = sem
bandeira), `TyreSupplier` ("M", "B" ou ""), `IsPlayer`.

## 8. Campos de `BoardState`

| Campo | Uso |
|---|---|
| `Mode` | o que desenhar |
| `Revision` | sobe em mudança estrutural (modo, página, piloto novo na página, janela nova, vizinho novo): disparar fade-in/slide |
| `Now` | relógio do provider do quadro |
| `IsRace` | sessão de corrida |
| `WindowStartT` / `WindowEndT` | início/fim do modo atual (página da torre, janela de setor); fim = +∞ em LapComparison/DriverPlate |
| `RemainingSeconds` | `WindowEndT − Now` (≥ 0; +∞ nos modos livres) → fade-out quando < ~0,4 s |
| `ElapsedSeconds` | `Now − WindowStartT` → fade-in/animação de entrada |
| `ItemCount` | itens do modo (pilotos na página, 2, linhas do comparativo, 1) |
| `Page` | atalho para `Tower.Entries` |
| `Tower`, `SectorGap`, `LapComparison`, `Plate` | dados de cada modo |

Notas para a UI:
- Pilotos aparecem na página um a um: anime a entrada de cada novo slot (compare `ItemCount`/`Revision`).
- Intervalos livres podem ser curtos (fração de segundo entre duas janelas); faça crossfade e evite piscar a legenda.
- Valores exibidos já vêm formatados (`GapText`, "Lap N"); tempos de volta vêm em segundos (formate m:ss.mmm).
- Alocação: os blocos pesados (`Tower`, `Plate`, comparativo) são reutilizados entre quadros quando nada muda (mesma
  referência); só o `BoardState` e o `SectorGap` ao vivo são recriados.

## 9. Parâmetros (`BoardOptions`, passados ao `OverlayDataProvider`)

| Parâmetro | Padrão | Significado |
|---|---|---|
| `PageSize` | 8 | pilotos por página (metade por coluna) |
| `PageHoldSeconds` | 4 | hold da página cheia / última |
| `TowerStallSeconds` | 30 | sem cruzamento novo numa página incompleta → encerra a rodada |
| `SectorCloseDelaySeconds` | 2 | fecha após o último cruzamento |
| `SectorMaxWindowSeconds` | 40 | teto da janela de setor |
| `SectorMinShowSeconds` | 1 | anti-pisca da janela que estava escondida |
| `NeighborTieSeconds` | 0,05 | empate do vizinho → o da frente |
| `LapComparisonEvery` | 3 | comparativo a cada N voltas do jogador |
| `LapComparisonLaps` | 3 | voltas listadas |
| `LapTimeSettleSeconds` | 1 | espera pelo LastLapTime novo |

## 10. Reset

Reset total quando: muda o tipo de sessão ou a pista (ou o comprimento), as voltas do líder diminuem (reinício da
corrida), o relógio volta, o jogo sai da sessão (`InSession=false`) ou o provider reconecta.

## 11. Dados falsos para PNG: `AMS2_FAKE_BOARD=1`

Com `--fake`/`--png`, `AMS2_FAKE_BOARD=1` troca o campo de 8 carros por uma corrida rápida de 20 carros
(`FakeRawSource`): pista de 1400 m, volta de ~20 s, pelotão de ~2 s, jogador = "Player" P6, P20 (Albers) uma volta atrás,
carro 12 (Heidfeld) parado no box de t=62 a 65 s. Linha do tempo (segundos do `--sim`):

| `--sim` | Board |
|---|---|
| 0–1,4 | DriverPlate |
| 2 | LineTower, página 1 sendo preenchida (líder "Lap 2") |
| 5 | página 1 cheia (P1–P8) |
| 8 | página 2 (P9–P16) |
| 12 | página 3 parcial (P17–P20, com "+1L") |
| 16 | SectorGap S2 (Player × Button, split) |
| 20 | DriverPlate |
| 40 | LapComparison (voltas 3/2/1; a 1 sem tempo) |
| 63 | LineTower da volta 5 com o carro 12 no box (`ExpectedCount` 19) |
| 101,47 | LapComparison das voltas 6/5/4 (intervalo curto antes da linha) |

Exemplo (PowerShell), quando o widget existir:
```
$env:AMS2_FAKE_BOARD = "1"; .\Ams2.OverlayHost.exe --png preview-board.png --widget board --sim 12
```
