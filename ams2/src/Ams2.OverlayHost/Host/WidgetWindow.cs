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
    /// <summary>Medição de apresentacoes aceitas (nao conta quadros recusados por fila cheia ou device lost).</summary>
    public Ams2.Core.Calc.RateStats RenderStats { get; } = new();
    FrameLog? _frameLog;
    /// <summary>Handle waitable do swap chain atual (0 = indisponivel). Lido a cada volta: muda em Rebuild/device lost.</summary>
    public nint FrameWaitHandle => _gfx.FrameLatencyWaitable;
    /// <summary>Ticks que o laco esperou pelo vblank antes deste quadro (telemetria [FPS]); definido pelo host.</summary>
    public long WaitTicks;
    long _drawCount;
    long? _lastSig;
    bool _fullNext = true;
    readonly List<Ams2.Core.Calc.PixelRect> _dirty = [];
    double _drawSeconds, _presentSeconds;
    public string RenderTiming => _drawCount == 0 ? "" : $"draw={_drawSeconds / _drawCount * 1000:F2}ms present={_presentSeconds / _drawCount * 1000:F2}ms";
    public WidgetSettings Settings { get; private set; }
    public bool Visible => Settings.Visible && SessionAllowed && (_gateOpen || _widget.IgnoresDrivingGate);
    bool _gateOpen = true;
    string? _session;
    /// <summary>Filtro de sessao (<see cref="WidgetSettings.Sessions"/>): o widget aparece no grupo da sessao atual? Sem sessao = sim.
    /// Vale tambem no modo de edicao (para posicionar os de classificacao, edite numa sessao de classificacao ou no menu).</summary>
    public bool SessionAllowed => Settings.ShowsIn(_session);
    public bool Editing => _win.EditMode;
    /// <summary>Visível e marcado como alta frequência: o host o desenha a cada vblank.</summary>
    public bool HighFrequency => Visible && _widget.HighFrequency;
    /// <summary>Apresenta a cada N &gt; 1 vblanks (<see cref="IWidget.VblankTargetHz"/>).</summary>
    public bool Divided => _gfx.PresentInterval > 1;
    /// <summary>Widget de alta frequencia sem nada para desenhar neste quadro (volta ao ritmo de 60 Hz).</summary>
    public bool IsIdle(OverlayModel model) => _widget.IsIdle(model);
    double _lastLow = double.NegativeInfinity;
    /// <summary>Ritmo de 60 Hz: o widget pode pedir menos (<see cref="IWidget.LowRateHz"/>); 2 ms de folga como o acumulador do host.</summary>
    public bool LowDue(double now)
    {
        double rate = _widget.LowRateHz;
        if (rate >= 59.5 || now - _lastLow + 0.002 >= 1.0 / rate) { _lastLow = now; return true; }
        return false;
    }

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
        _show = ShowPlan.For(settings, theme.Id);
        _widget.Configure(ShowPlan.ContentSettings(settings, theme.Id));
        (_w, _h) = PixelSize(settings);
        var (x, y) = ClampToScreen(settings.X, settings.Y, _w, _h);
        _win = OverlayWindow.Create($"AMS2 Overlay - {id}", x, y, _w, _h);
        _win.Mouse = OnMouse;
        _win.IsResizeGrip = (cx, cy) => cx >= _w - GripSize && cy >= _h - GripSize;
        _gfx = CreateDevice();
        _canvas = new ThemeCanvas(_gfx, theme, settings.RenderScale);
        ApplyCanvas();
        _win.SetVisible(Visible);
    }

    /// <summary>Tamanho da janela pelos eixos de geometria; a escala de fonte não altera estes limites.</summary>
    (int W, int H) PixelSize(WidgetSettings settings)
    {
        var d = _widget.DesignSize;
        return ((int)Math.Ceiling(d.Width * settings.ScaleX), (int)Math.Ceiling(d.Height * settings.ScaleY));
    }

    void ApplyCanvas()
    {
        _canvas.Theme = ThemeOverrides.Apply(_theme, Settings);
        _canvas.Scale = Settings.RenderScale;
        _canvas.WidthScale = Settings.WidthScale ?? 1;
        _canvas.HeightScale = Settings.HeightScale ?? 1;
        _canvas.ConfigureTypography(Settings);
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
        _show = ShowPlan.For(s, theme.Id);
        _widget.Configure(ShowPlan.ContentSettings(s, theme.Id));
        var (w, h) = PixelSize(s);
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
        Invalidate();
    }

    void Rebuild()
    {
        _canvas.Dispose();
        _gfx.Dispose();
        _gfx = CreateDevice();
        _canvas = new ThemeCanvas(_gfx, _theme, Settings.RenderScale);
        ApplyCanvas();
        Invalidate();
    }

    DeviceResources CreateDevice()
    {
        var gfx = DeviceResources.CreateForWindow(_win.Handle, _w, _h);
        gfx.PresentInterval = Ams2.Core.Calc.FramePacing.Divisor(PrimaryRefreshHz(), _widget.VblankTargetHz);
        return gfx;
    }

    /// <summary>Regra central de visibilidade (PlayerDriving ou modo de edicao): fecha/abre a janela sem mexer nas Settings do perfil.</summary>
    public void SetGate(bool open)
    {
        if (_gateOpen == open) return;
        _gateOpen = open;
        _win.SetVisible(Visible);
        Invalidate();
    }

    /// <summary>Grupo da sessao atual ("practice"/"qualify"/"race"; null = nao filtra).</summary>
    public void SetSession(string? session)
    {
        if (_session == session) return;
        _session = session;
        _win.SetVisible(Visible);
        Invalidate();
    }

    public void SetEditMode(bool edit)
    {
        _win.SetEditMode(edit);
        if (!edit) _drag = Drag.None;
        Invalidate();
    }

    public void BringToTop() => _win.BringToTop();

    /// <summary>Ha algo novo a apresentar? Sem assinatura (null) ou editando = sempre.</summary>
    public bool NeedsFrame(OverlayModel model) => Editing || _lastSig is null || Signature(model) != _lastSig;

    /// <summary>Exibicao efetiva (gatilho generico: sempre/por evento/periodico controla o alfa da janela inteira).</summary>
    ShowPlan _show = null!;
    float _lastGate = 1f;

    /// <summary>Alfa do gatilho generico (1 = gatilho padrao do widget ou sempre), quantizado em 1/64 para a assinatura.</summary>
    float GateAlpha(OverlayModel model)
    {
        if (_show.IsNative || _show.IsAlways) return 1f;
        float a = _show.GateAlpha(model.Now, id => model.Events?.Last(id));
        return MathF.Round(a * 64f) / 64f;
    }

    /// <summary>Assinatura do widget combinada com o alfa do gatilho; oculto pelo gatilho = assinatura fixa (nada a redesenhar).</summary>
    long? Signature(OverlayModel model)
    {
        float gate = GateAlpha(model);
        if (gate <= 0f) return long.MinValue + 1;
        var sig = _widget.FrameSignature(model);
        return sig is { } v && gate < 1f ? unchecked(v * 1_000_003L + BitConverter.SingleToInt32Bits(gate)) : sig;
    }

    /// <summary>Esquece o ultimo quadro apresentado: o proximo desenha e apresenta inteiro.</summary>
    void Invalidate() { _lastSig = null; _fullNext = true; }

    /// <param name="paced">true = o host ja consumiu o sinal do waitable (laco de vblank).</param>
    public void Render(OverlayModel model, bool paced = false)
    {
        if (!Visible) return;
        bool preview = Editing && !model.PlayerDriving;
        long? sig = null;
        if (!Editing)
        {
            sig = Signature(model);
            if (sig is not null && sig == _lastSig) return;   // nada mudou: o DComp mantem o ultimo quadro
        }
        // Present(N>1) fora do laco de vblank: so com vaga na fila (consome o sinal como o laco faria), para nunca bloquear o host.
        if (!paced && _gfx.PresentInterval > 1 && FrameWaitHandle is var h and not 0 && WaitForSingleObjectEx(h, 0, false) != 0) return;
        if (preview)
        {
            model = LayoutPreview.ForWidget(WidgetCatalog.ParentOf(Id) ?? Id);
            _widget.Configure(LayoutPreview.Settings(Settings));
        }
        float gate = Editing ? 1f : GateAlpha(model);
        if (gate != _lastGate) { _fullNext = true; _lastGate = gate; }
        double started = Ams2.Core.Calc.RateStats.Now();
        _gfx.BeginFrame();
        _canvas.Begin();
        if (gate > 0f)
        {
            _canvas.Opacity = Settings.Opacity * gate;
            _widget.Draw(_canvas, model);
            _canvas.Opacity = Settings.Opacity;
        }
        if (_win.EditMode) DrawEditAdornments(preview);
        _canvas.End();
        double drawn = Ams2.Core.Calc.RateStats.Now();
        bool partial = !_fullNext && !_win.EditMode && _widget.DirtyRegion(_dirty);
        if (_gfx.EndFrame(partial ? System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_dirty) : default))
        {
            RenderStats.Mark();
            if (_widget.HighFrequency) (_frameLog ??= new FrameLog(Id)).Add(_gfx.LastPresentStart, _gfx.LastPresentEnd, WaitTicks);
            _lastSig = sig;
            _fullNext = _win.EditMode;
        }
        else Invalidate();
        _drawCount++;
        _drawSeconds += drawn - started;
        _presentSeconds += Ams2.Core.Calc.RateStats.Now() - drawn;
        if (preview) _widget.Configure(ShowPlan.ContentSettings(Settings, _theme.Id));
    }

    void DrawEditAdornments(bool preview)
    {
        var (dw, dh) = _widget.DesignSize;
        float s = Settings.RenderScale;
        float opacity = _canvas.Opacity;
        _canvas.Opacity = 1f;
        _canvas.StrokeRect(0, 0, dw, dh, EditColor, 2f / s);
        float g = GripSize / s;
        _canvas.FillRect(dw - g, dh - g, g, g, EditFill);
        var tag = _theme.Label with { Size = 14f / s };
        string text = $"{Id.ToUpperInvariant()}  {Settings.Scale:0.00}x" + (preview ? " · PRÉVIA SIMULADA" : "");
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
                    float scale = (p.X - _dragStartWinX) / (d.Width * (Settings.WidthScale ?? 1f));
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
        var (w, h) = PixelSize(Settings);
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
        (_widget as IDisposable)?.Dispose();
        _canvas.Dispose();
        _gfx.Dispose();
        _win.Close();
    }
}
