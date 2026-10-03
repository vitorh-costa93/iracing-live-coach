using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ams2.Shared.Profiles;

namespace Ams2.ControlCenter;

public abstract class Notify : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ColumnVm : Notify
{
    bool _visible = true;
    public ColumnVm(ColumnDef def) { Def = def; }
    public ColumnDef Def { get; }
    public string Label => Def.Label;
    public bool IsVisible { get => _visible; set { if (Set(ref _visible, value)) Edited?.Invoke(); } }
    public event Action? Edited;
    public void Load(bool v) { _visible = v; Raise(nameof(IsVisible)); }
}

/// <summary>Um widget na lista: espelha <see cref="WidgetSettings"/>; toda mudanca feita pelo usuario dispara <see cref="Edited"/> com o nome da propriedade.</summary>
public sealed class WidgetVm : Notify
{
    public const string FontDefault = "Padrão do tema";

    bool _visible = true, _dropTarget;
    double _scale = 1, _opacity = 1;
    int _x, _y, _rows, _top = WidgetCatalog.DefaultTopCount, _near = WidgetCatalog.DefaultNearCount;
    string _font = FontDefault;
    bool _loading;

    public WidgetVm(WidgetDef def)
    {
        Def = def;
        Columns = new ObservableCollection<ColumnVm>(def.Columns.Select(c => new ColumnVm(c)));
        foreach (var c in Columns) c.Edited += () => { if (!_loading) { Raise(nameof(Summary)); Edited?.Invoke(this, nameof(Columns)); } };
        _rows = def.DefaultRows ?? 0;
    }

    public WidgetDef Def { get; }
    public string Id => Def.Id;
    public string Name => Def.DisplayName;
    public ObservableCollection<ColumnVm> Columns { get; }
    public bool HasColumns => Columns.Count > 0;
    public bool SupportsRows => Def.SupportsRows;
    public string RowsLabel => Def.RowsLabel;
    public int MinRows => Def.MinRows ?? 0;
    public int MaxRows => Def.MaxRows ?? 0;
    /// <summary>Standings: passos de "pilotos no topo" e "perto de mim".</summary>
    public bool HasSelection => Def.HasSelection;
    public bool IsBoard => Def.Id == "board";

    public event Action<WidgetVm, string>? Edited;

    void Edit<T>(ref T field, T value, string prop)
    {
        if (!Set(ref field, value, prop)) return;
        Raise(nameof(Summary));
        if (!_loading) Edited?.Invoke(this, prop);
    }

    public bool Visible { get => _visible; set => Edit(ref _visible, value, nameof(Visible)); }
    public double Scale { get => _scale; set => Edit(ref _scale, Math.Round(Math.Clamp(value, WidgetCatalog.MinScale, WidgetCatalog.MaxScale) / 0.05) * 0.05, nameof(Scale)); }
    /// <summary>0..100 na interface; no perfil e 0..1.</summary>
    public double OpacityPct { get => _opacity * 100; set => Edit(ref _opacity, Math.Round(Math.Clamp(value, WidgetCatalog.MinOpacity * 100, 100)) / 100, nameof(OpacityPct)); }
    public int X { get => _x; set => Edit(ref _x, value, nameof(X)); }
    public int Y { get => _y; set => Edit(ref _y, value, nameof(Y)); }
    public int Rows { get => _rows; set => Edit(ref _rows, Math.Clamp(value, MinRows, Math.Max(MinRows, MaxRows)), nameof(Rows)); }
    public int TopCount { get => _top; set => Edit(ref _top, Math.Clamp(value, 0, WidgetCatalog.MaxTopCount), nameof(TopCount)); }
    public int NearCount { get => _near; set => Edit(ref _near, Math.Clamp(value, 0, WidgetCatalog.MaxNearCount), nameof(NearCount)); }
    public string FontChoice { get => _font; set => Edit(ref _font, value, nameof(FontChoice)); }
    public bool DropTarget { get => _dropTarget; set => Set(ref _dropTarget, value); }

    public string Summary => (Visible ? "" : "oculto · ") + $"{Scale:0.00}x · {OpacityPct:0}%" + (SupportsRows ? $" · {Rows} linhas" : "") + (HasSelection ? $" · top {TopCount} + perto {NearCount}" : "");

    /// <summary>Carrega do modelo sem disparar <see cref="Edited"/>.</summary>
    public void Load(WidgetSettings s)
    {
        _loading = true;
        try
        {
            Visible = s.Visible; Scale = s.Scale; OpacityPct = s.Opacity * 100; X = s.X; Y = s.Y;
            Rows = s.Rows ?? Def.DefaultRows ?? 0;
            TopCount = s.EffectiveTop; NearCount = s.EffectiveNear;
            FontChoice = s.Font ?? FontDefault;
            foreach (var c in Columns) c.Load(s.ColumnVisible(c.Def.Id));
            Raise(nameof(Summary));
        }
        finally { _loading = false; }
    }

    public WidgetSettings ToSettings(int order) => new WidgetSettings
    {
        Id = Id, Visible = Visible, X = X, Y = Y, Scale = (float)Scale, Opacity = (float)(OpacityPct / 100), Order = order,
        Font = FontChoice == FontDefault ? null : FontChoice,
        Rows = SupportsRows ? Rows : null,
        TopCount = HasSelection ? TopCount : null, NearCount = HasSelection ? NearCount : null,
        Columns = Columns.All(c => c.IsVisible) ? null : Columns.Where(c => c.IsVisible).Select(c => c.Def.Id).ToArray(),
    }.Normalized();

    /// <summary>Patch IPC so com o campo que mudou.</summary>
    public WidgetPatch? PatchFor(string prop) => prop switch
    {
        nameof(Visible) => new WidgetPatch { Visible = Visible },
        nameof(Scale) => new WidgetPatch { Scale = (float)Scale },
        nameof(OpacityPct) => new WidgetPatch { Opacity = (float)(OpacityPct / 100) },
        nameof(X) or nameof(Y) => new WidgetPatch { X = X, Y = Y },
        nameof(Rows) => new WidgetPatch { Rows = Rows },
        nameof(TopCount) or nameof(NearCount) => new WidgetPatch { TopCount = TopCount, NearCount = NearCount },
        nameof(FontChoice) => FontChoice == FontDefault ? new WidgetPatch { ClearFont = true } : new WidgetPatch { Font = FontChoice },
        nameof(Columns) => Columns.All(c => c.IsVisible) ? new WidgetPatch { AllColumns = true } : new WidgetPatch { Columns = Columns.Where(c => c.IsVisible).Select(c => c.Def.Id).ToArray() },
        _ => null,
    };
}

public sealed class ProfileItem : Notify
{
    bool _active;
    public ProfileItem(string name) { Name = name; }
    public string Name { get; set; }
    public bool IsActive { get => _active; set => Set(ref _active, value); }
    public override string ToString() => Name;
}

public sealed class ThemeItem
{
    public ThemeItem(ThemeDef def, bool available) { Def = def; IsAvailable = available; }
    public ThemeDef Def { get; }
    public bool IsAvailable { get; }
    public string Label => IsAvailable ? Def.DisplayName : Def.DisplayName + " (em breve)";
    public override string ToString() => Label;
}
