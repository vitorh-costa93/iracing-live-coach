namespace Ams2.Core.Calc;

/// <summary>Uma linha escolhida da classificação: índice na lista ordenada e se há salto de posições antes dela ("...").</summary>
public readonly record struct StandingsPick(int Index, bool GapBefore);

/// <summary>
/// Seleção das linhas do Standings, igual à do V3/iRacing (StandingsSelection): os <c>top</c> primeiros pilotos MAIS uma janela de
/// <c>near</c> pilotos centrada no jogador (a janela inclui o jogador; com 3: um à frente, o jogador e um atrás). O topo e a janela
/// somam ao total (orçamento = top + near); sobreposição é desduplicada e o orçamento restante é preenchido na ordem da classificação.
/// O jogador está sempre visível (mesmo com near = 0). Sem jogador, só o topo + preenchimento. Função pura, sem UI.
/// </summary>
public static class StandingsSelector
{
    /// <param name="count">Pilotos na classificação (índices 0..count-1, ordem de posição).</param>
    /// <param name="playerIndex">Índice do jogador ou -1.</param>
    public static IReadOnlyList<StandingsPick> Select(int count, int playerIndex, int top, int near)
    {
        if (count <= 0) return [];
        top = Math.Max(0, top); near = Math.Max(0, near);
        int budget = Math.Max(1, near + top);
        var picked = new List<int>();
        for (int i = 0; i < Math.Min(Math.Min(top, budget), count); i++) picked.Add(i);

        if (playerIndex >= 0 && playerIndex < count)
        {
            int remaining = Math.Max(1, budget - picked.Count);
            int start = Math.Max(0, playerIndex - remaining / 2);
            start = Math.Min(start, Math.Max(0, count - remaining));
            for (int i = start; i < Math.Min(count, start + remaining); i++) if (!picked.Contains(i)) picked.Add(i);
            // Jogador sempre visível: se a janela cortada pelo orçamento o deixou de fora, entra no lugar do último da janela.
            if (!picked.Contains(playerIndex)) picked.Add(playerIndex);
        }
        for (int i = 0; i < count && picked.Count < budget; i++) if (!picked.Contains(i)) picked.Add(i);

        picked.Sort();
        var result = new List<StandingsPick>(picked.Count);
        for (int k = 0; k < picked.Count; k++) result.Add(new StandingsPick(picked[k], k > 0 && picked[k] - picked[k - 1] > 1));
        return result;
    }
}
