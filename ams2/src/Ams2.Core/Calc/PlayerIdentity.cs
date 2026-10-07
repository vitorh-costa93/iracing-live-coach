using Ams2.Shared.PlayerNames;

namespace Ams2.Core.Calc;

/// <summary>
/// Nome de exibicao do carro do jogador. O jogo da ao carro do jogador o nome do perfil ("Vitor COSTA"); aqui ele pode ser trocado
/// pelo nome que a IA teria naquele modelo. So o <see cref="CarSnapshot.Name"/> muda (todo widget deriva sobrenome, sigla e
/// "M Sobrenome" dele); indices, posicoes e distancias ficam intactos, entao os gaps nao sao afetados.
/// </summary>
public static class PlayerIdentity
{
    /// <summary>Nome do primeiro participante (por indice) que NAO e o jogador e tem o mesmo modelo de carro (sem sufixo, sem
    /// diferenciar caixa); null se nao houver.</summary>
    public static string? Suggest(SessionSnapshot s)
    {
        var me = s.PlayerCar;
        if (me is null) return null;
        string model = PlayerNameStore.ModelKey(me.CarName);
        if (model.Length == 0) return null;
        CarSnapshot? best = null;
        foreach (var c in s.Cars)
        {
            if (c.Index == me.Index || c.Name.Length == 0) continue;
            if (!string.Equals(PlayerNameStore.ModelKey(c.CarName), model, StringComparison.OrdinalIgnoreCase)) continue;
            if (best is null || c.Index < best.Index) best = c;
        }
        return best?.Name;
    }

    /// <summary>Devolve o snapshot com o carro do jogador (Player.Index) renomeado se <paramref name="lookup"/> der um nome para o
    /// modelo dele; <see cref="CarSnapshot.OriginalName"/> do jogador sempre guarda o nome do jogo. Sem jogador: o mesmo snapshot.</summary>
    public static SessionSnapshot Apply(SessionSnapshot s, Func<string, string?> lookup)
    {
        var me = s.PlayerCar;
        if (me is null) return s;
        string original = me.OriginalName.Length > 0 ? me.OriginalName : me.Name;
        string? display = lookup(me.CarName);
        var renamed = me with { Name = string.IsNullOrWhiteSpace(display) ? original : display.Trim(), OriginalName = original };
        if (renamed.Equals(me)) return s;
        var cars = new List<CarSnapshot>(s.Cars.Count);
        foreach (var c in s.Cars) cars.Add(c.Index == me.Index ? renamed : c);
        return s with { Cars = cars };
    }
}
