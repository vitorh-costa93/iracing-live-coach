using Ams2.Shared.PlayerNames;

namespace Ams2.ControlCenter;

/// <summary>Uma linha da secao "Piloto e pintura": um modelo de carro ja visto com o jogador ao volante e a pintura escolhida para ele.</summary>
public sealed class PlayerNameVm : Notify
{
    string _original = "", _livery = "", _name = "", _country = "", _team = "";
    bool _current;

    public PlayerNameVm(string model) { Model = model; }

    public string Model { get; }
    /// <summary>Nome que o jogo da ao carro do jogador (somente leitura).</summary>
    public string OriginalName { get => _original; set { if (Set(ref _original, value)) Raise(nameof(OriginalLabel)); } }
    public string Livery { get => _livery; set => Set(ref _livery, value); }
    /// <summary>Piloto em vigor ("" = o nome do jogo).</summary>
    public string Name { get => _name; set { if (Set(ref _name, value)) { Raise(nameof(IdentityLabel)); Raise(nameof(HasIdentity)); } } }
    public string Country { get => _country; set { if (Set(ref _country, value)) { Raise(nameof(IdentityLabel)); Raise(nameof(HasIdentity)); } } }
    public string Team { get => _team; set { if (Set(ref _team, value)) { Raise(nameof(IdentityLabel)); Raise(nameof(HasIdentity)); } } }
    public bool IsCurrent { get => _current; set => Set(ref _current, value); }

    public string OriginalLabel => _original.Length > 0 ? _original : "—";
    public bool HasIdentity => _name.Length > 0 || _country.Length > 0 || _team.Length > 0;
    /// <summary>"Kimi Raikkonen · McLaren · FIN" (so as partes preenchidas) ou o aviso de que vale o nome do jogo.</summary>
    public string IdentityLabel => HasIdentity
        ? string.Join("  ·  ", new[] { _name, _team, _country }.Where(p => p.Length > 0))
        : "Nenhuma pintura escolhida (vale o nome do jogo)";

    public void Load(PlayerNameEntry e, bool current)
    {
        OriginalName = e.OriginalName;
        Livery = e.Livery;
        Name = e.Name;
        Country = e.Country;
        Team = e.Team;
        IsCurrent = current;
    }
}
