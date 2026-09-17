using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.Direct3D;
using Vortice.Win32.Graphics.Direct3D11;
using Vortice.Win32.Graphics.DirectComposition;
using Vortice.Win32.Graphics.DirectWrite;
using Vortice.Win32.Graphics.Dxgi;
using Vortice.Win32.Graphics.Dxgi.Common;
using Vortice.Win32.Numerics;
using static Vortice.Win32.Apis;
using static Vortice.Win32.Graphics.Direct2D.Apis;
using static Vortice.Win32.Graphics.Direct3D11.Apis;
using static Vortice.Win32.Graphics.DirectComposition.Apis;
using static Vortice.Win32.Graphics.DirectWrite.Apis;
using static Vortice.Win32.Graphics.Dxgi.Apis;
using DWriteFactoryType = Vortice.Win32.Graphics.DirectWrite.FactoryType;
using D2DFactoryType = Vortice.Win32.Graphics.Direct2D.FactoryType;
using FeatureLevel = Vortice.Win32.Graphics.Direct3D.FeatureLevel;
using DxgiFormat = Vortice.Win32.Graphics.Dxgi.Common.Format;
using DxgiUsage = Vortice.Win32.Graphics.Dxgi.Usage;
using DxgiAlphaMode = Vortice.Win32.Graphics.Dxgi.Common.AlphaMode;
using D2DPixelFormat = Vortice.Win32.Graphics.Direct2D.Common.PixelFormat;
using D2DEllipse = Vortice.Win32.Graphics.Direct2D.Ellipse;
using DxgiSurface = Vortice.Win32.Graphics.Dxgi.IDXGISurface;

namespace IracingLiveCoach.OverlayHost;

/// <summary>
/// Owns the DirectComposition + D3D11 + D2D + DirectWrite chain for one overlay window.
/// Phase 1: survives device-lost (see <see cref="HandleDeviceLost"/>) by tearing down and
/// rebuilding every GPU resource against the same HWND/visual-tree slot, without a process
/// restart -- required by spec section 3 ("preveja device lost, desconexão do simulador...").
/// </summary>
public sealed unsafe class DeviceResources : IDisposable
{
    // These fields own their COM references (plain struct-copy transfer, no extra AddRef) --
    // the locals that produce them in Initialize() are deliberately NOT `using`, since disposing
    // them there would Release() the only reference and leave these fields dangling.
    private ComPtr<ID3D11Device> _d3dDevice;
    private ComPtr<IDXGIDevice> _dxgiDevice;
    private ComPtr<IDXGIFactory2> _dxgiFactory;
    private ComPtr<IDXGISwapChain1> _swapChain;
    private ComPtr<IDCompositionDesktopDevice> _dcompDevice;
    private ComPtr<IDCompositionTarget> _target;
    private ComPtr<IDCompositionVisual2> _rootVisual;
    private ComPtr<ID2D1Factory1> _d2dFactory;
    private ComPtr<ID2D1Device> _d2dDevice;
    private ComPtr<ID2D1DeviceContext> _dc;
    private ComPtr<IDWriteFactory> _dwriteFactory;
    private ComPtr<IDWriteTextFormat> _textFormat;
    private ComPtr<ID2D1SolidColorBrush> _textBrush;
    private ComPtr<ID2D1SolidColorBrush> _dotBrush;

    private nint _hwnd;
    private int _width;
    private int _height;

    // DXGI_ERROR_DEVICE_REMOVED / _RESET / _HUNG -- stable, documented Win32 HRESULT values,
    // not exposed as named constants anywhere in the Vortice.Win32 bindings.
    private const int DXGI_ERROR_DEVICE_REMOVED = unchecked((int)0x887A0005);
    private const int DXGI_ERROR_DEVICE_RESET = unchecked((int)0x887A0007);
    private const int DXGI_ERROR_DEVICE_HUNG = unchecked((int)0x887A0006);

    /// <summary>Raised right after a device-lost recovery completes, so callers (e.g. a future
    /// layout engine) know to invalidate any GPU-resident caches (fonts, icon bitmaps) tied to
    /// the old device -- see spec section 18's "recrie recursos dependentes da GPU após device lost".</summary>
    public event Action? DeviceRecovered;

    /// <summary>Exposed so widget code (Phase 3+) can draw with the same device context this class
    /// owns, without each widget standing up its own device chain. Valid only between frames'
    /// <see cref="RenderFrame"/> BeginDraw/EndDraw -- widgets are drawn from inside that call.</summary>
    public ID2D1DeviceContext* Context => _dc.Get();

    /// <summary>Exposed for widget code to build its own <see cref="Layout.TextMeasurer"/> and
    /// <c>IDWriteTextFormat</c>s against the same factory this class owns.</summary>
    public IDWriteFactory* DWriteFactory => _dwriteFactory.Get();

    private DeviceResources() { }

    public static DeviceResources Create(nint hwnd, int width, int height)
    {
        var self = new DeviceResources();
        self.Initialize(hwnd, width, height);
        return self;
    }

    private void Initialize(nint hwnd, int width, int height)
    {
        _hwnd = hwnd;
        _width = width;
        _height = height;

        ReadOnlySpan<FeatureLevel> featureLevels = [FeatureLevel.Level_11_0];
        ComPtr<ID3D11Device> device = default;
        using ComPtr<ID3D11DeviceContext> immediateContext = default;
        FeatureLevel achievedLevel;
        ThrowIfFailed(D3D11CreateDevice(
            null,
            DriverType.Hardware,
            CreateDeviceFlags.BgraSupport,
            featureLevels,
            device.GetAddressOf(),
            &achievedLevel,
            immediateContext.GetAddressOf()));
        _d3dDevice = device;

        ComPtr<IDXGIDevice> dxgiDevice = default;
        ThrowIfFailed(_d3dDevice.As(ref dxgiDevice));
        _dxgiDevice = dxgiDevice;

        ComPtr<IDXGIFactory2> dxgiFactory = default;
        ThrowIfFailed(CreateDXGIFactory2(CreateFactoryFlags.None, __uuidof<IDXGIFactory2>(), (void**)dxgiFactory.GetAddressOf()));
        _dxgiFactory = dxgiFactory;

        var swapChainDesc = new SwapChainDescription1
        {
            Width = (uint)width,
            Height = (uint)height,
            Format = DxgiFormat.B8G8R8A8Unorm,
            Stereo = false,
            SampleDesc = new SampleDescription(1, 0),
            BufferUsage = DxgiUsage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = DxgiAlphaMode.Premultiplied,
            Flags = SwapChainFlags.None
        };
        ComPtr<IDXGISwapChain1> swapChain = default;
        ThrowIfFailed(_dxgiFactory.Get()->CreateSwapChainForComposition(
            (IUnknown*)_d3dDevice.Get(), &swapChainDesc, null, swapChain.GetAddressOf()));
        _swapChain = swapChain;

        ComPtr<IDCompositionDesktopDevice> dcompDevice = default;
        ThrowIfFailed(DCompositionCreateDevice3(
            (IUnknown*)_dxgiDevice.Get(), __uuidof<IDCompositionDesktopDevice>(), (void**)dcompDevice.GetAddressOf()));
        _dcompDevice = dcompDevice;

        ComPtr<IDCompositionTarget> target = default;
        ThrowIfFailed(_dcompDevice.Get()->CreateTargetForHwnd(hwnd, true, target.GetAddressOf()));
        _target = target;

        ComPtr<IDCompositionVisual2> visual = default;
        ThrowIfFailed(_dcompDevice.Get()->CreateVisual(visual.GetAddressOf()));
        _rootVisual = visual;
        ThrowIfFailed(((IDCompositionVisual*)_rootVisual.Get())->SetContent((IUnknown*)_swapChain.Get()));
        ThrowIfFailed(_target.Get()->SetRoot((IDCompositionVisual*)_rootVisual.Get()));
        ThrowIfFailed(_dcompDevice.Get()->Commit());

        ComPtr<ID2D1Factory1> d2dFactory = default;
        ThrowIfFailed(D2D1CreateFactory(D2DFactoryType.SingleThreaded, __uuidof<ID2D1Factory1>(), null, (void**)d2dFactory.GetAddressOf()));
        _d2dFactory = d2dFactory;

        ComPtr<ID2D1Device> d2dDevice = default;
        ThrowIfFailed(_d2dFactory.Get()->CreateDevice((Vortice.Win32.Graphics.Dxgi.IDXGIDevice*)_dxgiDevice.Get(), d2dDevice.GetAddressOf()));
        _d2dDevice = d2dDevice;

        ComPtr<ID2D1DeviceContext> dc = default;
        ThrowIfFailed(_d2dDevice.Get()->CreateDeviceContext(DeviceContextOptions.None, dc.GetAddressOf()));
        _dc = dc;

        ComPtr<IDWriteFactory> dwriteFactory = default;
        ThrowIfFailed(DWriteCreateFactory(DWriteFactoryType.Shared, __uuidof<IDWriteFactory>(), (void**)dwriteFactory.GetAddressOf()));
        _dwriteFactory = dwriteFactory;

        ComPtr<IDWriteTextFormat> textFormat = _dwriteFactory.Get()->CreateTextFormat(
            "Segoe UI", 28.0f, fontWeight: FontWeight.SemiBold, localeName: "en-us");
        _textFormat = textFormat;

        var white = new Color4(1f, 1f, 1f, 1f);
        ComPtr<ID2D1SolidColorBrush> textBrush = default;
        ThrowIfFailed(_dc.Get()->CreateSolidColorBrush(&white, null, textBrush.GetAddressOf()));
        _textBrush = textBrush;

        var cyan = new Color4(0f, 0.788f, 0.910f, 1f); // #00C9E8, player-highlight token from spec §16
        ComPtr<ID2D1SolidColorBrush> dotBrush = default;
        ThrowIfFailed(_dc.Get()->CreateSolidColorBrush(&cyan, null, dotBrush.GetAddressOf()));
        _dotBrush = dotBrush;

        BindTargetBitmap();
    }

    private void BindTargetBitmap()
    {
        using ComPtr<DxgiSurface> surface = default;
        ThrowIfFailed(_swapChain.Get()->GetBuffer(0, __uuidof<DxgiSurface>(), (void**)surface.GetAddressOf()));

        var bitmapProps = new BitmapProperties1
        {
            pixelFormat = new D2DPixelFormat(DxgiFormat.B8G8R8A8Unorm, Vortice.Win32.Graphics.Direct2D.Common.AlphaMode.Premultiplied),
            dpiX = 96f,
            dpiY = 96f,
            bitmapOptions = BitmapOptions.Target | BitmapOptions.CannotDraw
        };
        using ComPtr<ID2D1Bitmap1> bitmap = default;
        ThrowIfFailed(_dc.Get()->CreateBitmapFromDxgiSurface(surface.Get(), &bitmapProps, bitmap.GetAddressOf()));
        _dc.Get()->SetTarget((ID2D1Image*)bitmap.Get());
    }

    /// <summary>
    /// Tears down every GPU-dependent COM object and rebuilds them from scratch against the same
    /// HWND/size, without recreating the window itself. Call this when <see cref="RenderFrame"/>
    /// reports device loss. Per spec section 18, anything caching GPU-resident bitmaps (fonts,
    /// icons) must re-create them after <see cref="DeviceRecovered"/> fires -- this class only
    /// owns the device/swapchain/visual chain, not those higher-level caches.
    /// </summary>
    public void HandleDeviceLost()
    {
        ReleaseGpuResources();
        Initialize(_hwnd, _width, _height);
        DeviceRecovered?.Invoke();
    }

    /// <summary>Starts a frame: BeginDraw + clear to fully transparent. Pair with <see cref="EndFrame"/>;
    /// draw calls (widgets, or <see cref="DrawPhase0Proof"/>) go in between, using <see cref="Context"/>.</summary>
    public void BeginFrame()
    {
        _dc.Get()->BeginDraw();
        var transparent = new Color4(0, 0, 0, 0);
        _dc.Get()->Clear(&transparent);
    }

    /// <returns>True if the frame presented normally; false if a device-lost condition was
    /// detected and recovery was triggered -- the caller should simply try again next frame.</returns>
    public bool EndFrame()
    {
        var endDrawResult = _dc.Get()->EndDraw();
        if (IsDeviceLost(endDrawResult))
        {
            HandleDeviceLost();
            return false;
        }
        ThrowIfFailed(endDrawResult);

        var presentResult = _swapChain.Get()->Present(1, PresentFlags.None);
        if (IsDeviceLost(presentResult))
        {
            HandleDeviceLost();
            return false;
        }
        ThrowIfFailed(presentResult);

        _dcompDevice.Get()->Commit();
        return true;
    }

    /// <summary>The original Phase 0 proof content (static text + moving dot), extracted verbatim
    /// so it can still be driven from <see cref="Program"/> for a quick transparency/pacing sanity
    /// check, now that Phase 3 widgets draw through <see cref="BeginFrame"/>/<see cref="EndFrame"/> instead.</summary>
    public void DrawPhase0Proof(double angle, bool clickThrough)
    {
        string text = $"V3 PROOF  |  click-through: {(clickThrough ? "ON" : "OFF")} (SPACE toggles)";
        fixed (char* pText = text)
        {
            var layoutRect = new RectF(12, 8, 468, 60);
            _dc.Get()->DrawText(pText, (uint)text.Length, _textFormat.Get(), &layoutRect,
                (ID2D1Brush*)_textBrush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }

        float cx = 240 + (float)(180 * Math.Cos(angle * Math.PI / 180.0));
        float cy = 110 + (float)(20 * Math.Sin(angle * Math.PI / 180.0));
        var ellipse = new D2DEllipse { point = new System.Numerics.Vector2(cx, cy), radiusX = 8, radiusY = 8 };
        _dc.Get()->FillEllipse(&ellipse, (ID2D1Brush*)_dotBrush.Get());
    }

    private static bool IsDeviceLost(HResult hr) =>
        hr.Value is DXGI_ERROR_DEVICE_REMOVED or DXGI_ERROR_DEVICE_RESET or DXGI_ERROR_DEVICE_HUNG;

    public void SetClickThrough(nint hwnd, bool clickThrough) => ApplyClickThrough(hwnd, clickThrough);

    public static void ApplyClickThrough(nint hwnd, bool clickThrough)
    {
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x00000020;
        int style = Program.GetWindowLongW(hwnd, GWL_EXSTYLE);
        style = clickThrough ? (style | WS_EX_TRANSPARENT) : (style & ~WS_EX_TRANSPARENT);
        Program.SetWindowLongW(hwnd, GWL_EXSTYLE, style);
    }

    private void ReleaseGpuResources()
    {
        _dotBrush.Dispose();
        _textBrush.Dispose();
        _textFormat.Dispose();
        _dwriteFactory.Dispose();
        _dc.Dispose();
        _d2dDevice.Dispose();
        _d2dFactory.Dispose();
        _rootVisual.Dispose();
        _target.Dispose();
        _dcompDevice.Dispose();
        _swapChain.Dispose();
        _dxgiFactory.Dispose();
        _dxgiDevice.Dispose();
        _d3dDevice.Dispose();
    }

    public void Dispose() => ReleaseGpuResources();
}
