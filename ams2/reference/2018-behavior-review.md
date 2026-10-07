# Revisão de comportamento 2018–2021 — 06/10/2026

## Evidência
Vídeos do canal verificado FORMULA 1, examinados no navegador com o som desativado. Foram inspecionados trechos, não todas as transmissões completas.

- [Bahrein 2018](https://www.youtube.com/watch?v=DIm2cqy7-AI), aproximadamente 1:40: torre com sobrenomes completos, sem a coluna de intervalos naquele quadro.
- [Grã-Bretanha 2019, corrida](https://www.youtube.com/watch?v=TjiCXhGuLgw), 2:29: gap ao líder, IN PIT e cartões PIT LANE simultâneos.
- [Grã-Bretanha 2019, classificação](https://www.youtube.com/watch?v=SLdc3XiL9w8), 3:00–3:15: cronômetro inferior, setores coloridos e passagem para resultado preservando a placa.
- [Áustria 2020](https://www.youtube.com/watch?v=1Q470aUwi3E), aproximadamente 1:53: coluna de gaps e bloco OUT, mesmo vocabulário gráfico.
- [Monza 2021, versão estendida](https://www.youtube.com/watch?v=gPkIfNtro7I), 8:09–8:11: valores retidos por linha; 10:14–10:15: movimento vertical na troca HAM/RUS/VER. Legendas, box e bandeirada também comparados com os quadros já documentados em `f1-2018-analysis.md`.

Na sequência avançada em quadros de 40 ms de Monza, 489.807 s mostra PER +3.259, SAI +7.982 e RUS +2.718; 490.207 s mostra +3.211, +7.818 e +4.014; esses três valores permanecem em 490.607 s. Em 491.007 s ALO/VER mudam de posição. Isso prova retenção entre quadros e atualizações independentes; não prova um período universal de 1 s. Seeks isolados podem exibir quadros anteriores durante carregamento e não foram usados para cronometrar entradas.

## Implementação e inventário

| Widgets/modos | Resultado da revisão |
| --- | --- |
| Standings | Gap e interval amostrados por carro/referência; padrão 1 s, opção `intervalSeconds` de 0.25–3 s. Posição, pit, OUT, voltas e dado ausente imediatos. Movimento de linhas por identidade; BATTLE e bandeirada preservam geometria com troca direta. |
| Board: torre, setor, voltas, legenda | Torre conserva os gaps de passagem congelados pelo Core. Somente o gap de setor ainda ao vivo recebe retenção; split final imediato. Troca sequencial de conteúdos com revelação horizontal e identidade estável da janela/par. |
| Driver Caption, Winner, Pit Stops, Race Start, Race Control | Revelação/recolhimento horizontal; eventos sobrepostos estendem a exibição sem repetir a entrada. Prévia de Race Control agora fornece bandeira amarela fictícia para mostrar a composição. |
| Pit Timer | `showPitTime` mostra desde a entrada na pit lane, preservando a placa entre parada/liberação/saída; retenção após saída. Desligado, continua restrito à parada. Cronômetros não recebem sample-hold. |
| Live Speed | Entrada da placa ao conectar; velocidade contínua. Prévia/always assentados. |
| Quali Tower | Linhas normais e zona de eliminação se movem por identidade; cartão DRIVER AT RISK se revela quando troca de piloto. Relógio contínuo e melhores tempos autoritativos imediatos. |
| Quali Lap | Placa/auxiliar de setor com revelação; running→resultado não repete entrada. Comparação negativa válida verde; inválida sem indicação verde de ganho. |
| Quali Result | Revelação horizontal; atualizações de resultado estendem a janela sem reiniciar a entrada. |
| Relative, Fuel, Tyres, Weather, Inputs, Radar, Lap Counter | Composição conferida nas prévias; atualizações contínuas preservadas. São ferramentas de apoio ou elementos persistentes, sem contraparte direta suficiente nesses trechos para inventar animações de TV. |

`Broadcast18ValueHold`, `Broadcast18RowMotion`, `Broadcast18Motion` e `Broadcast18BoardPresentation` não alteram a telemetria bruta. Sessão/contexto/relógio e referência invalidam a retenção. Outras épocas preservadas.

## Validação e limites

- Core 209 testes e Shared 142 aprovados; integração focada 11 testes, incluindo renderização dos 19 widgets 2018 e regressão dos widgets 2004, serialmente. Build Host/Control Center aprovado.
- Teste gráfico verifica retenção real dos pixels de interval até o período, mudança imediata para IN PIT, presença do timer antes de parar, continuidade ao liberar e verde válido/ausência de verde inválido na comparação de quali.
- Imagens de revisão fora do repositório: `%TEMP%/AMS2-2018-review`. Publicação pelo script AMS2 e verificação dos espelhos LocalAppData/Desktop.
- Entrada .24 s, saída .20 s, deslocamento de linhas .28 s e cadência padrão 1 s são aproximações de apresentação, não equivalência quadro a quadro certificada. Board inferior é adaptação do aplicativo, não reprodução comprovada de um Board único da F1 moderna.
- Sem novo teste no jogo. Retratos/logos/radio/minimapa e identificação específica de Safety Car não foram fabricados onde a fonte de dados não fornece informação suficiente. A suíte completa de integração com múltiplos hosts/gráficos não foi executada nesta etapa.
