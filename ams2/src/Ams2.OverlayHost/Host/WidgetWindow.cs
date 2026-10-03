using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Gfx;
using Ams2.OverlayHost.Native;
using Ams2.OverlayHost.Theme;
using Ams2.OverlayHost.Widgets;
using Ams2.Shared.Profiles;
using HostTheme = Ams2.OverlayHost.Theme.Theme;
using Themes = Ams2.OverlayHost.Theme.Themes;
using Vortice.Win32.Numerics;
using static Ams2.OverlayHost.Native.Win32;

namespace Ams2.OverlayHost.Host;

/// <summary>
/// Uma janela de overlay = um widget (decisão do usuário: nunca compor tudo numa janela única). Guarda janela, device D2D,
/// canvas e as configurações do widget; reconstrói o device quando o tamanho muda (escala ou linhas).
/// No modo de edição o usuário arrasta (com encaixe), redimensiona pela escala (canto ou roda) e o host é avisado por <see cref="UserChanged"/>.
/// </summary>
internal sealed class WidgetWindow : IDisposable
{
    const float ScaleStep = 0.05f;
    const int GripSize = 26;
    static readonly Color4 EditColor = new(0.17f, 0.83f, 0.94f, 1f), EditFill = new(0.17f, 0.83f, 0.94f, 0.85f);

    readonly OverlayWindow _win;
    readonly IWidget _widget;
    DeviceResources _gfx;
    ThemeCanvas _canvas;
    int _w, _h;
    HostTheme _theme;

    enum Drag { None, Move, Resize }
    Drag _drag;
    int _dragStartCursorX, _dragStartCursorY, _dragStartWinX, _dragStartWinY;
    bool _dragged;

    public string Id { get; }
    /// <summary>Medição de fps de render (marca a cada quadro desenhado).</summary>
    public Ams2.Core.Calc.RateStats RenderStats { get; } = new();
    public WidgetSettings Settings { get; private set; }
    public bool Visible => Settings.Visible && _gateOpen;
    bool _gateOpen = true;
    public bool Editing => _win.EditMode;
    /// <summary>Visível e marcado como alta frequência: o host o desenha a cada vblank.</summary>
    public bool HighFrequency => Visible && _widget.HighFrequency;
    /// <summary>Widget de alta frequencia sem nada para desenhar neste quadro (volta ao ritmo de 60 Hz).</summary>
    public bool IsIdle(OverlayModel model) => _widget.IsIdle(model);

    /// <summary>Disparado quando o usuário termina de arrastar ou muda a escala no modo de edição (já com as novas Settings).</summary>
    public event Action<WidgetWindow>? UserChanged;
    /// <summary>Encaixe: dado (janela, x, y, w, h) devolve a posição ajustada.</summary>
    public Func<WidgetWindow, int, int, int, int, (int X, int Y)>? Snap { get; set; }

    public WidgetWindow(string id, WidgetSettings settings, HostTheme theme)
    {
        Id = id;
        _theme = theme;
        _widget = WidgetRegistry.Create(id);
        Settings = settings;
        _widget.UseTheme(theme);
        _widget.Configure(settings);
        (_w, _h) = PixelSize(settings.Scale);
        var (x, y) = ClampToScreen(settings.X, settings.Y, _w, _h);
        _win = OverlayWindow.Create($"AMS2 Overlay - {id}", x, y, _w, _h);
        _win.Mouse = OnMouse;
        _win.IsResizeGrip = (cx, cy) => cx >= _w - GripSize && cy >= _h - GripSize;
        _gfx = DeviceResources.CreateForWindow(_win.Handle, _w, _h);
        _gfx.PresentInterval = 0;
        _canvas = new ThemeCanvas(_gfx, theme, settings.Scale);
        ApplyCanvas();
        _win.SetVisible(Visible);
    }

    (int W, int H) PixelSize(float scale)
    {
        var d = _widget.DesignSize;
        return ((int)Math.Ceiling(d.Width * scale), (int)Math.Ceiling(d.Height * scale));
    }

    void ApplyCanvas()
    {
        _canvas.Theme = _theme;
        _canvas.Scale = Settings.Scale;
        _canvas.Opacity = Settings.Opacity;
        _canvas.FontOverride = Settings.Font;
    }

    public (int X, int Y, int W, int H) Bounds => _win.Bounds;

    /// <summary>Aplica novas configurações/tema ao vivo: reconstrói o device só se o tamanho mudou.</summary>
    public void Apply(WidgetSettings s, HostTheme theme)
    {
        Settings = s;
        _theme = theme;
        _widget.UseTheme(theme);
        _widget.Configure(s);
        var (w, h) = PixelSize(s.Scale);
        var (x, y) = ClampToScreen(s.X, s.Y, w, h);
        if (w != _w || h != _h)
        {
            _w = w; _h = h;
            _win.MoveResize(x, y, w, h);
            Rebuild();
        }
        else if (_drag == Drag.None) _win.Move(x, y);
        ApplyCanvas();
        _win.SetVisible(Visible);
    }

    void Rebuild()
    {
        _canvas.Dispose();
        _gfx.Dispose();
        _gfx = DeviceResources.CreateForWindow(_win.Handle, _w, _h);
        _gfx.PresentInterval = 0;
        _canvas = new ThemeCanvas(_gfx, _theme, Settings.Scale);
        ApplyCanvas();
    }

    /// <summary>Regra central de visibilidade (PlayerDriving ou modo de edicao): fecha/abre a janela sem mexer nas Settings do perfil.</summary>
    public void SetGate(bool open)
    {
        if (_gateOpen == open) return;
        _gateOpen = open;
        _win.SetVisible(Visible);
    }

    public void SetEditMode(bool edit)
    {
        _win.SetEditMode(edit);
        if (!edit) _drag = Drag.None;
    }

    public void BringToTop() => _win.BringToTop();

    public void Render(OverlayModel model)
    {
        if (!Visible) return;
        RenderStats.Mark();
        _gfx.BeginFrame();
        _canvas.Begin();
        _widget.Draw(_canvas, model);
        if (_win.EditMode) DrawEditAdornments();
        _canvas.End();
        _gfx.EndFrame();
    }

    void DrawEditAdornments()
    {
        var (dw, dh) = _widget.DesignSize;
        float s = Settings.Scale;
        float opacity = _canvas.Opacity;
        _canvas.Opacity = 1f;
        _canvas.StrokeRect(0, 0, dw, dh, EditColor, 2f / s);
        float g = GripSize / s;
        _canvas.FillRect(dw - g, dh - g, g, g, EditFill);
        var tag = _theme.Label with { Size = 14f / s };
        string text = $"{Id.ToUpperInvariant()}  {Settings.Scale:0.00}x";
        float tw = _canvas.Measure(text, tag) + 12f / s;
        _canvas.FillRect(0, 0, tw, 20f / s, EditFill);
        _canvas.Text(text, tag, 6f / s, 0, tw, 20f / s, new Color4(0.02f, 0.08f, 0.1f, 1f));
        _canvas.Opacity = opacity;
    }

    // ---- Modo de edição: mouse ----

    void OnMouse(uint msg, int cx, int cy, int wheel)
    {
        switch (msg)
        {
            case WM_LBUTTONDOWN:
            {
                GetCursorPos(out var p);
                var b = _win.Bounds;
                _drag = cx >= _w - GripSize && cy >= _h - GripSize ? Drag.Resize : Drag.Move;
                _dragStartCursorX = p.X; _dragStartCursorY = p.Y; _dragStartWinX = b.X; _dragStartWinY = b.Y;
                _dragged = false;
                SetCapture(_win.Handle);
                break;
            }
            case WM_MOUSEMOVE when _drag != Drag.None:
            {
                GetCursorPos(out var p);
                if (_drag == Drag.Move)
                {
                    int nx = _dragStartWinX + (p.X - _dragStartCursorX), ny = _dragStartWinY + (p.Y - _dragStartCursorY);
                    if ((GetKeyState(VK_MENU) & 0x8000) == 0 && Snap is not null) (nx, ny) = Snap(this, nx, ny, _w, _h);
                    _win.Move(nx, ny);
                    _dragged = true;
                    Settings = Settings with { X = nx, Y = ny };
                }
                else
                {
                    var d = _widget.DesignSize;
                    float scale = (p.X - _dragStartWinX) / d.Width;
                    SetScale(scale);
                }
                break;
            }
            case WM_LBUTTONUP when _drag != Drag.None:
            {
                bool changed = _dragged || _drag == Drag.Resize;
                _drag = Drag.None;
                ReleaseCapture();
                if (changed) UserChanged?.Invoke(this);
                break;
            }
            case WM_MOUSEWHEEL:
                SetScale(Settings.Scale + (wheel > 0 ? ScaleStep : -ScaleStep));
                UserChanged?.Invoke(this);
                break;
        }
    }

    void SetScale(float scale)
    {
        scale = (float)Math.Round(Math.Clamp(scale, WidgetCatalog.MinScale, WidgetCatalog.MaxScale) / ScaleStep) * ScaleStep;
        scale = MathF.Round(scale, 2);
        if (Math.Abs(scale - Settings.Scale) < 0.001f) return;
        var b = _win.Bounds;
        Settings = Settings with { Scale = scale, X = b.X, Y = b.Y };
        var (w, h) = PixelSize(scale);
        _w = w; _h = h;
        _win.MoveResize(b.X, b.Y, w, h);
        Rebuild();
    }

    /// <summary>Mantém ao menos uma faixa da janela dentro da área virtual dos monitores, para nunca ficar inalcançável.</summary>
    public static (int X, int Y) ClampToScreen(int x, int y, int w, int h)
    {
        int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
        if (vw <= 0 || vh <= 0) return (x, y);
        const int keep = 40;
        // Se a janela cabe na tela, mantém inteira dentro dela (o Inputs analógico do 2004 passava da borda inferior);
        // se for maior que a tela, só garante uma faixa visível.
        int minX = w <= vw ? vx : vx - w + keep, maxX = w <= vw ? vx + vw - w : vx + vw - keep;
        int minY = h <= vh ? vy : vy - h + keep, maxY = h <= vh ? vy + vh - h : vy + vh - keep;
        return (Math.Clamp(x, minX, maxX), Math.Clamp(y, minY, maxY));
    }

    public void Dispose()
    {
        _canvas.Dispose();
        _gfx.Dispose();
        _win.Close();
    }
}
