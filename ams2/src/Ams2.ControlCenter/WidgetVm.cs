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

/// <summary>Largura de uma coluna dimensionavel, em % da largura do tema (100 = padrao).</summary>
public sealed class WidthVm : Notify
{
    int _pct = 100;
    public WidthVm(ColumnDef def) { Def = def; }
    public ColumnDef Def { get; }
    public string Label => Def.Label;
    public int Pct { get => _pct; set { if (Set(ref _pct, (int)Math.Round(Math.Clamp(value, WidgetCatalog.MinWidthPct, WidgetCatalog.MaxWidthPct) / 5.0) * 5)) Edited?.Invoke(); } }
    public event Action? Edited;
    public void Load(int pct) { _pct = pct; Raise(nameof(Pct)); }
}

/// <summary>Opcao propria do tema (<see cref="OptionDef"/>): ComboBox (Choice), CheckBox (Toggle) ou caixa numerica (Number). Valor sempre canonico.</summary>
public sealed class OptionVm : Notify
{
    string _value;
    public OptionVm(OptionDef def) { Def = def; _value = def.Normalize(def.Default) ?? def.Default; }
    public OptionDef Def { get; }
    public string Label => Def.Label;
    public bool IsChoice => Def.Kind == OptionKind.Choice;
    public bool IsToggle => Def.Kind == OptionKind.Toggle;
    public bool IsNumber => Def.Kind == OptionKind.Number;
    public IReadOnlyList<OptionChoice> Choices => Def.Choices ?? [];
    /// <summary>Valor canonico atual (padrao da definicao quando nao configurado).</summary>
    public string Value { get => _value; set { var v = Def.Normalize(value) ?? _value; if (Set(ref _value, v)) { RaiseAll(); Edited?.Invoke(); } } }
    public bool IsDefault => _value == (Def.Normalize(Def.Default) ?? Def.Default);
    /// <summary>Toggle ligado/desligado.</summary>
    public bool Checked { get => _value == "true"; set => Value = value ? "true" : "false"; }
    /// <summary>Number: texto da caixa (aceita virgula ou ponto; invalido mantem o valor anterior).</summary>
    public string NumberText { get => _value; set { Value = value.Replace(',', '.'); Raise(); } }
    public event Action? Edited;
    void RaiseAll() { Raise(nameof(Value)); Raise(nameof(Checked)); Raise(nameof(NumberText)); }
    public void Load(string? stored) { _value = Def.Normalize(stored) ?? Def.Normalize(Def.Default) ?? Def.Default; RaiseAll(); }
    public void Reset() => Value = Def.Default;
}

/// <summary>Opcao de ComboBox (valor inteiro; -1 = padrao do widget/tema).</summary>
public sealed record Choice(string Label, int Value);

/// <summary>Listas fixas das opcoes de formato.</summary>
public static class Choices
{
    public static IReadOnlyList<Choice> Names { get; } =
        [new("Padrão do widget", -1), new("Sigla (3 letras)", (int)NameStyle.Code3), new("Iniciais", (int)NameStyle.Initials),
         new("Inicial + sobrenome", (int)NameStyle.InitialLastName), new("Sobrenome", (int)NameStyle.LastName), new("Nome completo", (int)NameStyle.FullName)];
    public static IReadOnlyList<Choice> Decimals { get; } = [new("Padrão (3)", -1), new("0", 0), new("1", 1), new("2", 2), new("3", 3)];
    public static IReadOnlyList<Choice> Signs { get; } = [new("Padrão do widget", -1), new("Com sinal +/-", 1), new("Sem sinal", 0)];
    public static IReadOnlyList<Choice> LapStyles { get; } = [new("m:ss.mmm (padrão)", -1), new("ss.mmm (só segundos)", (int)LapTimeStyle.Seconds)];
    public static IReadOnlyList<Choice> Speeds { get; } = [new("km/h", 0), new("mph", 1)];
    public static IReadOnlyList<Choice> Temps { get; } = [new("°C", 0), new("°F", 1)];
    public static IReadOnlyList<Choice> Fuels { get; } = [new("Litros", 0), new("Galões (US)", 1)];
    public static IReadOnlyList<Choice> Weights { get; } =
        [new("Padrão do tema", 0), new("Normal (400)", 400), new("Médio (500)", 500), new("Semi-negrito (600)", 600), new("Negrito (700)", 700), new("Extra (800)", 800), new("Black (900)", 900)];
}

/// <summary>Um widget na lista: espelha <see cref="WidgetSettings"/>; toda mudanca feita pelo usuario dispara <see cref="Edited"/> com o nome da propriedade.</summary>
public sealed class WidgetVm : Notify
{
    public const string FontDefault = "Padrão do tema";

    bool _visible = true, _dropTarget;
    double _scale = 1, _opacity = 1;
    int _x, _y, _rows, _top = WidgetCatalog.DefaultTopCount, _near = WidgetCatalog.DefaultNearCount;
    int _radarRange = WidgetCatalog.DefaultRadarRange, _radarSens = WidgetCatalog.DefaultRadarSensitivity;
    string _font = FontDefault;
    bool _loading;

    public WidgetVm(WidgetDef def, string themeId)
    {
        Def = def;
        ThemeOptions = new ObservableCollection<OptionVm>(WidgetCatalog.OptionsFor(themeId, def.Id).Select(o => new OptionVm(o)));
        foreach (var o in ThemeOptions) o.Edited += () => { if (!_loading) Edited?.Invoke(this, nameof(ThemeOptions)); };
        Columns = new ObservableCollection<ColumnVm>(def.Columns.Select(c => new ColumnVm(c)));
        foreach (var c in Columns) c.Edited += () => { if (!_loading) { Raise(nameof(Summary)); Edited?.Invoke(this, nameof(Columns)); } };
        Widths = new ObservableCollection<WidthVm>(def.Widths.Select(c => new WidthVm(c)));
        foreach (var w in Widths) w.Edited += () => { if (!_loading) Edited?.Invoke(this, nameof(Widths)); };
        _rows = def.DefaultRows ?? 0;
    }

    // ---- Texto e formato (personalizacao) ----
    int _textPct = 100, _weight, _name = -1, _gapDec = -1, _gapSign = -1, _lapStyle = -1, _lapDec = -1, _speed, _temp, _fuel;
    bool _carNumber, _gapSuffix;
    string _textColor = "", _labelColor = "", _valueColor = "";

    public ObservableCollection<WidthVm> Widths { get; }
    public bool HasWidths => Widths.Count > 0;
    /// <summary>Opcoes proprias do tema ativo (bloco "Opções do tema"; vazio = bloco oculto).</summary>
    public ObservableCollection<OptionVm> ThemeOptions { get; }
    public bool HasThemeOptions => ThemeOptions.Count > 0;

    /// <summary>Mapa das opcoes fora do padrao (null = todas no padrao).</summary>
    Dictionary<string, string>? BuildOptions()
    {
        var d = ThemeOptions.Where(o => !o.IsDefault).ToDictionary(o => o.Def.Id, o => o.Value, StringComparer.OrdinalIgnoreCase);
        return d.Count == 0 ? null : d;
    }
    public bool HasName => Def.Caps.HasFlag(DisplayCaps.Name);
    public bool HasGap => Def.Caps.HasFlag(DisplayCaps.Gap);
    public bool HasLapTime => Def.Caps.HasFlag(DisplayCaps.LapTime);
    public bool HasSpeed => Def.Caps.HasFlag(DisplayCaps.Speed);
    public bool HasTemp => Def.Caps.HasFlag(DisplayCaps.Temp);
    public bool HasFuel => Def.Caps.HasFlag(DisplayCaps.Fuel);
    public bool HasFormat => Def.Caps != DisplayCaps.None;

    /// <summary>Tamanho do texto em % (o widget inteiro cresce junto).</summary>
    public int TextScalePct { get => _textPct; set => Edit(ref _textPct, (int)Math.Round(Math.Clamp(value, WidgetCatalog.MinTextScale * 100, WidgetCatalog.MaxTextScale * 100) / 5.0) * 5, nameof(TextScalePct)); }
    public int FontWeightChoice { get => _weight; set => Edit(ref _weight, value, nameof(FontWeightChoice)); }
    public string TextColor { get => _textColor; set => Edit(ref _textColor, ColorHex.Normalize(value) ?? "", nameof(TextColor)); }
    public string LabelColor { get => _labelColor; set => Edit(ref _labelColor, ColorHex.Normalize(value) ?? "", nameof(LabelColor)); }
    public string ValueColor { get => _valueColor; set => Edit(ref _valueColor, ColorHex.Normalize(value) ?? "", nameof(ValueColor)); }
    public int NameChoice { get => _name; set => Edit(ref _name, value, nameof(NameChoice)); }
    public bool CarNumber { get => _carNumber; set => Edit(ref _carNumber, value, nameof(CarNumber)); }
    public int GapDecimals { get => _gapDec; set => Edit(ref _gapDec, value, nameof(GapDecimals)); }
    public int GapSign { get => _gapSign; set => Edit(ref _gapSign, value, nameof(GapSign)); }
    public bool GapSuffix { get => _gapSuffix; set => Edit(ref _gapSuffix, value, nameof(GapSuffix)); }
    public int LapStyle { get => _lapStyle; set => Edit(ref _lapStyle, value, nameof(LapStyle)); }
    public int LapDecimals { get => _lapDec; set => Edit(ref _lapDec, value, nameof(LapDecimals)); }
    public int SpeedChoice { get => _speed; set => Edit(ref _speed, value, nameof(SpeedChoice)); }
    public int TempChoice { get => _temp; set => Edit(ref _temp, value, nameof(TempChoice)); }
    public int FuelChoice { get => _fuel; set => Edit(ref _fuel, value, nameof(FuelChoice)); }

    /// <summary>Bloco de formato montado das escolhas (null = tudo padrao).</summary>
    public DisplayOptions? BuildDisplay() => new DisplayOptions
    {
        Name = NameChoice >= 0 ? (NameStyle)NameChoice : null,
        CarNumber = CarNumber ? true : null,
        GapDecimals = GapDecimals >= 0 ? GapDecimals : null,
        GapSign = GapSign >= 0 ? GapSign == 1 : null,
        GapSuffix = GapSuffix ? true : null,
        LapTime = LapStyle >= 0 ? (LapTimeStyle)LapStyle : null,
        LapDecimals = LapDecimals >= 0 ? LapDecimals : null,
        Speed = SpeedChoice == 1 ? SpeedUnit.Mph : null,
        Temp = TempChoice == 1 ? TempUnit.Fahrenheit : null,
        Fuel = FuelChoice == 1 ? FuelUnit.Gallons : null,
    }.Normalized();

    Dictionary<string, int>? BuildWidths()
    {
        var d = Widths.Where(w => w.Pct != 100).ToDictionary(w => w.Def.Id, w => w.Pct, StringComparer.OrdinalIgnoreCase);
        return d.Count == 0 ? null : d;
    }

    static readonly HashSet<string> DisplayProps = [nameof(NameChoice), nameof(CarNumber), nameof(GapDecimals), nameof(GapSign), nameof(GapSuffix), nameof(LapStyle), nameof(LapDecimals), nameof(SpeedChoice), nameof(TempChoice), nameof(FuelChoice)];

    /// <summary>Volta texto, formato e larguras ao padrao (Restaurar padroes do widget).</summary>
    public void ResetCustomization()
    {
        TextScalePct = 100; FontWeightChoice = 0; TextColor = ""; LabelColor = ""; ValueColor = "";
        NameChoice = -1; CarNumber = false; GapDecimals = -1; GapSign = -1; GapSuffix = false; LapStyle = -1; LapDecimals = -1;
        SpeedChoice = 0; TempChoice = 0; FuelChoice = 0;
        foreach (var w in Widths) w.Pct = 100;
        foreach (var o in ThemeOptions) o.Reset();
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
    /// <summary>Radar: alcance em metros e sensibilidade.</summary>
    public bool HasRadarOptions => Def.HasRadarOptions;
    public bool IsRadar => Def.Id == "radar";

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
    public int RadarRange { get => _radarRange; set => Edit(ref _radarRange, Math.Clamp(value, WidgetCatalog.MinRadarRange, WidgetCatalog.MaxRadarRange), nameof(RadarRange)); }
    public int RadarSensitivity { get => _radarSens; set => Edit(ref _radarSens, Math.Clamp(value, WidgetCatalog.MinRadarSensitivity, WidgetCatalog.MaxRadarSensitivity), nameof(RadarSensitivity)); }
    public string RadarSensitivityText => RadarSensitivity switch { 1 => "1 (só lado a lado)", 2 => "2", 3 => "3 (padrão: ~4 m de sobreposição)", 4 => "4", _ => "5 (avisa cedo)" };
    public string FontChoice { get => _font; set => Edit(ref _font, value, nameof(FontChoice)); }
    public bool DropTarget { get => _dropTarget; set => Set(ref _dropTarget, value); }

    public string Summary => (Visible ? "" : "oculto · ") + $"{Scale:0.00}x · {OpacityPct:0}%" + (SupportsRows ? $" · {Rows} linhas" : "") + (HasSelection ? $" · top {TopCount} + perto {NearCount}" : "") + (HasRadarOptions ? $" · {RadarRange} m · sens. {RadarSensitivity}" : "");

    /// <summary>Carrega do modelo sem disparar <see cref="Edited"/>.</summary>
    public void Load(WidgetSettings s)
    {
        _loading = true;
        try
        {
            Visible = s.Visible; Scale = s.Scale; OpacityPct = s.Opacity * 100; X = s.X; Y = s.Y;
            Rows = s.Rows ?? Def.DefaultRows ?? 0;
            TopCount = s.EffectiveTop; NearCount = s.EffectiveNear;
            RadarRange = s.EffectiveRadarRange; RadarSensitivity = s.EffectiveRadarSensitivity;
            FontChoice = s.Font ?? FontDefault;
            foreach (var c in Columns) c.Load(s.ColumnVisible(c.Def.Id));
            foreach (var w in Widths) w.Load((int)Math.Round(s.WidthFactor(w.Def.Id) * 100));
            foreach (var o in ThemeOptions) o.Load(s.Option(o.Def.Id));
            TextScalePct = (int)Math.Round((s.TextScale ?? 1f) * 100);
            FontWeightChoice = s.FontWeight ?? 0;
            TextColor = s.TextColor ?? ""; LabelColor = s.LabelColor ?? ""; ValueColor = s.ValueColor ?? "";
            var f = s.Fmt;
            NameChoice = f.Name is { } n ? (int)n : -1;
            CarNumber = f.CarNumber == true;
            GapDecimals = f.GapDecimals ?? -1;
            GapSign = f.GapSign is { } gs ? (gs ? 1 : 0) : -1;
            GapSuffix = f.GapSuffix == true;
            LapStyle = f.LapTime is { } ls ? (int)ls : -1;
            LapDecimals = f.LapDecimals ?? -1;
            SpeedChoice = f.Speed == SpeedUnit.Mph ? 1 : 0;
            TempChoice = f.Temp == TempUnit.Fahrenheit ? 1 : 0;
            FuelChoice = f.Fuel == FuelUnit.Gallons ? 1 : 0;
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
        RadarRange = HasRadarOptions ? RadarRange : null, RadarSensitivity = HasRadarOptions ? RadarSensitivity : null,
        Columns = Columns.All(c => c.IsVisible) ? null : Columns.Where(c => c.IsVisible).Select(c => c.Def.Id).ToArray(),
        ColumnWidths = BuildWidths(),
        TextScale = TextScalePct / 100f,
        FontWeight = FontWeightChoice > 0 ? FontWeightChoice : null,
        TextColor = TextColor, LabelColor = LabelColor, ValueColor = ValueColor,
        Display = BuildDisplay(),
        Options = BuildOptions(),
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
        nameof(RadarRange) or nameof(RadarSensitivity) => new WidgetPatch { RadarRange = RadarRange, RadarSensitivity = RadarSensitivity },
        nameof(FontChoice) => FontChoice == FontDefault ? new WidgetPatch { ClearFont = true } : new WidgetPatch { Font = FontChoice },
        nameof(Columns) => Columns.All(c => c.IsVisible) ? new WidgetPatch { AllColumns = true } : new WidgetPatch { Columns = Columns.Where(c => c.IsVisible).Select(c => c.Def.Id).ToArray() },
        nameof(Widths) => new WidgetPatch { ColumnWidths = BuildWidths() ?? [] },
        nameof(ThemeOptions) => new WidgetPatch { Options = BuildOptions() ?? [] },
        nameof(TextScalePct) => new WidgetPatch { TextScale = TextScalePct / 100f },
        nameof(FontWeightChoice) => new WidgetPatch { FontWeight = FontWeightChoice },
        nameof(TextColor) => new WidgetPatch { TextColor = TextColor },
        nameof(LabelColor) => new WidgetPatch { LabelColor = LabelColor },
        nameof(ValueColor) => new WidgetPatch { ValueColor = ValueColor },
        _ when DisplayProps.Contains(prop) => new WidgetPatch { Display = BuildDisplay() ?? new DisplayOptions() },
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
