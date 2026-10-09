using System.Text.Json;
using System.Text.Json.Serialization;
using Ams2.Shared.PlayerNames;
using Ams2.Shared.Profiles;

namespace Ams2.Shared.Ipc;

public static class IpcCommands
{
    /// <summary>Responde com o estado atual (tambem serve de heartbeat).</summary>
    public const string GetState = "getState";
    /// <summary>Aplica um perfil: por nome (Profile + Theme opcional) ou o perfil inteiro em Data.</summary>
    public const string ApplyProfile = "applyProfile";
    /// <summary>Altera propriedades de um widget ao vivo (Widget + Patch).</summary>
    public const string SetWidget = "setWidget";
    public const string SetTheme = "setTheme";
    public const string SetEditMode = "setEditMode";
    /// <summary>Responde com o estado (incl. PlayerNames); o mesmo que GetState, explicito para o Control Center.</summary>
    public const string GetPlayerNames = "getPlayerNames";
    /// <summary>Define o nome de exibicao do jogador para um modelo de carro (Model + Name); Name vazio limpa.</summary>
    public const string SetPlayerName = "setPlayerName";
    /// <summary>Remove o nome de exibicao do modelo (Model).</summary>
    public const string ClearPlayerName = "clearPlayerName";
    /// <summary>Aplica o nome sugerido a todo modelo visto que ainda nao tem nome.</summary>
    public const string ApplySuggestedNames = "applySuggestedNames";
    /// <summary>Grava a config do tema da vitoria (Victory) no host (victory.json).</summary>
    public const string SetVictory = "setVictory";
    /// <summary>Toca o tema da vitoria: VictoryTheme (Default/Senna/Barrichello/Massa) ou, vazio, o do nome atual do piloto.</summary>
    public const string TestVictory = "testVictory";
    /// <summary>Para o audio do tema da vitoria.</summary>
    public const string StopVictory = "stopVictory";
}

public static class IpcEvents
{
    /// <summary>Estado mudou por iniciativa do host (arrastar/escalar no modo de edicao, atalho de edicao).</summary>
    public const string StateChanged = "stateChanged";
}

/// <summary>Estado do host que o Control Center exibe.</summary>
public sealed record HostState
{
    public bool Fake { get; init; }
    public bool GameConnected { get; init; }
    public string GameStatus { get; init; } = "";
    public string Theme { get; init; } = ThemeCatalog.Default;
    public string ActiveProfile { get; init; } = "";
    public bool EditMode { get; init; }
    public List<WidgetSettings> Widgets { get; init; } = [];
    public List<ThemeDef> Themes { get; init; } = [];
    /// <summary>Nome de exibicao do jogador por modelo de carro: carro atual detectado e modelos ja vistos.</summary>
    public PlayerNamesState PlayerNames { get; init; } = new();
    /// <summary>Grupo da sessao atual do jogo (<see cref="SessionIds"/>); null = sem sessao/tipo invalido (nao filtra).</summary>
    public string? Session { get; init; }
    /// <summary>Widgets com janela escondida pelo filtro de sessao (<see cref="WidgetSettings.Sessions"/>) neste momento.</summary>
    public List<string> HiddenBySession { get; init; } = [];
}

/// <summary>
/// Envelope unico do canal: uma mensagem JSON por linha. Kind = "req" (cliente pede), "res" (host responde, mesmo Id) ou "evt" (host avisa).
/// Versao de protocolo diferente da atual e ignorada, nao adivinhada (mesma regra do V3).
/// </summary>
public sealed record IpcMessage
{
    public int V { get; init; } = IpcProtocol.Version;
    public string Kind { get; init; } = "req";
    public long Id { get; init; }
    public string? Cmd { get; init; }
    public string? Event { get; init; }
    public bool Ok { get; init; } = true;
    public string? Error { get; init; }
    public string? Profile { get; init; }
    public string? Theme { get; init; }
    public string? Widget { get; init; }
    public WidgetPatch? Patch { get; init; }
    public bool? Edit { get; init; }
    /// <summary>Modelo de carro (CarName sem sufixo) de setPlayerName/clearPlayerName.</summary>
    public string? Model { get; init; }
    /// <summary>Nome de exibicao de setPlayerName.</summary>
    public string? Name { get; init; }
    public Profile? Data { get; init; }
    /// <summary>Config do tema da vitoria de setVictory.</summary>
    public Ams2.Shared.Victory.VictoryConfig? Victory { get; init; }
    /// <summary>Tema de testVictory (opcional).</summary>
    public string? VictoryTheme { get; init; }
    public HostState? State { get; init; }
}

public static class IpcProtocol
{
    public const int Version = 1;
    public const string DefaultPipeName = "ams2-live-coach-overlay";

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(IpcMessage m) => JsonSerializer.Serialize(m, Options);

    /// <summary>Null para linha invalida ou de outra versao de protocolo.</summary>
    public static IpcMessage? TryParse(string line)
    {
        try
        {
            var m = JsonSerializer.Deserialize<IpcMessage>(line, Options);
            return m is { V: Version } ? m : null;
        }
        catch (JsonException) { return null; }
    }
}
