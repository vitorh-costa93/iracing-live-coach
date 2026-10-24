using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Exibição efetiva de um widget (durações, fades, forma e peças do <see cref="ShowCatalog"/> + o bloco "show" do perfil), recalculada só quando
/// as settings ou o estilo mudam. Os padrões dependem só do estilo (2004 e 2012 têm os mesmos), então o widget não precisa do id do tema.
/// </summary>
public sealed class ShowRef
{
    WidgetSettings? _cfg;
    ThemeStyle _style;
    ShowPlan? _plan;

    public static string ThemeOf(ThemeStyle style) => style switch
    {
        ThemeStyle.Modern2018 => "f1-2018",
        ThemeStyle.Broadcast2000s => "f1-2004",
        ThemeStyle.Broadcast93 => "f1-1993",
        _ => "f1-1998",
    };

    public ShowPlan Get(WidgetSettings cfg, ThemeStyle style)
    {
        if (_plan is null || !ReferenceEquals(cfg, _cfg) || style != _style)
        {
            _cfg = cfg; _style = style;
            _plan = ShowPlan.For(cfg, ThemeOf(style));
        }
        return _plan;
    }
}
