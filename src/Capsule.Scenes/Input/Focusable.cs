using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Scenes.Input;

/// <summary>
/// Makes its entity an item a <see cref="FocusNavigator"/> can focus and press: a hit box, the
/// focus state, and the three events a menu item, a tab or a dialogue choice reacts to. It draws
/// nothing — what the focus looks like is whatever the entity does in <see cref="Focused"/> and
/// <see cref="Unfocused"/>.
/// <para>
/// Only a navigator holding this focusable raises its events, so one in no navigator is inert; the
/// neighbours it names are data until a navigator reads them.
/// </para>
/// </summary>
/// <param name="size">The extent the hit box covers; see <see cref="Size"/>.</param>
public sealed class Focusable(Vector2 size) : Component
{
    /// <summary>
    /// The extent the hit box covers, in the entity's units — world units on a world entity and
    /// canvas pixels on a screen one. A non-positive axis is never under the pointer, and is still
    /// reached by the directional actions.
    /// </summary>
    public Vector2 Size { get; set; } = size;

    /// <summary>
    /// Added to the entity's position to give the hit box's top-left corner. In the entity's own
    /// units; zero by default, which puts the corner on the entity.
    /// </summary>
    public Vector2 Offset { get; set; }

    /// <summary>
    /// The hit box in the space the entity draws in — world units on a world entity, canvas pixels
    /// with the <see cref="ScreenEntity.Anchor"/> resolved on a screen one. Read from the entity's
    /// current position; <c>default</c> while attached to no entity.
    /// </summary>
    public Rect Bounds => Entity is { } entity ? new Rect(entity.Position + entity.SpaceOrigin + Offset, Size) : default;

    /// <summary>
    /// The item a navigator's up direction moves to from this one, read before the geometry. Null —
    /// the default — leaves that direction to the geometry and its wrap, and this item itself blocks
    /// it, moving nothing; a named item that is not live hands the move on to its own
    /// <see cref="Up"/>. See <see cref="FocusNavigator"/> for the chain and the geometry.
    /// </summary>
    public Focusable? Up { get; set; }

    /// <summary>
    /// The item a navigator's down direction moves to from this one, read before the geometry. Null —
    /// the default — leaves that direction to the geometry and its wrap, and this item itself blocks
    /// it, moving nothing; a named item that is not live hands the move on to its own
    /// <see cref="Down"/>. See <see cref="FocusNavigator"/> for the chain and the geometry.
    /// </summary>
    public Focusable? Down { get; set; }

    /// <summary>
    /// The item a navigator's left direction moves to from this one, read before the geometry. Null —
    /// the default — leaves that direction to the geometry and its wrap, and this item itself blocks
    /// it, moving nothing; a named item that is not live hands the move on to its own
    /// <see cref="Left"/>. See <see cref="FocusNavigator"/> for the chain and the geometry.
    /// </summary>
    public Focusable? Left { get; set; }

    /// <summary>
    /// The item a navigator's right direction moves to from this one, read before the geometry. Null
    /// — the default — leaves that direction to the geometry and its wrap, and this item itself
    /// blocks it, moving nothing; a named item that is not live hands the move on to its own
    /// <see cref="Right"/>. See <see cref="FocusNavigator"/> for the chain and the geometry.
    /// </summary>
    public Focusable? Right { get; set; }

    /// <summary>
    /// Whether a navigator's focus is on this item. False until the navigator holding it starts,
    /// which is when its starting item takes the focus.
    /// </summary>
    public bool IsFocused { get; private set; }

    /// <summary>
    /// Raised as the focus lands on this item, with <see cref="IsFocused"/> already true and before
    /// the navigator's <see cref="FocusNavigator.FocusChanged"/>. Handlers run synchronously, in
    /// subscription order; one that moves the focus on is queued, per
    /// <see cref="FocusNavigator.Focus"/>.
    /// </summary>
    public event Action? Focused;

    /// <summary>
    /// Raised as the focus leaves this item, with <see cref="IsFocused"/> already false and before
    /// the item taking the focus is told it has it — or with no item taking it, where the navigator
    /// released this item because it is no longer live.
    /// </summary>
    public event Action? Unfocused;

    /// <summary>
    /// Raised when this item is pressed, which reaches the focused item alone and comes after that
    /// step's focus events. At most once per step however many of the step's actions asked for it.
    /// </summary>
    public event Action? Pressed;

    internal void TakeFocus()
    {
        IsFocused = true;
        Focused?.Invoke();
    }

    internal void LoseFocus()
    {
        IsFocused = false;
        Unfocused?.Invoke();
    }

    internal void Press() => Pressed?.Invoke();
}
