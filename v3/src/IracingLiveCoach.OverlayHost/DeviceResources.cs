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
/// Phase 0 proof only: device-lost recovery is Phase 1's job (see the plan), this class only
/// proves the drawing path itself -- real transparency, real GPU compositing, sharp text.
/// </summary>
public sealed unsafe class DeviceResources : IDisposable
{
    // These fields own their COM references (plain struct-copy transfer, no extra AddRef) --
    // the locals that produce them in Create() are deliberately NOT `using`, since disposing
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

    private DeviceResources() { }

    public static DeviceResources Create(nint hwnd, int width, int height)
    {
        var self = new DeviceResources();

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
        self._d3dDevice = device;

        ComPtr<IDXGIDevice> dxgiDevice = default;
        ThrowIfFailed(self._d3dDevice.As(ref dxgiDevice));
        self._dxgiDevice = dxgiDevice;

        ComPtr<IDXGIFactory2> dxgiFactory = default;
        ThrowIfFailed(CreateDXGIFactory2(CreateFactoryFlags.None, __uuidof<IDXGIFactory2>(), (void**)dxgiFactory.GetAddressOf()));
        self._dxgiFactory = dxgiFactory;

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
        ThrowIfFailed(self._dxgiFactory.Get()->CreateSwapChainForComposition(
            (IUnknown*)self._d3dDevice.Get(), &swapChainDesc, null, swapChain.GetAddressOf()));
        self._swapChain = swapChain;

        ComPtr<IDCompositionDesktopDevice> dcompDevice = default;
        ThrowIfFailed(DCompositionCreateDevice3(
            (IUnknown*)self._dxgiDevice.Get(), __uuidof<IDCompositionDesktopDevice>(), (void**)dcompDevice.GetAddressOf()));
        self._dcompDevice = dcompDevice;

        ComPtr<IDCompositionTarget> target = default;
        ThrowIfFailed(self._dcompDevice.Get()->CreateTargetForHwnd(hwnd, true, target.GetAddressOf()));
        self._target = target;

        ComPtr<IDCompositionVisual2> visual = default;
        ThrowIfFailed(self._dcompDevice.Get()->CreateVisual(visual.GetAddressOf()));
        self._rootVisual = visual;
        ThrowIfFailed(((IDCompositionVisual*)self._rootVisual.Get())->SetContent((IUnknown*)self._swapChain.Get()));
        ThrowIfFailed(self._target.Get()->SetRoot((IDCompositionVisual*)self._rootVisual.Get()));
        ThrowIfFailed(self._dcompDevice.Get()->Commit());

        ComPtr<ID2D1Factory1> d2dFactory = default;
        ThrowIfFailed(D2D1CreateFactory(D2DFactoryType.SingleThreaded, __uuidof<ID2D1Factory1>(), null, (void**)d2dFactory.GetAddressOf()));
        self._d2dFactory = d2dFactory;

        ComPtr<ID2D1Device> d2dDevice = default;
        ThrowIfFailed(self._d2dFactory.Get()->CreateDevice((Vortice.Win32.Graphics.Dxgi.IDXGIDevice*)self._dxgiDevice.Get(), d2dDevice.GetAddressOf()));
        self._d2dDevice = d2dDevice;

        ComPtr<ID2D1DeviceContext> dc = default;
        ThrowIfFailed(self._d2dDevice.Get()->CreateDeviceContext(DeviceContextOptions.None, dc.GetAddressOf()));
        self._dc = dc;

        ComPtr<IDWriteFactory> dwriteFactory = default;
        ThrowIfFailed(DWriteCreateFactory(DWriteFactoryType.Shared, __uuidof<IDWriteFactory>(), (void**)dwriteFactory.GetAddressOf()));
        self._dwriteFactory = dwriteFactory;

        ComPtr<IDWriteTextFormat> textFormat = self._dwriteFactory.Get()->CreateTextFormat(
            "Segoe UI", 28.0f, fontWeight: FontWeight.SemiBold, localeName: "en-us");
        self._textFormat = textFormat;

        var white = new Color4(1f, 1f, 1f, 1f);
        ComPtr<ID2D1SolidColorBrush> textBrush = default;
        ThrowIfFailed(self._dc.Get()->CreateSolidColorBrush(&white, null, textBrush.GetAddressOf()));
        self._textBrush = textBrush;

        var cyan = new Color4(0f, 0.788f, 0.910f, 1f); // #00C9E8, player-highlight token from spec §16
        ComPtr<ID2D1SolidColorBrush> dotBrush = default;
        ThrowIfFailed(self._dc.Get()->CreateSolidColorBrush(&cyan, null, dotBrush.GetAddressOf()));
        self._dotBrush = dotBrush;

        self.BindTargetBitmap();

        return self;
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

    public void RenderFrame(double angle, bool clickThrough)
    {
        _dc.Get()->BeginDraw();

        var transparent = new Color4(0, 0, 0, 0);
        _dc.Get()->Clear(&transparent);

        // Static text: proves per-pixel transparency (Step 5) and DirectWrite sharpness (Step 7).
        string text = $"V3 PROOF  |  click-through: {(clickThrough ? "ON" : "OFF")} (SPACE toggles)";
        fixed (char* pText = text)
        {
            var layoutRect = new RectF(12, 8, 468, 60);
            _dc.Get()->DrawText(pText, (uint)text.Length, _textFormat.Get(), &layoutRect,
                (ID2D1Brush*)_textBrush.Get(), DrawTextOptions.None, MeasuringMode.Natural);
        }

        // Moving dot: Radar/Start Helper pacing stand-in (Step 8) -- proves the render loop keeps up
        // with per-frame telemetry-shaped updates without a growing backlog (spec §3).
        float cx = 240 + (float)(180 * Math.Cos(angle * Math.PI / 180.0));
        float cy = 110 + (float)(20 * Math.Sin(angle * Math.PI / 180.0));
        var ellipse = new D2DEllipse { point = new System.Numerics.Vector2(cx, cy), radiusX = 8, radiusY = 8 };
        _dc.Get()->FillEllipse(&ellipse, (ID2D1Brush*)_dotBrush.Get());

        ThrowIfFailed(_dc.Get()->EndDraw());
        _swapChain.Get()->Present(1, PresentFlags.None);
        _dcompDevice.Get()->Commit();
    }

    public void SetClickThrough(nint hwnd, bool clickThrough) => ApplyClickThrough(hwnd, clickThrough);

    public static void ApplyClickThrough(nint hwnd, bool clickThrough)
    {
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x00000020;
        int style = Program.GetWindowLongW(hwnd, GWL_EXSTYLE);
        style = clickThrough ? (style | WS_EX_TRANSPARENT) : (style & ~WS_EX_TRANSPARENT);
        Program.SetWindowLongW(hwnd, GWL_EXSTYLE, style);
    }

    public void Dispose()
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
}
