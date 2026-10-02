using System.Text.Json.Serialization;

namespace Ams2.Shared.Profiles;

/// <summary>Configuracao de um widget (uma janela do overlay). Nulos em Font/Rows/Columns = padrao do tema/widget.</summary>
public sealed record WidgetSettings
{
    public string Id { get; init; } = "";
    public bool Visible { get; init; } = true;
    public int X { get; init; }
    public int Y { get; init; }
    public float Scale { get; init; } = 1f;
    public float Opacity { get; init; } = 1f;
    public int Order { get; init; }
    /// <summary>Familia de fonte que substitui as fontes de texto do tema (os numeros do tema nao mudam).</summary>
    public string? Font { get; init; }
    /// <summary>Linhas visiveis (Standings; Relative = por lado). Null = padrao.</summary>
    public int? Rows { get; init; }
    /// <summary>Ids das colunas visiveis. Null = todas.</summary>
    public string[]? Columns { get; init; }

    public bool ColumnVisible(string column) => Columns is null || Columns.Contains(column, StringComparer.OrdinalIgnoreCase);

    /// <summary>Forca limites validos (escala, opacidade, linhas, colunas conhecidas).</summary>
    public WidgetSettings Normalized()
    {
        var def = WidgetCatalog.Find(Id);
        int? rows = null;
        if (def is { SupportsRows: true }) rows = Math.Clamp(Rows ?? def.DefaultRows!.Value, def.MinRows!.Value, def.MaxRows!.Value);
        string[]? cols = Columns;
        if (cols is not null && def is not null)
            cols = cols.Where(c => def.Columns.Any(d => string.Equals(d.Id, c, StringComparison.OrdinalIgnoreCase))).Distinct().ToArray();
        return this with
        {
            Scale = float.IsFinite(Scale) ? Math.Clamp(Scale, WidgetCatalog.MinScale, WidgetCatalog.MaxScale) : 1f,
            Opacity = float.IsFinite(Opacity) ? Math.Clamp(Opacity, WidgetCatalog.MinOpacity, WidgetCatalog.MaxOpacity) : 1f,
            Font = string.IsNullOrWhiteSpace(Font) ? null : Font,
            Rows = rows,
            Columns = cols,
        };
    }
}

/// <summary>Alteracao parcial de um widget (IPC): so os campos presentes mudam. ClearFont/AllColumns voltam ao padrao.</summary>
public sealed record WidgetPatch
{
    public bool? Visible { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
    public float? Scale { get; init; }
    public float? Opacity { get; init; }
    public int? Order { get; init; }
    public string? Font { get; init; }
    public bool? ClearFont { get; init; }
    public int? Rows { get; init; }
    public string[]? Columns { get; init; }
    public bool? AllColumns { get; init; }

    public WidgetSettings ApplyTo(WidgetSettings s) => (s with
    {
        Visible = Visible ?? s.Visible,
        X = X ?? s.X,
        Y = Y ?? s.Y,
        Scale = Scale ?? s.Scale,
        Opacity = Opacity ?? s.Opacity,
        Order = Order ?? s.Order,
        Font = ClearFont == true ? null : Font ?? s.Font,
        Rows = Rows ?? s.Rows,
        Columns = AllColumns == true ? null : Columns ?? s.Columns,
    }).Normalized();
}

/// <summary>Perfil nomeado de um tema: um <see cref="WidgetSettings"/> por widget.</summary>
public sealed record Profile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Name { get; init; } = "Padrao";
    public string ThemeId { get; init; } = ThemeCatalog.Default;
    public List<WidgetSettings> Widgets { get; init; } = [];

    public WidgetSettings? Get(string id) => Widgets.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Widgets em ordem de exibicao.</summary>
    [JsonIgnore]
    public IEnumerable<WidgetSettings> Ordered => Widgets.OrderBy(w => w.Order);

    /// <summary>Normaliza todos os widgets e garante que todo widget do catalogo exista (os que faltam entram com o padrao).</summary>
    public Profile Normalized(int screenWidth = 1920, int screenHeight = 1080)
    {
        var defaults = ProfileFactory.CreateDefault(Name, ThemeId, screenWidth, screenHeight);
        var list = new List<WidgetSettings>();
        foreach (var d in WidgetCatalog.All)
            list.Add((Get(d.Id) ?? defaults.Get(d.Id)!).Normalized());
        var ordered = list.OrderBy(w => w.Order).Select((w, i) => w with { Order = i }).ToList();
        return this with { SchemaVersion = CurrentSchemaVersion, Widgets = ordered };
    }

    public Profile WithWidget(WidgetSettings s) => this with { Widgets = Widgets.Select(w => string.Equals(w.Id, s.Id, StringComparison.OrdinalIgnoreCase) ? s : w).ToList() };
}

public static class ProfileFactory
{
    /// <summary>Perfil padrao: posicoes do catalogo escaladas para a tela (referencia 1920x1080).</summary>
    public static Profile CreateDefault(string name, string themeId, int screenWidth = 1920, int screenHeight = 1080)
    {
        double fx = screenWidth / 1920.0, fy = screenHeight / 1080.0;
        var list = WidgetCatalog.All.Select((d, i) => new WidgetSettings
        {
            Id = d.Id, Visible = d.DefaultVisible, Order = i,
            X = (int)Math.Round(d.DefaultX * fx), Y = (int)Math.Round(d.DefaultY * fy),
            Rows = d.DefaultRows,
        }).ToList();
        return new Profile { Name = name, ThemeId = themeId, Widgets = list };
    }
}
