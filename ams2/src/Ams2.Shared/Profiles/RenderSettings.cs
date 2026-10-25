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
    /// <summary>Opcao de execucao: a janela "pai.peca" desenha o pai so com esta peca (<see cref="ShowPlan.Solo"/>).</summary>
    public const string PieceSoloOption = "pieceSolo";
    /// <summary>Opcao de execucao: pecas do pai com janela propria visivel (separadas por virgula), que o pai deixa de desenhar.</summary>
    public const string PiecesDetachedOption = "piecesDetached";

    /// <summary>Settings de renderizacao de <paramref name="s"/> dentro de <paramref name="profile"/>.</summary>
    public static WidgetSettings For(Profile profile, WidgetSettings s)
    {
        if (WidgetCatalog.ParentOf(s.Id) is { } parentId)
        {
            // Janela de peca: conteudo, Exibicao, sessoes e tipografia do pai; geometria, visibilidade, ordem e opacidade proprias.
            if (profile.Get(parentId) is not { } parent) return s;
            var p = For(profile, parent);   // Solo prevalece sobre a lista de pecas destacadas do pai
            return WithOption(p with
            {
                X = s.X, Y = s.Y, Scale = s.Scale, WidthScale = s.WidthScale, HeightScale = s.HeightScale,
                Visible = s.Visible, Order = s.Order, Opacity = s.Opacity,
            }, PieceSoloOption, s.Id[(parentId.Length + 1)..]);
        }
        if (string.Equals(s.Id, "drivercaption", StringComparison.OrdinalIgnoreCase) && profile.Get("winner") is { } winner)
        {
            double hold = ShowPlan.For(winner, profile.ThemeId).Duration;
            if (hold != ShowCatalog.For(profile.ThemeId, "winner").Duration!.Default)
                s = WithOption(s, WinnerHoldOption, hold.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        string prefix = s.Id + WidgetCatalog.PieceSeparator;
        var detached = profile.Widgets.Where(w => w.Visible && w.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(w => w.Id[prefix.Length..]).ToArray();
        if (detached.Length > 0) s = WithOption(s, PiecesDetachedOption, string.Join(',', detached));
        return s;
    }

    /// <summary>Widgets cujas settings de renderizacao dependem de <paramref name="changedId"/> (alem dele mesmo).</summary>
    public static IEnumerable<string> Dependents(string changedId)
    {
        if (string.Equals(changedId, "winner", StringComparison.OrdinalIgnoreCase)) yield return "drivercaption";
        if (WidgetCatalog.ParentOf(changedId) is { } parent) yield return parent;
    }

    /// <summary>Dependentes dentro do perfil: os de <see cref="Dependents(string)"/> mais as janelas de peca do widget.</summary>
    public static IEnumerable<string> Dependents(Profile profile, string changedId)
    {
        foreach (var d in Dependents(changedId)) yield return d;
        string prefix = changedId + WidgetCatalog.PieceSeparator;
        // A peca muda: o pai e as outras pecas dele (todas leem o pai). O pai muda: as pecas dele.
        string? parent = WidgetCatalog.ParentOf(changedId);
        foreach (var w in profile.Widgets)
            if (w.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || parent is not null && WidgetCatalog.ParentOf(w.Id) == parent && w.Id != changedId)
                yield return w.Id;
    }

    /// <summary>
    /// Entrada nova da janela de peca <paramref name="pieceId"/> (desligada, no lugar e na escala do pai, no fim da ordem); null se a peca
    /// nao existir no tema ou o perfil nao tiver o pai.
    /// </summary>
    public static WidgetSettings? NewPiece(Profile profile, string pieceId)
    {
        if (WidgetCatalog.Find(pieceId, profile.ThemeId) is not { } def || profile.Get(WidgetCatalog.ParentOf(def.Id)!) is not { } parent) return null;
        return new WidgetSettings
        {
            Id = def.Id, Visible = false, X = parent.X, Y = parent.Y, Scale = parent.Scale,
            Order = profile.Widgets.Count == 0 ? 0 : profile.Widgets.Max(w => w.Order) + 1,
        };
    }

    static WidgetSettings WithOption(WidgetSettings s, string key, string value)
        => s with { Options = new Dictionary<string, string>(s.Options ?? [], StringComparer.OrdinalIgnoreCase) { [key] = value } };
}
