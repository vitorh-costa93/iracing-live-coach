using System.Runtime.InteropServices;
using static Ams2.OverlayHost.Native.Win32;

namespace Ams2.OverlayHost.Native;

/// <summary>
/// Janela do overlay: topmost, click-through e sem roubar foco
/// (WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED, sem bitmap de redirecionamento: o conteúdo vem do DirectComposition).
/// </summary>
internal sealed class OverlayWindow
{
    static readonly WndProcDelegate Proc = WndProc; // referência estática: o GC não pode coletar o delegate
    static bool _registered;
    const string ClassName = "Ams2.OverlayHost.Window";

    public nint Handle { get; private set; }

    public static OverlayWindow Create(string title, int x, int y, int width, int height)
    {
        nint inst = GetModuleHandleW(null);
        if (!_registered)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
                hInstance = inst,
                lpszClassName = ClassName,
            };
            if (RegisterClassExW(ref wc) == 0) throw new InvalidOperationException($"RegisterClassExW falhou: {Marshal.GetLastWin32Error()}");
            _registered = true;
        }
        nint hwnd = CreateWindowExW(
            WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOREDIRECTIONBITMAP,
            ClassName, title, WS_POPUP, x, y, width, height, 0, 0, inst, 0);
        if (hwnd == 0) throw new InvalidOperationException($"CreateWindowExW falhou: {Marshal.GetLastWin32Error()}");
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        return new OverlayWindow { Handle = hwnd };
    }

    static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_MOUSEACTIVATE: return MA_NOACTIVATE;
            case WM_NCHITTEST: return HTTRANSPARENT;
            case WM_DESTROY: PostQuitMessage(0); return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <summary>Processa as mensagens pendentes. false = pedido de saída (WM_QUIT ou Ctrl+Alt+Q).</summary>
    public static bool Pump()
    {
        MSG msg = default;
        while (PeekMessageW(ref msg, 0, 0, 0, 1))
        {
            if (msg.message == WM_QUIT || msg.message == WM_HOTKEY) return false;
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
        return true;
    }

    /// <summary>Atalho global de saída, já que a janela nunca recebe foco nem cliques.</summary>
    public static void RegisterQuitHotkey() => RegisterHotKey(0, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 'Q');

    public void Close() { if (Handle != 0) DestroyWindow(Handle); Handle = 0; }
}
