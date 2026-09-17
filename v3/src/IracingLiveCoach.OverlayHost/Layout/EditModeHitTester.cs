namespace IracingLiveCoach.OverlayHost.Layout;

/// <summary>What part of a widget's edit-mode chrome a point landed on.</summary>
public enum HitZone { None, Body, ResizeNW, ResizeNE, ResizeSW, ResizeSE }

/// <summary>One hit-test result: which widget, and which zone of it.</summary>
/// <param name="WidgetKey">The widget that was hit.</param>
/// <param name="Zone">Body means "start a drag-move"; a Resize* value means "start a corner resize".</param>
public sealed record HitResult(string WidgetKey, HitZone Zone);

/// <summary>
/// Manual mouse hit-testing for edit mode. WPF gets this for free from its visual tree's own
/// input routing; a Direct2D-drawn overlay does not, so this class exists to replace it: given the
/// current placements and a mouse point, decide what a mouse-down should start doing (spec §4:
/// "arrastar diretamente na tela no modo de edição... exibindo limites e alças apenas durante
/// edição"). Pure geometry — no window/input-system dependency — so it's fully unit-testable.
/// </summary>
public static class EditModeHitTester
{
    /// <summary>How close (in DIPs) to a corner counts as grabbing its resize handle, rather than
    /// the widget's body. Matches spec §4's expectation of visible resize handles during editing.</summary>
    public const float ResizeHandleSizeDip = 10f;

    /// <summary>
    /// Finds the topmost (highest <see cref="WidgetPlacement.ZOrder"/>) unlocked widget whose
    /// rectangle contains <paramref name="pointX"/>/<paramref name="pointY"/>, and which zone of it
    /// was hit. Locked widgets are excluded entirely — spec §4: editing a locked widget is a no-op,
    /// so it must not intercept the hit either, letting whatever is beneath it respond instead.
    /// </summary>
    public static HitResult? HitTest(IReadOnlyDictionary<string, WidgetPlacement> placements, float pointX, float pointY)
    {
        HitResult? best = null;
        int bestZOrder = int.MinValue;

        foreach (var (key, placement) in placements)
        {
            if (placement.Locked) continue;
            if (placement.ZOrder < bestZOrder) continue; // only topmost survives on a tie or better

            var (left, top, width, height) = placement.ToRect();
            if (pointX < left || pointX > left + width || pointY < top || pointY > top + height)
                continue;

            var zone = ClassifyZone(pointX, pointY, left, top, width, height);
            best = new HitResult(key, zone);
            bestZOrder = placement.ZOrder;
        }

        return best;
    }

    private static HitZone ClassifyZone(float pointX, float pointY, float left, float top, float width, float height)
    {
        float right = left + width;
        float bottom = top + height;
        float handle = ResizeHandleSizeDip;

        bool nearLeft = pointX - left <= handle;
        bool nearRight = right - pointX <= handle;
        bool nearTop = pointY - top <= handle;
        bool nearBottom = bottom - pointY <= handle;

        if (nearLeft && nearTop) return HitZone.ResizeNW;
        if (nearRight && nearTop) return HitZone.ResizeNE;
        if (nearLeft && nearBottom) return HitZone.ResizeSW;
        if (nearRight && nearBottom) return HitZone.ResizeSE;
        return HitZone.Body;
    }

    /// <summary>
    /// Applies a drag-move delta to a placement. Locked widgets are returned unchanged (caller
    /// should have already excluded them via <see cref="HitTest"/>, but this stays defensive since
    /// a drag can span multiple calls after a lock toggles mid-gesture).
    /// </summary>
    public static WidgetPlacement ApplyDragDelta(WidgetPlacement placement, float deltaX, float deltaY) =>
        placement.Locked ? placement : placement with { X = placement.X + deltaX, Y = placement.Y + deltaY };

    /// <summary>
    /// Applies a corner-resize delta, respecting <paramref name="minWidthDip"/>/<paramref name="minHeightDip"/>
    /// so a widget can never be dragged to zero or negative size (spec §4 implies sane resize
    /// bounds; §17 separately governs column-level minimums once content is laid out inside it).
    /// </summary>
    public static WidgetPlacement ApplyResizeDelta(
        WidgetPlacement placement, HitZone zone, float deltaX, float deltaY,
        float minWidthDip = 40f, float minHeightDip = 24f)
    {
        if (placement.Locked || zone is HitZone.None or HitZone.Body) return placement;

        float newWidth = placement.WidthDip;
        float newHeight = placement.HeightDip;
        float newX = placement.X;
        float newY = placement.Y;

        bool growsFromLeft = zone is HitZone.ResizeNW or HitZone.ResizeSW;
        bool growsFromTop = zone is HitZone.ResizeNW or HitZone.ResizeNE;

        if (growsFromLeft)
        {
            newWidth = Math.Max(minWidthDip, placement.WidthDip - deltaX / placement.Scale);
            if (placement.Anchor is PlacementAnchor.TopLeft or PlacementAnchor.BottomLeft)
                newX += placement.WidthDip * placement.Scale - newWidth * placement.Scale;
        }
        else
        {
            newWidth = Math.Max(minWidthDip, placement.WidthDip + deltaX / placement.Scale);
        }

        if (growsFromTop)
        {
            newHeight = Math.Max(minHeightDip, placement.HeightDip - deltaY / placement.Scale);
            if (placement.Anchor is PlacementAnchor.TopLeft or PlacementAnchor.TopRight)
                newY += placement.HeightDip * placement.Scale - newHeight * placement.Scale;
        }
        else
        {
            newHeight = Math.Max(minHeightDip, placement.HeightDip + deltaY / placement.Scale);
        }

        return placement with { WidthDip = newWidth, HeightDip = newHeight, X = newX, Y = newY };
    }
}
