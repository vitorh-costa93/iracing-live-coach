using System.Text.Json;
using System.Text.Json.Serialization;
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
    public Profile? Data { get; init; }
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
