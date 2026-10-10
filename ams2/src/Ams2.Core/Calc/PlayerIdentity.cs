using Ams2.Shared.PlayerNames;

namespace Ams2.Core.Calc;

/// <summary>
/// Identidade de exibicao do carro do jogador. O jogo da ao carro do jogador o nome do perfil ("Vitor COSTA") e nao informa pintura,
/// equipe nem pais; aqui eles vem da pintura escolhida no Control Center (catalogo dos XMLs do jogo). So o <see cref="CarSnapshot.Name"/> muda (todo widget deriva sobrenome, sigla e
/// "M Sobrenome" dele); indices, posicoes e distancias ficam intactos, entao os gaps nao sao afetados.
/// </summary>
public static class PlayerIdentity
{
    /// <summary>Devolve o snapshot com o carro do jogador (Player.Index) com piloto/equipe/pais da pintura se <paramref name="lookup"/> der
    /// uma identidade para o modelo dele; <see cref="CarSnapshot.OriginalName"/> do jogador sempre guarda o nome do jogo. Sem jogador: o mesmo snapshot.</summary>
    public static SessionSnapshot Apply(SessionSnapshot s, Func<string, PlayerNameEntry?> lookup)
    {
        var me = s.PlayerCar;
        if (me is null) return s;
        string original = me.OriginalName.Length > 0 ? me.OriginalName : me.Name;
        var id = lookup(me.CarName);
        var renamed = me with
        {
            Name = string.IsNullOrWhiteSpace(id?.Name) ? original : id.Name.Trim(),
            OriginalName = original,
            TeamName = id?.Team.Trim() ?? "",
            Country = id?.Country.Trim() ?? "",
        };
        if (renamed.Equals(me)) return s;
        var cars = new List<CarSnapshot>(s.Cars.Count);
        foreach (var c in s.Cars) cars.Add(c.Index == me.Index ? renamed : c);
        return s with { Cars = cars };
    }
}
