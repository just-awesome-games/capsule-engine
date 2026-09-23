using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.UI;

/// <summary>
/// Makes its entity an item a <see cref="FocusNavigator"/> can focus and press: a hit box, the
/// focus state, and the three events a menu item, a tab or a dialogue choice reacts to. It draws
/// nothing.
/// </summary>
/// <remarks>
/// The entity gives the focus its look in <see cref="Focused"/> and <see cref="Unfocused"/>.
/// <para>
/// Only a navigator holding this focusable raises its events. A focusable in no navigator is inert,
/// and the neighbours it names are data until a navigator reads them.
/// </para>
/// </remarks>
/// <param name="size">The extent the hit box covers. See <see cref="Size"/>.</param>
public sealed class Focusable(Vector2 size) : Component
{
    /// <summary>
    /// The extent the hit box covers, in the entity's units: world units on a world entity and canvas pixels
    /// on a screen entity. A non-positive axis is never under the pointer, but the directional actions still
    /// reach the item.
    /// </summary>
    public Vector2 Size { get; set; } = size;

    /// <summary>
    /// The point in the entity's own space the hit box's top-left corner lands on, placed by the
    /// entity's world transform. Zero by default, which puts the corner on the entity.
    /// </summary>
    public Vector2 Offset { get; set; }

    /// <summary>
    /// The hit box in the space the entity draws in: world units under a world root, or canvas
    /// pixels with the <see cref="ScreenEntity.Anchor"/> resolved under a screen one.
    /// </summary>
    /// <remarks>
    /// The corner is placed by the entity's <see cref="Entity.WorldTransform"/> and spans
    /// <see cref="Size"/> times its scale. A hit box cannot turn. Rotation anywhere in the entity's
    /// ancestry is refused while a focusable is present. Reads the entity's current transform, and
    /// reads <c>default</c> while attached to no entity.
    /// </remarks>
    public Rect Bounds => Entity is { } entity
        ? new Rect(entity.World.TransformPoint(Offset) + entity.SpaceOrigin, Size * entity.World.Scale)
        : default;

    /// <summary>
    /// The item a navigator's up direction moves to from this one, read before the geometry. Null,
    /// the default, leaves that direction to the geometry and its wrap.
    /// </summary>
    /// <remarks>
    /// Naming this item blocks the direction and moves nothing. A named item that is not live hands
    /// the move on to its own <see cref="Up"/>. See <see cref="FocusNavigator"/> for the chain and
    /// the geometry.
    /// </remarks>
    public Focusable? Up { get; set; }

    /// <summary>The item a navigator's down direction moves to, read as <see cref="Up"/> is.</summary>
    public Focusable? Down { get; set; }

    /// <summary>The item a navigator's left direction moves to, read as <see cref="Up"/> is.</summary>
    public Focusable? Left { get; set; }

    /// <summary>The item a navigator's right direction moves to, read as <see cref="Up"/> is.</summary>
    public Focusable? Right { get; set; }

    /// <summary>
    /// Whether a navigator's focus is on this item. False until the navigator holding it starts,
    /// which is when its starting item takes the focus.
    /// </summary>
    public bool IsFocused { get; private set; }

    internal override TransformSupport Supports => TransformSupport.Scale;

    /// <summary>
    /// Raised as the focus lands on this item, with <see cref="IsFocused"/> already true and before
    /// the navigator's <see cref="FocusNavigator.FocusChanged"/>.
    /// </summary>
    /// <remarks>
    /// Handlers run synchronously, in subscription order. A handler that changes the navigator's
    /// focus or items throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    public event Action? Focused;

    /// <summary>
    /// Raised as the focus leaves this item, with <see cref="IsFocused"/> already false and before
    /// the item taking the focus is told it has it. When the navigator holds no other live item, no
    /// item takes the focus.
    /// </summary>
    public event Action? Unfocused;

    /// <summary>
    /// Raised when this item is pressed. Only the focused item is pressed, and the press comes
    /// after that step's focus events.
    /// </summary>
    /// <remarks>At most once per step, however many actions asked for it.</remarks>
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

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("IsFocused", IsFocused);
        panel.Field("Size", Size);
        panel.Field("Offset", Offset);
    }
}
