using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.Direct3D;
using Vortice.Win32.Graphics.Direct3D11;
using Vortice.Win32.Graphics.DirectComposition;
using Vortice.Win32.Graphics.DirectWrite;
using Vortice.Win32.Graphics.Dxgi;
using Vortice.Win32.Graphics.Dxgi.Common;
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
using D2DAlphaMode = Vortice.Win32.Graphics.Direct2D.Common.AlphaMode;
using DxgiSurface = Vortice.Win32.Graphics.Dxgi.IDXGISurface;

namespace Ams2.OverlayHost.Gfx;

/// <summary>
/// Cadeia D3D11 + Direct2D + DirectWrite. Dois modos:
/// janela (swap chain de composição + DirectComposition, como no V3) e offscreen (bitmap D2D, para o --png).
/// Sobrevive a device lost em modo janela: refaz tudo e incrementa <see cref="Generation"/>,
/// para quem guarda recursos de GPU (formatos, pincéis) recriá-los.
/// </summary>
public sealed unsafe class DeviceResources : IDisposable
{
    const int DXGI_ERROR_DEVICE_REMOVED = unchecked((int)0x887A0005);
    const int DXGI_ERROR_DEVICE_RESET = unchecked((int)0x887A0007);
    const int DXGI_ERROR_DEVICE_HUNG = unchecked((int)0x887A0006);

    ComPtr<ID3D11Device> _d3dDevice;
    ComPtr<IDXGIDevice> _dxgiDevice;
    ComPtr<IDXGISwapChain1> _swapChain;
    ComPtr<IDCompositionDesktopDevice> _dcompDevice;
    ComPtr<IDCompositionTarget> _target;
    ComPtr<IDCompositionVisual2> _rootVisual;
    ComPtr<ID2D1Factory1> _d2dFactory;
    ComPtr<ID2D1Device> _d2dDevice;
    ComPtr<ID2D1DeviceContext> _dc;
    ComPtr<IDWriteFactory> _dwriteFactory;
    ComPtr<ID2D1Bitmap1> _offscreen;
    FontLibrary? _fonts;

    nint _hwnd;
    int _width, _height;
    bool _offscreenMode;

    public ID2D1DeviceContext* Context => _dc.Get();
    public IDWriteFactory* DWriteFactory => _dwriteFactory.Get();
    public FontLibrary Fonts => _fonts!;
    public int Width => _width;
    public int Height => _height;
    /// <summary>Muda a cada recuperação de device lost.</summary>
    public int Generation { get; private set; }
    /// <summary>Intervalo de vsync do Present. Com varias janelas o host usa 0 (ritmo por temporizador) para nao somar vsyncs.</summary>
    public int PresentInterval { get; set; } = 1;

    DeviceResources() { }

    public static DeviceResources CreateForWindow(nint hwnd, int width, int height)
    {
        var d = new DeviceResources { _hwnd = hwnd, _width = width, _height = height };
        d.Initialize();
        return d;
    }

    public static DeviceResources CreateOffscreen(int width, int height)
    {
        var d = new DeviceResources { _offscreenMode = true, _width = width, _height = height };
        d.Initialize();
        return d;
    }

    void Initialize()
    {
        ReadOnlySpan<FeatureLevel> levels = [FeatureLevel.Level_11_0];
        ComPtr<ID3D11Device> device = default;
        using ComPtr<ID3D11DeviceContext> immediate = default;
        FeatureLevel achieved;
        ThrowIfFailed(D3D11CreateDevice(null, DriverType.Hardware, CreateDeviceFlags.BgraSupport, levels,
            device.GetAddressOf(), &achieved, immediate.GetAddressOf()));
        _d3dDevice = device;

        ComPtr<IDXGIDevice> dxgiDevice = default;
        ThrowIfFailed(_d3dDevice.As(ref dxgiDevice));
        _dxgiDevice = dxgiDevice;

        ComPtr<ID2D1Factory1> d2dFactory = default;
        ThrowIfFailed(D2D1CreateFactory(D2DFactoryType.SingleThreaded, __uuidof<ID2D1Factory1>(), null, (void**)d2dFactory.GetAddressOf()));
        _d2dFactory = d2dFactory;
        ComPtr<ID2D1Device> d2dDevice = default;
        ThrowIfFailed(_d2dFactory.Get()->CreateDevice((Vortice.Win32.Graphics.Dxgi.IDXGIDevice*)_dxgiDevice.Get(), d2dDevice.GetAddressOf()));
        _d2dDevice = d2dDevice;
        ComPtr<ID2D1DeviceContext> dc = default;
        ThrowIfFailed(_d2dDevice.Get()->CreateDeviceContext(DeviceContextOptions.None, dc.GetAddressOf()));
        _dc = dc;

        ComPtr<IDWriteFactory> dwrite = default;
        ThrowIfFailed(DWriteCreateFactory(DWriteFactoryType.Shared, __uuidof<IDWriteFactory>(), (void**)dwrite.GetAddressOf()));
        _dwriteFactory = dwrite;
        _fonts = FontLibrary.Build(_dwriteFactory.Get());

        if (_offscreenMode) CreateOffscreenTarget();
        else CreateWindowChain();
    }

    void CreateWindowChain()
    {
        ComPtr<IDXGIFactory2> factory = default;
        ThrowIfFailed(CreateDXGIFactory2(CreateFactoryFlags.None, __uuidof<IDXGIFactory2>(), (void**)factory.GetAddressOf()));
        using var f = factory;

        var desc = new SwapChainDescription1
        {
            Width = (uint)_width, Height = (uint)_height,
            Format = DxgiFormat.B8G8R8A8Unorm,
            SampleDesc = new SampleDescription(1, 0),
            BufferUsage = DxgiUsage.RenderTargetOutput,
            // 3 buffers: com 2 e Present(0) o Present bloqueava ~9 ms esperando o DWM liberar o buffer (limitava o render a ~70 fps a 165 Hz).
            BufferCount = 3,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = DxgiAlphaMode.Premultiplied,
        };
        ComPtr<IDXGISwapChain1> swap = default;
        ThrowIfFailed(f.Get()->CreateSwapChainForComposition((IUnknown*)_d3dDevice.Get(), &desc, null, swap.GetAddressOf()));
        _swapChain = swap;

        ComPtr<IDCompositionDesktopDevice> dcomp = default;
        ThrowIfFailed(DCompositionCreateDevice3((IUnknown*)_dxgiDevice.Get(), __uuidof<IDCompositionDesktopDevice>(), (void**)dcomp.GetAddressOf()));
        _dcompDevice = dcomp;
        ComPtr<IDCompositionTarget> target = default;
        ThrowIfFailed(_dcompDevice.Get()->CreateTargetForHwnd(_hwnd, true, target.GetAddressOf()));
        _target = target;
        ComPtr<IDCompositionVisual2> visual = default;
        ThrowIfFailed(_dcompDevice.Get()->CreateVisual(visual.GetAddressOf()));
        _rootVisual = visual;
        ThrowIfFailed(((IDCompositionVisual*)_rootVisual.Get())->SetContent((IUnknown*)_swapChain.Get()));
        ThrowIfFailed(_target.Get()->SetRoot((IDCompositionVisual*)_rootVisual.Get()));
        ThrowIfFailed(_dcompDevice.Get()->Commit());

        using ComPtr<DxgiSurface> surface = default;
        ThrowIfFailed(_swapChain.Get()->GetBuffer(0, __uuidof<DxgiSurface>(), (void**)surface.GetAddressOf()));
        var props = new BitmapProperties1
        {
            pixelFormat = new D2DPixelFormat(DxgiFormat.B8G8R8A8Unorm, D2DAlphaMode.Premultiplied),
            dpiX = 96f, dpiY = 96f,
            bitmapOptions = BitmapOptions.Target | BitmapOptions.CannotDraw,
        };
        using ComPtr<ID2D1Bitmap1> bitmap = default;
        ThrowIfFailed(_dc.Get()->CreateBitmapFromDxgiSurface(surface.Get(), &props, bitmap.GetAddressOf()));
        _dc.Get()->SetTarget((ID2D1Image*)bitmap.Get());
    }

    void CreateOffscreenTarget()
    {
        var props = new BitmapProperties1
        {
            pixelFormat = new D2DPixelFormat(DxgiFormat.B8G8R8A8Unorm, D2DAlphaMode.Premultiplied),
            dpiX = 96f, dpiY = 96f,
            bitmapOptions = BitmapOptions.Target,
        };
        ComPtr<ID2D1Bitmap1> bmp = default;
        ThrowIfFailed(_dc.Get()->CreateBitmap(new System.Drawing.Size(_width, _height), null, 0, &props, bmp.GetAddressOf()));
        _offscreen = bmp;
        _dc.Get()->SetTarget((ID2D1Image*)_offscreen.Get());
    }

    public void BeginFrame()
    {
        _dc.Get()->BeginDraw();
        var clear = new Vortice.Win32.Numerics.Color4(0, 0, 0, 0);
        _dc.Get()->Clear(&clear);
    }

    /// <summary>false = device lost detectado e recuperado; tente de novo no próximo quadro.</summary>
    public bool EndFrame()
    {
        var end = _dc.Get()->EndDraw();
        if (IsLost(end)) { Recover(); return false; }
        ThrowIfFailed(end);
        if (_offscreenMode) return true;

        var present = _swapChain.Get()->Present((uint)PresentInterval, PresentFlags.None);
        if (IsLost(present)) { Recover(); return false; }
        ThrowIfFailed(present);
        _dcompDevice.Get()->Commit();
        return true;
    }

    /// <summary>Copia o quadro offscreen para a memória: BGRA premultiplicado, linhas contíguas.</summary>
    public byte[] ReadPixelsBgra()
    {
        if (!_offscreenMode) throw new InvalidOperationException("Somente em modo offscreen.");
        var props = new BitmapProperties1
        {
            pixelFormat = new D2DPixelFormat(DxgiFormat.B8G8R8A8Unorm, D2DAlphaMode.Premultiplied),
            dpiX = 96f, dpiY = 96f,
            bitmapOptions = BitmapOptions.CpuRead | BitmapOptions.CannotDraw,
        };
        using ComPtr<ID2D1Bitmap1> staging = default;
        ThrowIfFailed(_dc.Get()->CreateBitmap(new System.Drawing.Size(_width, _height), null, 0, &props, staging.GetAddressOf()));
        ThrowIfFailed(staging.Get()->CopyFromBitmap(null, (ID2D1Bitmap*)_offscreen.Get(), null));

        Vortice.Win32.Graphics.Direct2D.MappedRect mapped;
        ThrowIfFailed(staging.Get()->Map(MapOptions.Read, &mapped));
        var result = new byte[_width * _height * 4];
        for (int y = 0; y < _height; y++)
            new ReadOnlySpan<byte>(mapped.bits + (long)y * mapped.pitch, _width * 4).CopyTo(result.AsSpan(y * _width * 4));
        staging.Get()->Unmap();
        return result;
    }

    void Recover()
    {
        Release();
        Initialize();
        Generation++;
    }

    static bool IsLost(HResult hr) => hr.Value is DXGI_ERROR_DEVICE_REMOVED or DXGI_ERROR_DEVICE_RESET or DXGI_ERROR_DEVICE_HUNG;

    void Release()
    {
        _offscreen.Dispose();
        _fonts?.Dispose();
        _dwriteFactory.Dispose();
        _dc.Dispose();
        _d2dDevice.Dispose();
        _d2dFactory.Dispose();
        _rootVisual.Dispose();
        _target.Dispose();
        _dcompDevice.Dispose();
        _swapChain.Dispose();
        _dxgiDevice.Dispose();
        _d3dDevice.Dispose();
    }

    public void Dispose() => Release();
}
