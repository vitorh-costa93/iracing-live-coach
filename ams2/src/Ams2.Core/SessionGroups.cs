using Ams2.Shared.Profiles;

namespace Ams2.Core;

/// <summary>Grupo de sessao usado pelo filtro de visibilidade dos widgets (<see cref="WidgetSettings.Sessions"/>).</summary>
public enum SessionGroup { Practice, Qualify, Race }

public static class SessionGroups
{
    /// <summary>Practice/Test/TimeAttack = Practice; Qualify = Qualify; FormationLap/Race = Race; Invalid (ou desconhecido) = null (nao filtra).</summary>
    public static SessionGroup? From(SessionKind kind) => kind switch
    {
        SessionKind.Practice or SessionKind.Test or SessionKind.TimeAttack => SessionGroup.Practice,
        SessionKind.Qualify => SessionGroup.Qualify,
        SessionKind.FormationLap or SessionKind.Race => SessionGroup.Race,
        _ => null,
    };

    /// <summary>Id gravado no perfil (<see cref="SessionIds"/>).</summary>
    public static string Id(SessionGroup g) => g switch
    {
        SessionGroup.Practice => SessionIds.Practice,
        SessionGroup.Qualify => SessionIds.Qualify,
        _ => SessionIds.Race,
    };

    /// <summary>Id do grupo da sessao (null = sem sessao ou tipo invalido: nao filtra).</summary>
    public static string? IdOf(SessionKind? kind) => kind is { } k && From(k) is { } g ? Id(g) : null;
}
