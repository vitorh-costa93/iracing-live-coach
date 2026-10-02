using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Widget: lê só o modelo neutro e desenha com o tema, em unidades de design.</summary>
public interface IWidget
{
    string Id { get; }
    (float Width, float Height) DesignSize { get; }
    void Draw(ThemeCanvas canvas, OverlayModel model);
}
