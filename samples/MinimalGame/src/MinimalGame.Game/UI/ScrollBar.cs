using System.Numerics;
using Capsule;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace MinimalGame.Game.UI;

/// <summary>A slim bar beside a scrolling list, with a thumb that shows and sets how far the list has scrolled.</summary>
/// <remarks>
/// It hides itself while the list fits its frame. The bar takes the menu's focus as a slider does: up and down
/// scroll the list by a row, dragging the thumb or the track sets its place, and the wheel over the focused bar
/// scrolls it by a row a notch.
/// </remarks>
public sealed class ScrollBar : ScreenEntity
{
    /// <summary>The bar's width in canvas pixels.</summary>
    public const float Width = 4f;

    private const float ThumbLength = 12f;

    private static readonly ColorRgba TrackColor = new(24, 24, 32);
    private static readonly ColorRgba ThumbColor = new(110, 110, 130);

    private readonly ScrollContainer _list;
    private readonly ScreenEntity _thumb = new(Anchor.TopWide, Vector2.Zero) { Size = new Vector2(0f, ThumbLength) };
    private readonly ColorRect _thumbFill = new() { Color = ThumbColor };
    private readonly Focusable _focus = new() { Adjusts = Axis.Vertical };

    /// <param name="list">The scrolling list this bar shows and sets the place of.</param>
    public ScrollBar(ScrollContainer list)
        : base(Anchor.RightTall, Vector2.Zero)
    {
        _list = list;
        Size = new Vector2(Width, 0f);

        Add(new ColorRect { Color = TrackColor });
        _thumb.Parent = this;
        _thumb.Add(_thumbFill);

        // The menu's navigator gathers this bar. Nothing else needs to hold it.
        _focus.Focused += () => _thumbFill.Color = ColorRgba.White;
        _focus.Unfocused += () => _thumbFill.Color = ThumbColor;
        _focus.Adjusted += step => ScrollTo(_list.ScrollOffset.Y - (step * (MenuItem.BoxHeight + MenuItem.Spacing)));
        _focus.Dragged += drag =>
        {
            Rect track = _focus.Bounds;
            float free = track.Size.Y - ThumbLength;

            if (free > 0f)
            {
                ScrollTo((drag.Position.Y - track.Top - (ThumbLength / 2f)) / free * _list.MaxScrollOffset.Y);
            }
        };

        Add(_focus);
    }

    // Runs after the wheel and the navigator have written the offset. The thumb never trails the list.
    /// <inheritdoc/>
    protected override void OnLateStep(in StepContext context)
    {
        float max = _list.MaxScrollOffset.Y;
        Visible = max > 0f;

        if (max > 0f)
        {
            float free = Math.Max(0f, _focus.Bounds.Size.Y - ThumbLength);
            _thumb.Position = new Vector2(0f, _list.ScrollOffset.Y / max * free);
        }
    }

    private void ScrollTo(float offset) => _list.ScrollOffset = new Vector2(_list.ScrollOffset.X, offset);
}
