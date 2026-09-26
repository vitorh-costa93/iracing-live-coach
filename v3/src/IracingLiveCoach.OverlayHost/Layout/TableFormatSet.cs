using Vortice.Win32;
using Vortice.Win32.Graphics.DirectWrite;
using static Vortice.Win32.Apis;

namespace IracingLiveCoach.OverlayHost.Layout;

/// <summary>The name/status/numeric text formats of a table widget (Standings, Relative), plus a
/// cache of variants for the columns and header fields that override the widget's font (typography
/// spec, phase 2). Set <see cref="Override"/> while drawing one column/field, clear it afterwards;
/// the accessors return the widget's shared format when nothing (or nothing different) is overridden.
/// A DirectWrite format's size and weight are immutable, hence one cached format per
/// (kind, family, weight).</summary>
public sealed unsafe class TableFormatSet : IDisposable
{
    private const int Name = 0, Status = 1, Numeric = 2;

    private readonly IDWriteFactory* _factory;
    private readonly IDWriteFontCollection1* _collection;
    private readonly ComPtr<IDWriteTextFormat>[] _shared = new ComPtr<IDWriteTextFormat>[3];
    private readonly Dictionary<(int Kind, string Family, int Weight), ComPtr<IDWriteTextFormat>> _variants = new();
    private WidgetAppearance _appearance = WidgetAppearance.Default;

    /// <summary>Font override of the cell being drawn; null members inherit from the widget.</summary>
    public (string? Family, int? Weight)? Override { get; set; }

    public TableFormatSet(IDWriteFactory* factory, IDWriteFontCollection1* collection)
    {
        _factory = factory;
        _collection = collection;
    }

    public IDWriteTextFormat* NameFormat => Resolve(Name);
    public IDWriteTextFormat* StatusFormat => Resolve(Status);
    public IDWriteTextFormat* NumericFormat => Resolve(Numeric);

    public void Rebuild(WidgetAppearance appearance)
    {
        _appearance = appearance;
        DisposeAll();
        for (int kind = 0; kind < 3; kind++)
            _shared[kind] = Make(kind, appearance.FontFamily, appearance.FontWeight);
    }

    private IDWriteTextFormat* Resolve(int kind)
    {
        if (Override is not { } o) return _shared[kind].Get();
        string family = o.Family ?? _appearance.FontFamily;
        int weight = o.Weight ?? _appearance.FontWeight;
        if (family == _appearance.FontFamily && weight == _appearance.FontWeight) return _shared[kind].Get();
        var key = (kind, family, weight);
        if (!_variants.TryGetValue(key, out var format))
            _variants[key] = format = Make(kind, family, weight);
        return format.Get();
    }

    private ComPtr<IDWriteTextFormat> Make(int kind, string family, int weight)
    {
        var (size, own) = kind switch
        {
            Name => (17f, FontWeight.Medium),
            Status => (15.5f, FontWeight.SemiBold),
            _ => (16f, FontWeight.Medium),
        };
        var format = FontCatalog.CreateFormat(_factory, _collection, family, size * _appearance.FontScale, own, weight);
        ThrowIfFailed(format.Get()->SetParagraphAlignment(ParagraphAlignment.Center));
        ThrowIfFailed(format.Get()->SetTextAlignment(kind switch
        {
            Status => TextAlignment.Center,
            Numeric => TextAlignment.Trailing,
            _ => TextAlignment.Leading,
        }));
        ThrowIfFailed(format.Get()->SetWordWrapping(WordWrapping.NoWrap));
        return format;
    }

    private void DisposeAll()
    {
        for (int i = 0; i < 3; i++) _shared[i].Dispose();
        foreach (var v in _variants.Values) v.Dispose();
        _variants.Clear();
    }

    public void Dispose() => DisposeAll();
}
