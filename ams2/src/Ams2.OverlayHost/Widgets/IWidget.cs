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
    /// <summary>Taxa maxima (Hz) no ritmo de 60 Hz (widgets nao HighFrequency): conteudo estatico pode pedir menos para poupar GPU/DWM do jogo.</summary>
    double LowRateHz => 60;
    /// <summary>true = nada a desenhar agora (p.ex. radar sem carro por perto): o host nao precisa do ritmo de vblank para este widget (60 Hz basta para limpar a janela).</summary>
    bool IsIdle(OverlayModel model) => false;
    /// <summary>Alvo (Hz) do ritmo de vblank: o Present espera N = refresh/alvo vblanks (<see cref="Ams2.Core.Calc.FramePacing"/>; 0 = todo vblank).</summary>
    double VblankTargetHz => 0;
    /// <summary>Assinatura do proximo quadro: igual a do ultimo apresentado = o host nao desenha nem apresenta (o DComp mantem o quadro). null = sempre desenha.</summary>
    long? FrameSignature(OverlayModel model) => null;
    /// <summary>Regiao alterada pelo ultimo Draw (px do device, ja unida a do quadro anterior). false = quadro inteiro.</summary>
    bool DirtyRegion(List<Ams2.Core.Calc.PixelRect> rects) => false;
    /// <summary>true = a janela fica aberta mesmo com a regra de visibilidade fechada (jogador fora do carro), p.ex. opcao "always" do Live Speed.</summary>
    bool IgnoresDrivingGate => false;
    void Configure(WidgetSettings settings);
    void Draw(ThemeCanvas canvas, OverlayModel model);
}
