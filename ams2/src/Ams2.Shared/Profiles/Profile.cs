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
    /// <summary>Standings: pilotos mostrados no TOPO da classificacao. Null = padrao (5).</summary>
    public int? TopCount { get; init; }
    /// <summary>Standings: pilotos AO REDOR do jogador (a janela inclui o jogador). Null = padrao (3).</summary>
    public int? NearCount { get; init; }

    /// <summary>Topo efetivo (padrao quando ausente, como em perfis antigos).</summary>
    [JsonIgnore] public int EffectiveTop => TopCount ?? WidgetCatalog.DefaultTopCount;
    [JsonIgnore] public int EffectiveNear => NearCount ?? WidgetCatalog.DefaultNearCount;

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
        int? top = null, near = null;
        if (def is { HasSelection: true })
        {
            top = Math.Clamp(EffectiveTop, 0, WidgetCatalog.MaxTopCount);
            near = Math.Clamp(EffectiveNear, 0, WidgetCatalog.MaxNearCount);
            if (top + near == 0) near = 1; // o jogador sempre aparece
        }
        return this with
        {
            TopCount = top, NearCount = near,
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
    public int? TopCount { get; init; }
    public int? NearCount { get; init; }

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
        TopCount = TopCount ?? s.TopCount,
        NearCount = NearCount ?? s.NearCount,
    }).Normalized();
}

/// <summary>Perfil nomeado de um tema: um <see cref="WidgetSettings"/> por widget.</summary>
public sealed record Profile
{
    /// <summary>2: Standings ganha TopCount/NearCount e o catalogo ganha o widget "board". Perfis v1 carregam normalmente (valores padrao).</summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Name { get; init; } = "Padrão";
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

    /// <summary>Move o widget para a posicao <paramref name="index"/> da ordem de exibicao e renumera 0..n-1.</summary>
    public Profile MoveTo(string id, int index)
    {
        var list = Ordered.ToList();
        var w = list.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (w is null) return this;
        list.Remove(w);
        list.Insert(Math.Clamp(index, 0, list.Count), w);
        return this with { Widgets = list.Select((x, i) => x with { Order = i }).ToList() };
    }

    public Profile WithWidget(WidgetSettings s) => this with { Widgets = Widgets.Select(w => string.Equals(w.Id, s.Id, StringComparison.OrdinalIgnoreCase) ? s : w).ToList() };
}

public static class ProfileFactory
{
    /// <summary>Perfil padrao: posicoes do catalogo escaladas para a tela (referencia 1920x1080).</summary>
    public static Profile CreateDefault(string name, string themeId, int screenWidth = 1920, int screenHeight = 1080)
    {
        double fx = screenWidth / 1920.0, fy = screenHeight / 1080.0;
        var list = WidgetCatalog.All.Select((d, i) => new { d, i, slot = WidgetLayout.Get(themeId, d.Id) }).Select(e => new WidgetSettings
        {
            Id = e.d.Id, Visible = e.d.DefaultVisible, Order = e.i,
            X = (int)Math.Round(e.slot.X * fx), Y = (int)Math.Round(e.slot.Y * fy),
            Rows = e.d.DefaultRows, TopCount = e.d.HasSelection ? WidgetCatalog.DefaultTopCount : null, NearCount = e.d.HasSelection ? WidgetCatalog.DefaultNearCount : null, Scale = (float)Math.Round(e.slot.Scale * fy, 3),
            // f1-2004: mini-torre da transmissao (posicao, sigla, bandeira); gap/classe/pneu ficam opcionais.
            // Standings: so posicao + sigla (2004-2008 com bandeira); gap e classe sao opcionais. Board: 1998 sem bandeira (como a faixa do GP do Brasil 2003).
            Columns = e.d.Id == "standings" && string.Equals(themeId, "f1-2004", StringComparison.OrdinalIgnoreCase) ? ["pos", "name", "flag"]
                : e.d.Id == "standings" ? ["pos", "name"]
                : e.d.Id == "board" && string.Equals(themeId, "f1-1998", StringComparison.OrdinalIgnoreCase) ? ["tyre", "page"]
                // f1-2004: o cluster (tacometro + marcha/pedais + barra de velocidade) e fiel a transmissao; o grafico de 10 s e opcional.
                : e.d.Id == "inputs" && string.Equals(themeId, "f1-2004", StringComparison.OrdinalIgnoreCase) ? ["bars", "gear"]
                // f1-1998: lista vertical e lista por lado sao o padrao; tabela inferior (standings) e barra de tempo dividido (relative) sao opcionais.
                : e.d.Id == "relative" && string.Equals(themeId, "f1-1998", StringComparison.OrdinalIgnoreCase) ? ["pos", "name", "gap"]
                // Widgets de transmissao: coluna "always" = sempre visivel; o padrao e aparecer so nos eventos.
                : e.d.Columns.Any(col => col.Id == "always") ? [] : null,
        }).ToList();
        return new Profile { Name = name, ThemeId = themeId, Widgets = list };
    }
}
