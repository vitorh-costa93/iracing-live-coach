using IracingLiveCoach.OverlayHost.Layout;

namespace IracingLiveCoach.OverlayHost.Tests;

public class WidgetPlacementTests
{
    private static WidgetPlacement Basic(float x = 100, float y = 100, float w = 200, float h = 100, PlacementAnchor anchor = PlacementAnchor.TopLeft, bool locked = false, int z = 0) =>
        new(MonitorIndex: 0, X: x, Y: y, Anchor: anchor, WidthDip: w, HeightDip: h, Scale: 1f, Locked: locked, ZOrder: z);

    // width=200, height=100 in every case below -- offsets are the resolved rect's (left, top)
    // MINUS (x, y), i.e. how far the anchor point sits from the widget's actual top-left corner.
    [Theory]
    [InlineData(PlacementAnchor.TopLeft, 0, 0)]
    [InlineData(PlacementAnchor.TopRight, -200, 0)]
    [InlineData(PlacementAnchor.BottomLeft, 0, -100)]
    [InlineData(PlacementAnchor.BottomRight, -200, -100)]
    public void ToRect_ResolvesEachAnchorToTopLeftCorrectly(PlacementAnchor anchor, float expectedLeftOffsetFromXY, float expectedTopOffsetFromXY)
    {
        const float x = 100, y = 100;
        var p = Basic(x, y, w: 200, h: 100, anchor: anchor);
        var (left, top, width, height) = p.ToRect();

        Assert.Equal(x + expectedLeftOffsetFromXY, left);
        Assert.Equal(y + expectedTopOffsetFromXY, top);
        Assert.Equal(200, width);
        Assert.Equal(100, height);
    }

    [Fact]
    public void ToRect_AppliesScaleToWidthAndHeight()
    {
        var p = Basic(w: 200, h: 100) with { Scale = 1.5f };
        var (_, _, width, height) = p.ToRect();
        Assert.Equal(300, width);
        Assert.Equal(150, height);
    }

    [Fact]
    public void Store_SetThenUndo_RestoresPreviousPlacement()
    {
        var store = new WidgetPlacementStore();
        var first = Basic(x: 10, y: 10);
        var second = Basic(x: 500, y: 500);

        store.Set("standings", first);
        store.Set("standings", second);
        Assert.Equal(second, store.Get("standings"));

        store.Undo();
        Assert.Equal(first, store.Get("standings"));

        store.Undo(); // undoing the very first Set (previous was null) removes the widget entirely
        Assert.Null(store.Get("standings"));
        Assert.False(store.CanUndo);
    }

    [Fact]
    public void Store_UndoThenRedo_ReappliesTheChange()
    {
        var store = new WidgetPlacementStore();
        store.Set("relative", Basic(x: 10));
        store.Set("relative", Basic(x: 20));

        store.Undo();
        Assert.Equal(10, store.Get("relative")!.X);

        store.Redo();
        Assert.Equal(20, store.Get("relative")!.X);
        Assert.False(store.CanRedo);
    }

    [Fact]
    public void Store_NewSetAfterUndo_ClearsRedoHistory()
    {
        var store = new WidgetPlacementStore();
        store.Set("fuel", Basic(x: 10));
        store.Set("fuel", Basic(x: 20));
        store.Undo();
        Assert.True(store.CanRedo);

        store.Set("fuel", Basic(x: 99)); // a fresh edit, not a redo
        Assert.False(store.CanRedo);
        Assert.Equal(99, store.Get("fuel")!.X);
    }

    [Fact]
    public void Store_SettingALockedWidgetToAnotherLockedValue_IsANoOp()
    {
        var store = new WidgetPlacementStore();
        store.Set("radar", Basic(x: 10, locked: true)); // the initial placement -- a real change, one undo frame
        store.Set("radar", Basic(x: 999, locked: true)); // attempted edit while already locked -- must be a true no-op

        Assert.Equal(10, store.Get("radar")!.X); // unchanged by the second Set

        // Exactly one undoable action exists (the first Set) -- the no-op second Set must not have
        // pushed a second frame. Undoing once should remove the widget entirely, with nothing left to undo.
        store.Undo();
        Assert.Null(store.Get("radar"));
        Assert.False(store.CanUndo);
    }

    [Fact]
    public void ClampToVirtualDesktop_LeavesOnScreenWidgetsUntouched()
    {
        var placement = Basic(x: 100, y: 100, w: 200, h: 100);
        var bounds = (Left: 0f, Top: 0f, Right: 1920f, Bottom: 1080f);

        var result = WidgetPlacementStore.ClampToVirtualDesktop(placement, bounds);
        Assert.Equal(placement, result);
    }

    [Fact]
    public void ClampToVirtualDesktop_MovesACompletelyOffscreenWidgetBackOnscreen()
    {
        // e.g. the widget lived on a second monitor at x=2500 that just got unplugged.
        var placement = Basic(x: 2500, y: 100, w: 200, h: 100);
        var bounds = (Left: 0f, Top: 0f, Right: 1920f, Bottom: 1080f);

        var result = WidgetPlacementStore.ClampToVirtualDesktop(placement, bounds);
        var (left, top, _, _) = result.ToRect();
        Assert.InRange(left, bounds.Left, bounds.Right - placement.WidthDip);
        Assert.InRange(top, bounds.Top, bounds.Bottom - placement.HeightDip);
    }
}

public class EditModeHitTesterTests
{
    private static WidgetPlacement Rect(float x, float y, float w, float h, bool locked = false, int z = 0) =>
        new(0, x, y, PlacementAnchor.TopLeft, w, h, 1f, locked, z);

    [Fact]
    public void HitTest_PointOutsideAllWidgets_ReturnsNull()
    {
        var placements = new Dictionary<string, WidgetPlacement> { ["standings"] = Rect(100, 100, 200, 100) };
        Assert.Null(EditModeHitTester.HitTest(placements, 5000, 5000));
    }

    [Fact]
    public void HitTest_PointInsideBody_ReturnsBodyZone()
    {
        var placements = new Dictionary<string, WidgetPlacement> { ["standings"] = Rect(100, 100, 200, 100) };
        var hit = EditModeHitTester.HitTest(placements, 200, 150); // well inside, far from any corner
        Assert.Equal("standings", hit!.WidgetKey);
        Assert.Equal(HitZone.Body, hit.Zone);
    }

    [Theory]
    [InlineData(102, 102, HitZone.ResizeNW)]
    [InlineData(298, 102, HitZone.ResizeNE)]
    [InlineData(102, 198, HitZone.ResizeSW)]
    [InlineData(298, 198, HitZone.ResizeSE)]
    public void HitTest_NearACorner_ReturnsTheMatchingResizeZone(float x, float y, HitZone expected)
    {
        var placements = new Dictionary<string, WidgetPlacement> { ["relative"] = Rect(100, 100, 200, 100) };
        var hit = EditModeHitTester.HitTest(placements, x, y);
        Assert.Equal(expected, hit!.Zone);
    }

    [Fact]
    public void HitTest_LockedWidget_IsNeverHit()
    {
        var placements = new Dictionary<string, WidgetPlacement> { ["radar"] = Rect(100, 100, 200, 100, locked: true) };
        Assert.Null(EditModeHitTester.HitTest(placements, 150, 150));
    }

    [Fact]
    public void HitTest_OverlappingWidgets_ReturnsTheHigherZOrderOne()
    {
        var placements = new Dictionary<string, WidgetPlacement>
        {
            ["behind"] = Rect(100, 100, 200, 200, z: 0),
            ["front"] = Rect(150, 150, 200, 200, z: 5),
        };
        var hit = EditModeHitTester.HitTest(placements, 200, 200); // inside both
        Assert.Equal("front", hit!.WidgetKey);
    }

    [Fact]
    public void ApplyDragDelta_MovesAnUnlockedWidget()
    {
        var p = Rect(100, 100, 200, 100);
        var moved = EditModeHitTester.ApplyDragDelta(p, deltaX: 50, deltaY: -20);
        Assert.Equal(150, moved.X);
        Assert.Equal(80, moved.Y);
    }

    [Fact]
    public void ApplyDragDelta_LockedWidget_IsUnchanged()
    {
        var p = Rect(100, 100, 200, 100, locked: true);
        var moved = EditModeHitTester.ApplyDragDelta(p, 50, 50);
        Assert.Equal(p, moved);
    }

    [Fact]
    public void ApplyResizeDelta_SoutheastCorner_GrowsWidthAndHeightWithoutMovingOrigin()
    {
        var p = Rect(100, 100, 200, 100);
        var resized = EditModeHitTester.ApplyResizeDelta(p, HitZone.ResizeSE, deltaX: 30, deltaY: 10);
        Assert.Equal(230, resized.WidthDip);
        Assert.Equal(110, resized.HeightDip);
        Assert.Equal(100, resized.X);
        Assert.Equal(100, resized.Y);
    }

    [Fact]
    public void ApplyResizeDelta_NorthwestCorner_GrowsAwayFromOriginAndMovesIt()
    {
        var p = Rect(100, 100, 200, 100);
        // Dragging the NW handle left/up by 30/10 should grow the widget and shift its origin.
        var resized = EditModeHitTester.ApplyResizeDelta(p, HitZone.ResizeNW, deltaX: -30, deltaY: -10);
        Assert.Equal(230, resized.WidthDip);
        Assert.Equal(110, resized.HeightDip);
        Assert.Equal(70, resized.X);
        Assert.Equal(90, resized.Y);
    }

    [Fact]
    public void ApplyResizeDelta_NeverShrinksBelowConfiguredMinimum()
    {
        var p = Rect(100, 100, 200, 100);
        var resized = EditModeHitTester.ApplyResizeDelta(p, HitZone.ResizeSE, deltaX: -1000, deltaY: -1000, minWidthDip: 40, minHeightDip: 24);
        Assert.Equal(40, resized.WidthDip);
        Assert.Equal(24, resized.HeightDip);
    }
}
