using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ams2.ControlCenter;

/// <summary>Pergunta simples de uma linha (nome de perfil), no mesmo tema escuro do app.</summary>
public sealed class InputDialog : Window
{
    readonly TextBox _box = new() { MinWidth = 280 };

    InputDialog(string title, string prompt, string initial, string okText)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "BgBrush");

        var ok = new Button { Content = okText, IsDefault = true, Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(0, 0, 8, 0) };
        ok.SetResourceReference(StyleProperty, "PrimaryButton");
        var cancel = new Button { Content = "Cancelar", IsCancel = true, Padding = new Thickness(14, 6, 14, 6) };
        ok.Click += (_, _) => { if (_box.Text.Trim().Length > 0) DialogResult = true; };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var label = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(label);
        panel.Children.Add(_box);
        panel.Children.Add(buttons);
        Content = panel;

        _box.Text = initial;
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    public static string? Ask(Window owner, string title, string prompt, string initial = "", string okText = "OK")
    {
        var d = new InputDialog(title, prompt, initial, okText) { Owner = owner };
        DarkTitleBar.Apply(d);
        return d.ShowDialog() == true ? d._box.Text.Trim() : null;
    }
}

internal static class DarkTitleBar
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    /// <summary>Barra de titulo escura (Windows 10 20H1+/11); ignora silenciosamente se nao suportado.</summary>
    public static void Apply(Window w)
    {
        w.SourceInitialized += (_, _) =>
        {
            int on = 1;
            var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            try { DwmSetWindowAttribute(h, 20, ref on, 4); } catch { }
        };
    }
}
