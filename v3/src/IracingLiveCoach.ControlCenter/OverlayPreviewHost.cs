using System.Runtime.InteropServices;
using IracingLiveCoach.Core.Telemetry;
using IracingLiveCoach.OverlayHost;
using IracingLiveCoach.OverlayHost.Assets;
using IracingLiveCoach.OverlayHost.Layout;
using IracingLiveCoach.OverlayHost.Persistence;
using IracingLiveCoach.OverlayHost.Widgets;

namespace IracingLiveCoach.ControlCenter;

/// <summary>
/// The Control Center's "Preview ao vivo" (spec §12): a REAL Direct2D surface running the same
/// widget classes (<see cref="StandingsWidget"/>, <see cref="RelativeWidget"/>) the live overlay
/// uses -- not a second, hand-drawn WPF mockup of them (spec §3/§12: "Preview e overlay real devem
/// compartilhar o mesmo motor de layout e renderização. Não mantenha uma segunda implementação
/// visual divergente.")
///
/// NOT implemented as a WPF <c>HwndHost</c> child window: a first attempt at that failed at runtime
/// -- <c>IDCompositionDesktopDevice::CreateTargetForHwnd</c> returned E_INVALIDARG (0x80070057) for
/// a WS_CHILD window. OverlayHost.exe's own <see cref="Program"/> only ever targets DirectComposition
/// at WS_POPUP top-level windows created with WS_EX_NOREDIRECTIONBITMAP, and that combination is what
/// actually works here too. So instead this creates its own top-level, borderless popup window
/// OWNED by the Control Center's main window (so it minimizes/closes with it, per §12: "fechar o
/// painel não fecha os overlays" -- that rule is about the separate OverlayHost process, not this
/// preview surface, which legitimately should disappear with the panel) and repositioned every time
/// its WPF anchor element moves or resizes, so it visually tracks as if embedded.
///
/// Data shown is ALWAYS the spec §12-mandated simulated preset ("preview com dados fictícios
/// claramente identificado como simulação, disponível sem iRacing aberto") -- each widget's own
/// <c>SetSimulatedRows</c> always wins over live telemetry in their own Draw() methods. Honest gap:
/// each widget's constructor still starts its own <see cref="TelemetryReader"/> internally, so this
/// process does open real SDK connection attempts in the background even though their data is never
/// drawn here -- harmless when iRacing isn't running, wasteful redundancy with OverlayHost.exe's own
/// connection when it is.
///
/// Also honest: layout/position/scale come from whatever <see cref="PlacementPersistence"/> last
/// saved to disk (read once at construction), not a live cross-process read of the running overlay.
/// Only Standings and Relative are wired in -- Weather/Fuel/Radar/StartHelper don't have a
/// SetSimulatedRows-equivalent yet, so adding them here would just show their real
/// "Aguardando iRacing..." placeholder inside a panel that's supposed to work with no iRacing open.
/// </summary>
public sealed unsafe class OverlayPreviewHost : IDisposable
{
    private const string ClassName = "IracingLiveCoach.ControlCenter.PreviewSurface";
    private static bool _classRegistered;

    private nint _hwnd;
    private int _width;
    private int _height;
    private DeviceResources? _device;
    private FlagBitmapCache? _flags;
    private StandingsWidget? _standings;
    private RelativeWidget? _relative;
    private readonly WidgetPlacementStore _placements = new();

    public OverlayPreviewHost(nint ownerHwnd, int x, int y, int width, int height)
    {
        EnsureClassRegistered();
        Program.LoadPrivateFonts();

        // Same hardcoded starting values as OverlayHost's own Program.cs (necessarily duplicated --
        // separate processes, spec §3) so the preview shows something sane even before
        // OverlayHost.exe has ever run once to create v3-layout.json.
        _placements.Set("standings", new WidgetPlacement(0, 200, 200, PlacementAnchor.TopLeft, 820, 260, 1f, false, 0));
        _placements.Set("relative", new WidgetPlacement(0, 200, 470, PlacementAnchor.TopLeft, 760, 200, 1f, false, 1));
        PlacementPersistence.Load(_placements);

        _width = Math.Max(1, width);
        _height = Math.Max(1, height);

        _hwnd = CreateWindowExW(
            WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOOLWINDOW,
            ClassName, "", WS_POPUP | WS_VISIBLE,
            x, y, _width, _height, ownerHwnd, 0, 0, 0);
        if (_hwnd == 0)
            throw new InvalidOperationException($"CreateWindowExW failed: {Marshal.GetLastWin32Error()}");
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);

        CreateDeviceChain();
    }

    private void CreateDeviceChain()
    {
        _device = DeviceResources.Create(_hwnd, _width, _height);
        _flags = new FlagBitmapCache(_device.Context);
        _standings = new StandingsWidget(_device.Context, _device.DWriteFactory, _flags);
        _relative = new RelativeWidget(_device.Context, _device.DWriteFactory, _flags);
        _standings.SetSimulatedRows(PreviewData.StandingsRows());
        _relative.SetSimulatedRows(PreviewData.RelativeRows());
    }

    private void DisposeDeviceChain()
    {
        _relative?.Dispose(); _relative = null;
        _standings?.Dispose(); _standings = null;
        _flags?.Dispose(); _flags = null;
        _device?.Dispose(); _device = null;
    }

    /// <summary>Repositions (and, if the size actually changed, rebuilds the device chain at the new
    /// size -- there is no incremental swap-chain resize path yet, matching the same gap already
    /// documented for the live overlay's own width/height/scale IPC messages) to track a WPF
    /// element's current screen rectangle.</summary>
    public void MoveTo(int x, int y, int width, int height)
    {
        if (_hwnd == 0 || width <= 0 || height <= 0) return;
        bool sizeChanged = width != _width || height != _height;
        SetWindowPos(_hwnd, 0, x, y, width, height, SWP_NOACTIVATE | SWP_NOZORDER);
        if (sizeChanged)
        {
            _width = width;
            _height = height;
            DisposeDeviceChain();
            CreateDeviceChain();
        }
    }

    public void SetVisible(bool visible) => ShowWindow(_hwnd, visible ? SW_SHOWNOACTIVATE : SW_HIDE);

    /// <summary>Draws one frame. The caller (MainWindow, via <see cref="System.Windows.Media.CompositionTarget.Rendering"/>)
    /// decides the cadence -- this class has no timer of its own.</summary>
    public void RenderFrame()
    {
        if (_device is null || _standings is null || _relative is null) return;

        // 1920x1080-relative preview scaled into whatever size the panel actually granted this
        // surface (spec §12/§17: "Preview inclui modo 1920×1080 em escala real").
        float scale = _width / 1920f;

        _device.BeginFrame();
        var standingsPlacement = _placements.Get("standings");
        var relativePlacement = _placements.Get("relative");
        if (standingsPlacement is not null) _standings.Draw(_device.Context, standingsPlacement.X * scale, standingsPlacement.Y * scale);
        if (relativePlacement is not null) _relative.Draw(_device.Context, relativePlacement.X * scale, relativePlacement.Y * scale);
        _device.EndFrame();
    }

    private static void EnsureClassRegistered()
    {
        if (_classRegistered) return;
        var wc = new WNDCLASSW
        {
            lpfnWndProc = DefWindowProcPtr,
            hInstance = GetModuleHandleW(null),
            lpszClassName = ClassName
        };
        ushort atom = RegisterClassW(ref wc);
        if (atom == 0)
            throw new InvalidOperationException($"RegisterClassW failed: {Marshal.GetLastWin32Error()}");
        _classRegistered = true;
    }

    private static readonly nint DefWindowProcPtr = GetProcAddress(LoadLibraryW("user32.dll"), "DefWindowProcW");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const int SW_SHOWNOACTIVATE = 4;
    private const int SW_HIDE = 0;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassW(ref WNDCLASSW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int cmdShow);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? moduleName);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadLibraryW(string fileName);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern nint GetProcAddress(nint module, string procName);

    public void Dispose()
    {
        DisposeDeviceChain();
        if (_hwnd != 0) { DestroyWindow(_hwnd); _hwnd = 0; }
    }
}
