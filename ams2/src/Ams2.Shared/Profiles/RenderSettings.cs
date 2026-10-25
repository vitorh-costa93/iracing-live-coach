namespace Ams2.Shared.Profiles;

/// <summary>
/// Settings que cada janela do overlay recebe, compostas a partir do perfil inteiro (o perfil gravado nunca muda):
/// valores que dependem de outro widget (p.ex. a Driver Caption some enquanto o Winner esta na tela, pela duracao do Winner).
/// Perfil sem nada configurado = as proprias settings do widget, sem copia.
/// </summary>
public static class RenderSettings
{
    /// <summary>Opcao de execucao (fora do catalogo, nunca gravada): duracao efetiva do Winner (s), lida pela Driver Caption.</summary>
    public const string WinnerHoldOption = "winnerHold";

    /// <summary>Settings de renderizacao de <paramref name="s"/> dentro de <paramref name="profile"/>.</summary>
    public static WidgetSettings For(Profile profile, WidgetSettings s)
    {
        if (string.Equals(s.Id, "drivercaption", StringComparison.OrdinalIgnoreCase) && profile.Get("winner") is { } winner)
        {
            double hold = ShowPlan.For(winner, profile.ThemeId).Duration;
            if (hold != ShowCatalog.For(profile.ThemeId, "winner").Duration!.Default)
                s = WithOption(s, WinnerHoldOption, hold.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return s;
    }

    /// <summary>Widgets cujas settings de renderizacao dependem de <paramref name="changedId"/> (alem dele mesmo).</summary>
    public static IEnumerable<string> Dependents(string changedId)
    {
        if (string.Equals(changedId, "winner", StringComparison.OrdinalIgnoreCase)) yield return "drivercaption";
    }

    static WidgetSettings WithOption(WidgetSettings s, string key, string value)
        => s with { Options = new Dictionary<string, string>(s.Options ?? [], StringComparer.OrdinalIgnoreCase) { [key] = value } };
}
