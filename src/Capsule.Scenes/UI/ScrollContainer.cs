using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.UI;

/// <summary>
/// A screen entity that shows a window onto children longer than its rect, clipped to the rect and moved by
/// <see cref="ScrollOffset"/>.
/// </summary>
/// <remarks>
/// The children are placed as a plain parent places them, in the padded inside. On each axis a child's slot is the
/// larger of the padded inside and the child's preferred extent, moved back by <see cref="ScrollOffset"/>. An
/// axis scrolls only where its content overflows. The container never grows to fit its children. It clips
/// with a <see cref="ClipRect"/> of its own.
/// <para>
/// The wheel scrolls the innermost container under the pointer, each axis by its own wheel axis, and never
/// chains to the one above at a limit. A focused <see cref="Focusable"/> under the pointer keeps the wheel axes
/// its <see cref="Focusable.Adjusts"/> reads.
/// </para>
/// <para>
/// A <see cref="FocusNavigator"/> that moves the focus onto an item inside the container by a direction, a
/// <see cref="FocusNavigator.Focus"/> call or its start scrolls the item into view by the smallest scroll,
/// within <see cref="ScreenEntity.Padding"/>. A pointer hover or click never scrolls. Rows would otherwise slide
/// under a resting mouse. A scroll bar or a drag is the game's, written through <see cref="ScrollOffset"/>.
/// </para>
/// </remarks>
/// <example>
/// A list of rows in a frame that shows five of them and scrolls the rest:
/// <code>
/// ScrollContainer frame = new(Anchor.Center, Vector2.Zero) { Size = new Vector2(104f, 80f) };
/// BoxContainer rows = new(Axis.Vertical, Anchor.TopWide, Vector2.Zero) { Parent = frame, Spacing = 2f };
/// for (int i = 0; i &lt; 12; i++)
/// {
///     ScreenEntity row = new(Anchor.TopWide, Vector2.Zero) { Parent = rows, Size = new Vector2(0f, 14f) };
///     row.Add(new Focusable());
/// }
/// </code>
/// </example>
public class ScrollContainer : ScreenEntity
{
    private Vector2 _offset;
    private Vector2 _previousOffset;

    // The scene's StepsBegun at the last write. A write in a later step first keeps the offset it replaces
    // as the previous one, which saves the container from a per-step save over every entity.
    private int _writtenInStep;

    /// <summary>
    /// How far the content is moved back, in canvas pixels. Zero by default, which shows the content's
    /// start.
    /// </summary>
    /// <remarks>
    /// A read clamps to zero and <see cref="MaxScrollOffset"/>. A write past the end shows the end, and one made
    /// before the content exists takes effect once it does. A change interpolates between steps as a
    /// position does.
    /// </remarks>
    public Vector2 ScrollOffset
    {
        get => Vector2.Clamp(_offset, Vector2.Zero, MaxScrollOffset);

        set
        {
            Guard.Finite(value, nameof(value));

            int step = SceneOrNull?.StepsBegun ?? 0;
            if (step != _writtenInStep)
            {
                _previousOffset = _offset;
                _writtenInStep = step;
            }

            _offset = Vector2.Max(value, Vector2.Zero);
        }
    }

    /// <summary>
    /// How far the content can scroll on each axis, in canvas pixels: how much of the tallest and widest
    /// visible child lies past the padded inside. Zero on an axis whose content fits.
    /// </summary>
    public Vector2 MaxScrollOffset => MaxFor(Padding.Inside(Extent));

    /// <summary>
    /// The canvas pixels one wheel notch scrolls, where zero, the default, scrolls an eighth of the padded
    /// inside's extent on the axis the wheel turned.
    /// </summary>
    public float WheelStep
    {
        get;

        set
        {
            Guard.NonNegative(value, nameof(value));
            field = value;
        }
    }

    /// <param name="anchor">Where the container sits in the slot its parent gives it, or in the canvas for a root.</param>
    /// <param name="offset">Canvas pixels from the anchor to the same point of the container's rect.</param>
    public ScrollContainer(Anchor anchor, Vector2 offset)
        : base(anchor, offset)
    {
        Add(new ClipRect());
        Add(new ScrollWheel());
    }

    // The corner of `child`'s slot and its extent. The offset is clamped to what the content allows now.
    internal Vector2 Place(ScreenEntity child, Vector2 extent, bool previous, out Vector2 slot)
    {
        Vector2 inside = Padding.Inside(extent);
        Vector2 offset = Vector2.Clamp(previous ? PreviousOffset : _offset, Vector2.Zero, MaxFor(inside));
        slot = Vector2.Max(inside, child.Preferred);

        return new Vector2(Padding.Left, Padding.Top) - offset;
    }

    // Scrolls by the least that brings `item`, in canvas pixels, inside the padded inside. An item larger
    // than the inside lines its near edge up with it.
    internal void Reveal(in Rect item)
    {
        Rect outer = ClipRect.OuterRect(this, previous: false);
        Vector2 scale = World.Scale;
        Insets padding = Padding;
        Rect inside = new(
            outer.Left + (padding.Left * scale.X),
            outer.Top + (padding.Top * scale.Y),
            outer.Right - (padding.Right * scale.X),
            outer.Bottom - (padding.Bottom * scale.Y));

        Vector2 shift = new(
            Shift(item.Left, item.Right, inside.Left, inside.Right),
            Shift(item.Top, item.Bottom, inside.Top, inside.Bottom));

        if (shift != Vector2.Zero && scale.X != 0f && scale.Y != 0f)
        {
            ScrollOffset += shift / scale;
        }
    }

    // Scrolls by this step's wheel unless something else under the pointer takes it.
    internal void Wheel(InputState input)
    {
        Vector2 point = input.Pointer;

        if (!ShownInTree || !Contains(point))
        {
            return;
        }

        bool horizontal = true;
        bool vertical = true;
        Yield(this, point, ref horizontal, ref vertical);

        Vector2 inside = Padding.Inside(Extent);
        Vector2 step = WheelStep != 0f ? new Vector2(WheelStep) : inside / 8f;
        Vector2 scroll = input.Scroll;

        Vector2 delta = new(horizontal ? scroll.X * step.X : 0f, vertical ? -scroll.Y * step.Y : 0f);

        // A write stores the clamped read. Skipping a yielded turn keeps an offset written past the end.
        if (delta != Vector2.Zero)
        {
            ScrollOffset += delta;
        }
    }

    // The previous step's offset, or the current one where no write has happened since that step began.
    private Vector2 PreviousOffset
    {
        get
        {
            int step = SceneOrNull?.StepsBegun ?? 0;

            return step != 0 && step == _writtenInStep ? _previousOffset : _offset;
        }
    }

    // How much of the largest visible child lies past `inside`, per axis.
    private Vector2 MaxFor(Vector2 inside)
    {
        Vector2 content = Vector2.Zero;

        foreach (Entity entity in Children)
        {
            if (entity is ScreenEntity { Visible: true } child)
            {
                content = Vector2.Max(content, child.Preferred);
            }
        }

        return Vector2.Max(content - inside, Vector2.Zero);
    }

    private bool Contains(Vector2 point) =>
        ClipRect.OuterRect(this, previous: false).Contains(point) && ClipRect.Admits(this, point);

    // Clears each wheel axis something in `entity`'s subtree owns at `point`. A scroll container there owns
    // both. The focused adjusting item there owns the axes its navigator reads: the vertical wheel always, and
    // the horizontal one on a horizontal item.
    private static void Yield(Entity entity, Vector2 point, ref bool horizontal, ref bool vertical)
    {
        foreach (Component component in entity.Components)
        {
            if (component is Focusable { IsFocused: true, Adjusts: { } axis } item && item.Entity is ScreenEntity &&
                FocusNavigator.Live(item) && item.Bounds.Contains(point) && ClipRect.Admits(item.Entity, point))
            {
                vertical = false;
                horizontal &= axis != Axis.Horizontal;
            }
        }

        foreach (Entity child in entity.Children)
        {
            if (!horizontal && !vertical)
            {
                return;
            }

            if (!child.Visible)
            {
                continue;
            }

            if (child is ScrollContainer inner && inner.Contains(point))
            {
                horizontal = vertical = false;
            }
            else
            {
                Yield(child, point, ref horizontal, ref vertical);
            }
        }
    }

    // How far the content must move for an item spanning near to far to sit between from and to.
    private static float Shift(float near, float far, float from, float to)
    {
        if (far - near > to - from || near < from)
        {
            return near - from;
        }

        return far > to ? far - to : 0f;
    }

    // Reads the wheel for its container. A separate component keeps a subclass that overrides OnStep
    // without calling the base from silencing it.
    private sealed class ScrollWheel : Component
    {
        protected internal override void OnStep(in StepContext context)
        {
            if (context.Input.Scroll != Vector2.Zero)
            {
                ((ScrollContainer)Entity!).Wheel(context.Input);
            }
        }
    }
}
