# Relatório de performance: Live Coach V3 × Kapps

Amostragem a cada 10 s durante o sim aberto, em 3 janelas (11:16–12:18, 12:18–14:18, 16:19–18:00, 24/09/2026), com `perf_log.ps1`. Foram cerca de 1.370 amostras por aplicativo. CPU é o percentual da máquina inteira; GPU é a utilização do motor 3D dos processos do app.

| Métrica | Kapps (média / p95) | Live Coach (média / p95) | Relação |
|---|---|---|---|
| CPU (% da máquina) | 0,19 / 0,38 | 0,69 / 1,06 | ~3,6× maior |
| Memória privada (MB) | 620 / 680 | 284 / 308 | ~2,2× menor |
| Working set (MB) | 1.095 / 1.302 | 249 / 278 | ~4,4× menor |
| GPU (%) | 0,10 / 0,34 | 1,85 / 2,99 | ~18× maior |

## Leitura honesta

- **Memória:** nosso app usa bem menos, principalmente no working set (o Kapps roda 11 processos).
- **CPU e GPU:** o nosso consome **mais** que o Kapps, ao contrário do que eu tinha registrado antes ("CPU ≈ igual"). Em valores absolutos ainda é pouco: menos de 1% de CPU e cerca de 2% de GPU, com pico de 5,2%. Ainda assim é uma diferença mensurável.
- Um pico de memória privada de 595 MB apareceu em uma janela (média 284); não investiguei a causa.
- Amostras de CPU negativas (reinício de processo) foram descartadas.

## Onde provavelmente está o custo

O overlay redesenha cada widget com Direct2D em taxa alta e cada widget tem seu próprio `TelemetryReader` (leituras duplicadas do SDK a 60 Hz). Reduzir isso (redesenhar só quando o dado muda, compartilhar um leitor) é o caminho para chegar perto do Kapps. Não medi isso ainda; é hipótese.
