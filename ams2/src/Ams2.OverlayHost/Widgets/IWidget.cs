using Ams2.OverlayHost.Data;
using Ams2.Shared.Profiles;
using Ams2.OverlayHost.Theme;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Widget: lê só o modelo neutro e desenha com o tema, em unidades de design.</summary>
public interface IWidget
{
    string Id { get; }
    (float Width, float Height) DesignSize { get; }
    /// <summary>Aplica linhas e colunas visiveis do perfil (muda DesignSize quando o widget tem linhas).</summary>
    /// <summary>Informa o tema antes de Configure/DesignSize (so Inputs muda de tamanho com o estilo).</summary>
    void UseTheme(Theme.Theme theme) { }
    /// <summary>true = o host redesenha este widget na taxa do monitor (sincronizado ao vblank) em vez de 60 Hz fixos.</summary>
    bool HighFrequency => false;
    /// <summary>true = nada a desenhar agora (p.ex. radar sem carro por perto): o host nao precisa do ritmo de vblank para este widget (60 Hz basta para limpar a janela).</summary>
    bool IsIdle(OverlayModel model) => false;
    void Configure(WidgetSettings settings);
    void Draw(ThemeCanvas canvas, OverlayModel model);
}
