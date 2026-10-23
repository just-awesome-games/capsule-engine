# UI

After this page you can lay out panels and text on the screen layer and build a menu that the keys, a
pad and the pointer all drive. Each type's XML docs hold its rules.

The screen layer is in canvas pixels and draws over the world, or among it from a root that sets
`ScreenEntity.SortsWithWorld` ([`rendering.md`](rendering.md#two-layers-and-draw-order)). Capsule
places rects, lines them up, clips and scrolls them, and moves a focus between items. What an item
looks like when focused, and what pressing it does, are the game's. Capsule ships no stock widgets. A
meter, a slider or a scroll bar is a few sprites the game sizes from a value, and the sample's
[`UI/`](../samples/MinimalGame/src/MinimalGame.Game/UI/) folder builds all three.

| Need | Piece |
| --- | --- |
| A rect placed on the canvas or in a parent | `ScreenEntity`, with `Anchor`, `Size` and `Padding` as `Insets` |
| Children in a row, a column or a grid | `BoxContainer`, `GridContainer` |
| A list longer than its frame | `ScrollContainer` |
| A frame nothing draws or points past | `ClipRect` |
| A panel, a fill or a caption | `NineSlice`, `ColorRect`, `Label` |
| An item a menu can focus and press | `Focusable` |
| A menu | `FocusNavigator`, driven by the game's `FocusActions` |
| A slider dragged by the pointer | `Focusable.Dragged`, which hands a `PointerDrag` |
| A held direction that repeats | `FocusNavigator.RepeatDelay` and `RepeatInterval` |

## Layout

A `ScreenEntity` sits at its `Anchor` and offset in the slot its parent gives it, or in the canvas for a
root. A resized canvas or parent moves everything under it. A plain parent's slot is its rect inset by
its `Padding`. A `BoxContainer` or `GridContainer` gives each child a cell and fits itself to them. A
`ColorRect`, `NineSlice`, `Label` or `Focusable` built without a size fills its entity's rect.

A `ScrollContainer` clips its children and moves them back by `ScrollOffset` on each axis their content
overflows.

## Focus

A `FocusNavigator` built with only its actions gathers every `Focusable` under its entity when it starts,
and one focus then moves across every container beneath it. The pointer reaches only items on a
`ScreenEntity`, and never one a `ClipRect` cuts away. A prompt that owns the input, such as "press a
button", sets `FocusNavigator.Interactable` false while it waits
([`input.md`](input.md#rebind-at-a-settings-screen)).

A game declares its `FocusActions` once beside its other actions and hands them to every navigator. A
vertical menu of two buttons:

```csharp
public sealed class TitleScreen : Scene
{
    public TitleScreen()
    {
        BoxContainer menu = new(Axis.Vertical, Anchor.Center, Vector2.Zero) { Spacing = 4f };
        menu.Add(new FocusNavigator(GameInput.MenuFocus));
        Button(menu, "Start").Pressed += () => Run.RequestScene<PlayField>();
        Button(menu, "Quit").Pressed += () => Run.RequestExit();
        Add(menu);
    }

    private static Focusable Button(BoxContainer menu, string caption)
    {
        ScreenEntity button = new(Anchor.TopWide, Vector2.Zero) { Parent = menu, Size = new Vector2(88f, 16f) };
        Label label = new(BitmapFont.Default, caption) { HorizontalAlignment = HorizontalAlignment.Center };
        Focusable focus = new();
        focus.Focused += () => label.Color = ColorRgba.Gold;
        focus.Unfocused += () => label.Color = ColorRgba.White;
        button.Add(label);
        button.Add(focus);
        return focus;
    }
}
```

`GameInput.MenuFocus` is a `FocusActions` the game declares from its own up, down, left, right and
confirm actions ([`input.md`](input.md#declare-the-actions)).
