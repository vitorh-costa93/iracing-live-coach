using Ams2.Shared.PlayerNames;

namespace Ams2.ControlCenter;

/// <summary>Uma linha da secao "Nome do piloto": um modelo de carro ja visto com o jogador ao volante.</summary>
public sealed class PlayerNameVm : Notify
{
    string _original = "", _suggested = "", _name = "";
    bool _current;

    public PlayerNameVm(string model) { Model = model; }

    public string Model { get; }
    /// <summary>Nome que o jogo da ao carro do jogador (somente leitura).</summary>
    public string OriginalName { get => _original; set { if (Set(ref _original, value)) Raise(nameof(OriginalLabel)); } }
    public string Suggested
    {
        get => _suggested;
        set { if (Set(ref _suggested, value)) { Raise(nameof(HasSuggestion)); Raise(nameof(SuggestionTip)); } }
    }
    /// <summary>Nome de exibicao em vigor ("" = o do jogo).</summary>
    public string Name { get => _name; set => Set(ref _name, value); }
    public bool IsCurrent { get => _current; set => Set(ref _current, value); }
    /// <summary>true enquanto o usuario edita a caixa de texto: atualizacoes do host nao a sobrescrevem.</summary>
    public bool Editing { get; set; }

    public bool HasSuggestion => _suggested.Length > 0;
    public string OriginalLabel => _original.Length > 0 ? _original : "—";
    public string SuggestionTip => HasSuggestion ? $"Usar \"{_suggested}\" (nome de outro piloto com este mesmo carro)" : "Nenhum outro piloto com este carro nesta sessão";

    public void Load(PlayerNameEntry e, bool current)
    {
        OriginalName = e.OriginalName;
        Suggested = e.Suggested;
        if (!Editing) Name = e.Name;
        IsCurrent = current;
    }
}
