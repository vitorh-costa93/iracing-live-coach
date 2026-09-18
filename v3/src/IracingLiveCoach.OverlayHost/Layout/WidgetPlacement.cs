using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.OverlayHost.Layout;

/// <summary>Which corner/edge of a widget's bounds an X/Y coordinate is measured from.
/// Spec §4: "editar X/Y, monitor, ponto de ancoragem, escala e dimensões".</summary>
public enum PlacementAnchor { TopLeft, TopRight, BottomLeft, BottomRight, Center }

/// <summary>
/// One widget's full free-positioning state — spec §4's "posicionamento completamente livre":
/// any monitor, negative virtual-desktop coordinates, any anchor, independent scale, lock, and
/// z-order. This is pure data (no drawing, no window handles) so the same record describes a
/// widget's placement identically in the Control Center's preview and the live overlay, and so it
/// round-trips through persistence (Phase 6) without any GPU dependency.
/// </summary>
/// <param name="MonitorIndex">Index into the current monitor list; not itself persisted across a monitor being removed — see <see cref="WidgetPlacementStore.ClampToVirtualDesktop"/>.</param>
/// <param name="X">Position relative to <paramref name="Anchor"/>, in virtual-desktop DIPs. May be negative (spec §4).</param>
/// <param name="Y">See <paramref name="X"/>.</param>
/// <param name="Anchor">Which corner of the widget's bounds <paramref name="X"/>/<paramref name="Y"/> measures.</param>
/// <param name="WidthDip">Widget width before <paramref name="Scale"/> is applied.</param>
/// <param name="HeightDip">Widget height before <paramref name="Scale"/> is applied.</param>
/// <param name="Scale">Independent per-widget scale multiplier (spec §4/§5).</param>
/// <param name="Locked">When true, edit-mode dragging/resizing this widget is a no-op (spec §4's per-widget lock).</param>
/// <param name="ZOrder">Higher draws on top; also the hit-test priority order (spec §4: "ordem de sobreposição configurável").</param>
/// <param name="Visible">Per-widget on/off switch (spec §12's sidebar toggle), independent of <paramref name="Locked"/>.</param>
/// <param name="Opacity">Background/content opacity multiplier, 0..1 (spec §5's "opacidade de fundo e de conteúdo separadamente" -- this is the single overall value until Phase 6 splits it further).</param>
/// <remarks>Visible/Opacity were added after Locked/ZOrder (Phase 5) as trailing optional parameters
/// so every existing positional construction of this record — including the unit tests — keeps
/// compiling unchanged; this is an additive change, not a breaking one.</remarks>
public sealed record WidgetPlacement(
    int MonitorIndex,
    float X,
    float Y,
    PlacementAnchor Anchor,
    float WidthDip,
    float HeightDip,
    float Scale,
    bool Locked,
    int ZOrder,
    bool Visible = true,
    float Opacity = 1f)
{
    /// <summary>Resolves this placement to an absolute, anchor-independent top-left rectangle in
    /// virtual-desktop DIPs — what hit-testing and drawing actually need.</summary>
    public (float Left, float Top, float Width, float Height) ToRect()
    {
        float w = WidthDip * Scale;
        float h = HeightDip * Scale;
        var (left, top) = Anchor switch
        {
            PlacementAnchor.TopLeft => (X, Y),
            PlacementAnchor.TopRight => (X - w, Y),
            PlacementAnchor.BottomLeft => (X, Y - h),
            PlacementAnchor.BottomRight => (X - w, Y - h),
            PlacementAnchor.Center => (X - w / 2, Y - h / 2),
            _ => (X, Y)
        };
        return (left, top, w, h);
    }
}

/// <summary>Optional link between the Fuel and Relative widgets (spec §4: "Fuel começa à esquerda
/// do Relative... vínculo opcional, desligado por padrão"). When enabled, moving Relative keeps
/// Fuel at a fixed horizontal offset; this record only holds the configuration, the actual
/// "move together" behavior is applied by whatever owns the placements (Phase 5's Control Center
/// or the edit-mode hit tester), not by this type itself.</summary>
/// <param name="Enabled">Off by default per spec.</param>
/// <param name="SpacingDip">Horizontal gap between Fuel's right edge and Relative's left edge.</param>
public sealed record FuelRelativeLink(bool Enabled, float SpacingDip)
{
    public static FuelRelativeLink Default { get; } = new(Enabled: false, SpacingDip: 8f);
}

/// <summary>
/// Holds every widget's <see cref="WidgetPlacement"/> plus undo/redo history for layout edits
/// (spec §4: "desfazer/refazer mudanças de layout"). Each mutation pushes the PRE-change state
/// onto the undo stack (memento pattern) — simple, and correct regardless of how many fields a
/// future placement type gains.
/// </summary>
public sealed class WidgetPlacementStore
{
    private readonly Dictionary<string, WidgetPlacement> _placements = new();
    private readonly Stack<(string Key, WidgetPlacement? Previous)> _undoStack = new();
    private readonly Stack<(string Key, WidgetPlacement? Previous)> _redoStack = new();

    public FuelRelativeLink FuelRelativeLink { get; set; } = FuelRelativeLink.Default;

    /// <summary>User overrides of the normative per-class palette (spec §16), keyed by class short
    /// name (e.g. "GT3"). Persisted alongside placements; applied to <see cref="Theme.PaletteTokens"/>
    /// by <see cref="Persistence.PlacementPersistence"/>, never read directly by widget draw code.</summary>
    public Dictionary<string, string> ClassColorOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>User overrides of a widget's column configuration (spec §12: reorder/width/decimals/
    /// alignment/visibility), keyed by widget key (e.g. "standings"). Persisted alongside placements.</summary>
    public Dictionary<string, List<ColumnDefinition>> ColumnOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Standings' Top N / rows-per-class selection rules (spec §6/§12). Null means "use
    /// StandingsPresentationOptions.Default" -- never persisted as a redundant copy of the default.</summary>
    public StandingsPresentationOptions? StandingsRules { get; set; }

    /// <summary>Fuel's consumption-source/reserve/pit-exclusion config (spec §9). Null means "use
    /// FuelConfig.Default".</summary>
    public FuelConfig? FuelConfig { get; set; }

    /// <summary>Per-widget font scale/row height/row spacing overrides (spec §12), keyed by widget
    /// key. A widget absent from this dictionary uses <see cref="WidgetAppearance.Default"/>.</summary>
    public Dictionary<string, WidgetAppearance> AppearanceOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, WidgetPlacement> All => _placements;

    public WidgetPlacement? Get(string widgetKey) => _placements.GetValueOrDefault(widgetKey);

    /// <summary>Sets a widget's placement, recording the previous value for <see cref="Undo"/>.
    /// A no-op (new == current) still records history, matching how a real drag/resize gesture
    /// would end with a genuinely different value in practice, and keeping the API simple.</summary>
    public void Set(string widgetKey, WidgetPlacement placement)
    {
        if (placement.Locked && _placements.TryGetValue(widgetKey, out var existing) && existing.Locked)
            return; // spec §4: a locked widget is a no-op target for edit-mode changes.

        _placements.TryGetValue(widgetKey, out var previous);
        _undoStack.Push((widgetKey, previous));
        _redoStack.Clear(); // a fresh edit invalidates any redo history, standard undo/redo semantics.
        _placements[widgetKey] = placement;
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>Discards accumulated undo/redo history without touching current placements. Called
    /// once at startup, after the default seed values and the persisted profile have both gone
    /// through <see cref="Set"/> -- otherwise a user's first "desfazer" would undo internal wiring
    /// they never asked to change, back past the state their profile actually saved.</summary>
    public void ClearHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    /// <summary>Returns the widget key that was just reverted (or null if the stack was empty) so
    /// a caller sitting outside this store -- e.g. the IPC handler that also owns the live overlay
    /// windows -- knows exactly which widget's window to re-sync, without diffing every key.</summary>
    public string? Undo()
    {
        if (_undoStack.Count == 0) return null;
        var (key, previous) = _undoStack.Pop();
        _redoStack.Push((key, _placements.GetValueOrDefault(key)));
        if (previous is null) _placements.Remove(key);
        else _placements[key] = previous;
        return key;
    }

    public string? Redo()
    {
        if (_redoStack.Count == 0) return null;
        var (key, next) = _redoStack.Pop();
        _undoStack.Push((key, _placements.GetValueOrDefault(key)));
        if (next is null) _placements.Remove(key);
        else _placements[key] = next;
        return key;
    }

    /// <summary>Spec §4: "recuperar widgets fora da tela" after a resolution/monitor change. Clamps
    /// a placement's resolved rectangle back inside <paramref name="virtualDesktopBounds"/> if it
    /// currently falls entirely outside it (a widget straddling an edge is left alone — only a
    /// widget that would be completely unreachable is moved).</summary>
    public static WidgetPlacement ClampToVirtualDesktop(WidgetPlacement placement, (float Left, float Top, float Right, float Bottom) virtualDesktopBounds)
    {
        var (left, top, width, height) = placement.ToRect();
        bool entirelyOutside = left + width <= virtualDesktopBounds.Left
            || left >= virtualDesktopBounds.Right
            || top + height <= virtualDesktopBounds.Top
            || top >= virtualDesktopBounds.Bottom;

        if (!entirelyOutside) return placement;

        float clampedX = placement.Anchor is PlacementAnchor.TopRight or PlacementAnchor.BottomRight
            ? virtualDesktopBounds.Left + width
            : virtualDesktopBounds.Left;
        float clampedY = placement.Anchor is PlacementAnchor.BottomLeft or PlacementAnchor.BottomRight
            ? virtualDesktopBounds.Top + height
            : virtualDesktopBounds.Top;

        return placement with { X = clampedX, Y = clampedY };
    }
}
