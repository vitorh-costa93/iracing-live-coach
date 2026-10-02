using System.Runtime.InteropServices;
using static Ams2.OverlayHost.Native.Win32;

namespace Ams2.OverlayHost.Native;

/// <summary>
/// Janela de um widget: topmost, sem roubar foco (WS_EX_NOACTIVATE), sem bitmap de redirecionamento (o conteúdo vem do DirectComposition).
/// Normalmente é click-through (WS_EX_TRANSPARENT); no modo de edição passa a receber o mouse e repassa os eventos a <see cref="Mouse"/>.
/// </summary>
internal sealed class OverlayWindow
{
    static readonly WndProcDelegate Proc = WndProc; // referência estática: o GC não pode coletar o delegate
    static readonly Dictionary<nint, OverlayWindow> Windows = [];
    static bool _registered;
    const string ClassName = "Ams2.OverlayHost.Window";

    public nint Handle { get; private set; }
    public bool EditMode { get; private set; }

    /// <summary>(mensagem, x cliente, y cliente, delta da roda). Só é chamado em modo de edição.</summary>
    public Action<uint, int, int, int>? Mouse { get; set; }
    /// <summary>Dado x,y do cliente, diz se o cursor deve ser o de redimensionar.</summary>
    public Func<int, int, bool>? IsResizeGrip { get; set; }

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
        var w = new OverlayWindow { Handle = hwnd };
        Windows[hwnd] = w;
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        return w;
    }

    public void SetEditMode(bool edit)
    {
        EditMode = edit;
        long ex = GetWindowLongPtr(Handle, GWL_EXSTYLE);
        ex = edit ? ex & ~(long)WS_EX_TRANSPARENT : ex | WS_EX_TRANSPARENT;
        SetWindowLongPtr(Handle, GWL_EXSTYLE, (nint)ex);
        BringToTop();
    }

    public void SetVisible(bool visible) => ShowWindow(Handle, visible ? SW_SHOWNOACTIVATE : SW_HIDE);

    public void MoveResize(int x, int y, int w, int h) => SetWindowPos(Handle, 0, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
    public void Move(int x, int y) => SetWindowPos(Handle, 0, x, y, 0, 0, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE);
    public void BringToTop() => SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public (int X, int Y, int W, int H) Bounds
    {
        get { GetWindowRect(Handle, out var r); return (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top); }
    }

    static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        Windows.TryGetValue(hwnd, out var win);
        switch (msg)
        {
            case WM_MOUSEACTIVATE: return MA_NOACTIVATE;
            case WM_NCHITTEST: return win is { EditMode: true } ? HTCLIENT : HTTRANSPARENT;
            case WM_SETCURSOR when win is { EditMode: true }:
            {
                GetCursorPos(out var p); GetWindowRect(hwnd, out var r);
                bool grip = win.IsResizeGrip?.Invoke(p.X - r.Left, p.Y - r.Top) ?? false;
                SetCursor(LoadCursorW(0, grip ? IDC_SIZENWSE : IDC_SIZEALL));
                return 1;
            }
            case WM_LBUTTONDOWN or WM_MOUSEMOVE or WM_LBUTTONUP when win is { EditMode: true }:
                win.Mouse?.Invoke(msg, (short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF), 0);
                return 0;
            case WM_MOUSEWHEEL when win is { EditMode: true }:
                win.Mouse?.Invoke(msg, 0, 0, (short)((wParam >> 16) & 0xFFFF));
                return 0;
            case WM_DESTROY:
                Windows.Remove(hwnd);
                if (Windows.Count == 0) PostQuitMessage(0);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <summary>Processa as mensagens pendentes. false = pedido de saída (WM_QUIT ou Ctrl+Alt+Q). Ctrl+Alt+E chama <paramref name="onEditHotkey"/>.</summary>
    public static bool Pump(Action? onEditHotkey = null)
    {
        MSG msg = default;
        while (PeekMessageW(ref msg, 0, 0, 0, 1))
        {
            if (msg.message == WM_QUIT) return false;
            if (msg.message == WM_HOTKEY)
            {
                if (msg.wParam == 1) return false;
                if (msg.wParam == 2) onEditHotkey?.Invoke();
                continue;
            }
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
        return true;
    }

    /// <summary>Atalhos globais (as janelas nunca recebem foco): Ctrl+Alt+Q sai, Ctrl+Alt+E liga/desliga o modo de edição.</summary>
    public static void RegisterHotkeys()
    {
        RegisterHotKey(0, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 'Q');
        RegisterHotKey(0, 2, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 'E');
    }

    public void Close()
    {
        if (Handle == 0) return;
        var h = Handle; Handle = 0;
        DestroyWindow(h);
    }
}
