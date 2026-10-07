# Estudo de overlay 1993

Abra `index.html` num navegador. Artefato independente, sem dependências externas ou telemetria. Ainda não é um tema do aplicativo. A tabela de classificação em tela cheia foi retirada do escopo por orientação expressa do usuário em 07/10/2026.

## Observação

Vídeos abertos desde 0:00, no mudo, no navegador integrado do Codex. Inspeção por amostras e uma sequência contínua curta; **não foram assistidos integralmente**. Nenhum vídeo foi baixado. Capturas de referência ficam fora do repositório em `D:/_ffds_temp/AMS2-1993-evidence/`.

| Referência | Instante | Evidência visual |
| --- | --- | --- |
| [Classificação](https://www.youtube.com/watch?v=UbdPUpVUCDA&t=772s) | 12:52 | Jogador SCHUMACHER à esquerda, cronômetro 1'07.9; LEHTO à direita, referência 1'12.830. Faixa cinza translúcida; nomes brancos; tempos amarelos. |
| [Classificação](https://www.youtube.com/watch?v=UbdPUpVUCDA&t=781s) | 13:01 | Resultado 1'12.533; posição (1) e delta −0.297 cianos no centro; LEHTO / 1'12.830 preservados à direita. |
| [Classificação](https://www.youtube.com/watch?v=UbdPUpVUCDA&t=822s) | 13:42 | Cronômetro 44.9 à esquerda; SCHUMACHER / 42.261 à direita; −0.290 ciano central e parcial 41.971 amarelo abaixo. Em amostra posterior, apenas cronômetro à esquerda. |
| [Classificação](https://www.youtube.com/watch?v=UbdPUpVUCDA&t=852s) | 14:12 | Resultado 1'12.266; posição (1) e delta −0.267 centrais; referência própria 1'12.533 à direita. |
| [Classificação](https://www.youtube.com/watch?v=UbdPUpVUCDA&t=1233s) | 20:33 | Retrato, número 6 amarelo, Ayrton SENNA branco, McLAREN FORD amarelo; placa cinza compacta à esquerda. |
| [Corrida de Donington](https://www.youtube.com/watch?v=P1PoF7BwWAY) | 0:02, 2:03, 3:07; amostras 9:17–10:43, 12:47, 25:15, 37:52, 50:30, 75:45, 101:01, 110:18, 116:58 | Não identifiquei placas de cronometragem nessas amostras. Isso não prova ausência no vídeo inteiro; pesquisa ampliada para outras provas. |
| [GP da Alemanha](https://www.youtube.com/watch?v=9cMKCIi7fNc&t=1233s) | 20:33 | Volta mais rápida: título SCHNELLSTE RUNDE ciano, retrato à esquerda, número amarelo, Alain PROST branco, WILLIAMS RENAULT amarelo; tempo 1'45.079 branco; média 233.48 Kmh / 145.08 Mph, unidades cianas. Placa no terço inferior. |
| [GP da Espanha](https://www.youtube.com/watch?v=Ag_ZuKJgbK8&t=4537s) | 75:37 | Legenda compacta sobre câmera a bordo: retrato, número 2 amarelo, Alain PROST branco, WILLIAMS RENAULT amarelo. Confirma o uso em corrida da identidade observada na classificação. |

Pesquisa ampliada pelo buscador do YouTube para `F1 1993 corrida completa`. Além das referências acima, foram amostrados [Brasil completo](https://www.youtube.com/watch?v=oW_nxHt3aVk) (28:02, 56:04, 70:05, 84:06, 98:08) e [Brasil: última volta/pódio](https://www.youtube.com/watch?v=Kf73w9haQ3w) (abertura/chegada). Nenhuma nova placa compacta foi confirmada nessas amostras. Encontrados como fontes adicionais, ainda sem inspeção: [França 1993](https://www.youtube.com/watch?v=em9I-8LUAMk) e [Brasil, outra gravação](https://www.youtube.com/watch?v=ukH1QLojz0E). Uma retransmissão moderna não torna sua moldura/logotipo moderno parte da identidade de 1993.

Fonte tipográfica exata, opacidade original e duração/quadro das animações não determinados. Arial é aproximação para revisão; não há afirmação de equivalência quadro a quadro. O protótipo usa cortes, sem acrescentar deslizamentos não comprovados. Sequência 4 s de volta / 3 s de parcial / 5 s de volta / 4 s de resultado é **demonstrativa**, com passagem de tempo abreviada. Caso lento não observado: mantém referência e usa delta ciano como proposta. Título da volta mais rápida traduzido do alemão para inglês; os números seguem o quadro de referência.

Retrato é símbolo original demonstrativo; fotos, número de corrida histórico e equipe histórica precisariam de configuração do usuário. Não usar `CarIndex` como número real. Logotipos de emissora e patrocinadores não foram reproduzidos. Não incluídos mockups de tabela grande, setor, comparativo de três voltas, Winner ou Pit Timer: não herdar widgets de épocas posteriores sem confirmar placas históricas. Menor quantidade de placas é parte do escopo 1993.

## Comparação com o código atual

- `Widgets/QualiBoardWidget.cs:26` / `Calc/QualiBoardTiming.cs:8`: tema 1998, torre na saída e entre 5–12 s de volta rápida; depois Quali Lap. Resultado elegível por 3 s. Proposta 1993 remove a torre grande desse fluxo e inverte jogador/referência.
- `Widgets/QualiLapWidget.cs:339` / `Calc/QualiLapTracker.cs:101`: split de S1/S2 observado, referências coerentes com volta, resultado após fechamento. Reaproveitar tracking; desenhar os estados compactos observados acima.
- `Widgets/Broadcast98RaceBoard.cs:22`: Winner → Pit → torre → setor → comparativo → legenda. Não importar automaticamente todas essas placas como grafismos históricos de 1993.
- `Calc/BoardSector98Tracker.cs:37`: referência física escolhida no setor anterior; primeiro cruzamento inicia a contagem, segundo congela. Preservar contrato autorizado.
- `Calc/BoardTracker.cs:443`: comparativo de até três voltas a cada três voltas do jogador; visual 1998 dura 6 s. Regra reutilizável, mas duração/gatilho históricos de 1993 não confirmados.
- `Shared/Profiles/Profile.cs`: dimensões e fontes já configuráveis independentemente no produto. Protótipo demonstra fontes de nome/tempo/delta e largura/altura sem escalar os glifos junto à placa.

Os caminhos acima são relativos a `ams2/src/Ams2.OverlayHost`, `Ams2.Core` ou `Ams2.Shared` conforme o módulo. Levantamento do estado local atual; não prova comportamento em pista. Nenhuma alteração em V2/V3 ou no runtime AMS2 nesta etapa.

## Próximo passo

Usuário revisar composição, fontes e proporções. Implementar um widget por etapa após essa revisão. Para fidelidade dos widgets de corrida restantes e animações precisas, localizar trechos em que as placas efetivamente apareçam; não converter propostas em requisitos históricos sem evidência.
