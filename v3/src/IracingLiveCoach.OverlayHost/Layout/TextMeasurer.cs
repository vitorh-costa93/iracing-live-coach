using Vortice.Win32;
using Vortice.Win32.Graphics.DirectWrite;

namespace IracingLiveCoach.OverlayHost.Layout;

/// <summary>
/// Thin DirectWrite wrapper measuring real rendered text (spec §15: "meça o texto efetivamente
/// renderizado", §12: "largura automática de tabela = soma das colunas visíveis..." depends on
/// knowing how wide content actually is, not an assumed average character width).
/// <see cref="WidgetLayoutEngine"/> stays free of this dependency on purpose — this class is the
/// one thing in the layout stack that touches a live DirectWrite factory/format, so the pure
/// geometry math can keep being unit-tested without one.
/// Deliberately NOT cached by this class: caching belongs to whoever owns the (text, format) pairs
/// that actually repeat across frames (e.g. a widget's static labels) — see spec §3's "cacheie...
/// textos estáticos", which this type enables but does not itself decide the cache policy for.
/// </summary>
public sealed unsafe class TextMeasurer
{
    private readonly IDWriteFactory* _factory;

    public TextMeasurer(IDWriteFactory* factory) => _factory = factory;

    /// <summary>Measures <paramref name="text"/> as it would actually render with <paramref name="format"/>,
    /// unconstrained (a very large layout box, so wrapping never kicks in) — the natural width/height
    /// a single-line cell needs. Returns (width, height) in DIPs.</summary>
    public (float Width, float Height) Measure(string text, IDWriteTextFormat* format)
    {
        fixed (char* pText = text)
        {
            IDWriteTextLayout* layout;
            var hr = _factory->CreateTextLayout(pText, (uint)text.Length, format, float.MaxValue, float.MaxValue, &layout);
            if (hr.Failure) return (0f, 0f);

            try
            {
                TextMetrics metrics;
                Vortice.Win32.Apis.ThrowIfFailed(layout->GetMetrics(&metrics));
                return (metrics.width, metrics.height);
            }
            finally
            {
                layout->Release();
            }
        }
    }
}
