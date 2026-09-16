using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace IracingLiveCoach.App;

/// <summary>Size-aware chamfer for native overlay surfaces and safe background-only dragging.</summary>
public static class BroadcastChrome
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(BroadcastChrome), new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);

    private static void OnEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        element.SizeChanged -= OnSizeChanged;
        if ((bool)args.NewValue) { element.SizeChanged += OnSizeChanged; UpdateClip(element); }
        else element.ClearValue(UIElement.ClipProperty);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs args) => UpdateClip((FrameworkElement)sender);

    private static void UpdateClip(FrameworkElement element)
    {
        var w = element.ActualWidth;
        var h = element.ActualHeight;
        if (w <= 0 || h <= 0) return;
        var cut = System.Math.Min(12, System.Math.Min(w, h) / 4);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new System.Windows.Point(0, 0), true, true);
            context.LineTo(new System.Windows.Point(w - cut, 0), true, false);
            context.LineTo(new System.Windows.Point(w, cut), true, false);
            context.LineTo(new System.Windows.Point(w, h), true, false);
            context.LineTo(new System.Windows.Point(0, h), true, false);
        }
        geometry.Freeze();
        element.Clip = geometry;
    }

    public static bool IsInteractive(DependencyObject? source)
    {
        for (var current = source; current is not null;)
        {
            if (current is Thumb or System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase or Slider or System.Windows.Controls.Primitives.ScrollBar) return true;
            current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }
}
