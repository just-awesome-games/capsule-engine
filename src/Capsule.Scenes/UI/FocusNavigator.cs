using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.UI;

/// <summary>
/// Moves a focus between <see cref="Focusable"/> items and presses the focused one.
/// </summary>
/// <remarks>
/// It steps itself and draws nothing. The items supply the focus visuals and decide what pressing
/// means, and they need not sit on this component's entity. An item is live while its
/// <see cref="Component.Entity"/> is in a scene, or queued to join one and not queued to leave, and
/// is shown by <see cref="Entity.Visible"/> up its ancestry. Only live items take part in
/// directions, the pointer and the press.
/// <para>
/// A direction first takes the neighbour the focused item names for that side. When the item names
/// none, the focus moves to the item lying that way with the highest
/// <c>dot(direction, delta) / |delta|²</c> from the focused item's <see cref="Focusable.Bounds"/>
/// centre, with ties to list order. When nothing lies that way, the focus wraps to the item farthest
/// the other way. An item that names itself blocks that direction. A named item that is not live
/// passes the move on to the item it names in the same direction. A chain that names nothing
/// further, returns to an item it already visited, or reaches an item this navigator does not hold
/// moves nothing.
/// </para>
/// <para>
/// A focus that lands on an item by a direction, a <see cref="Focus"/> call, the start or a repair scrolls the
/// item into view in each <see cref="ScrollContainer"/> above it. A pointer hover or click does not.
/// </para>
/// <para>
/// A focus change applies immediately, and handlers read the new focus. Changing this navigator's
/// items or its focus from inside one of its own focus events throws
/// <see cref="InvalidOperationException"/>.
/// </para>
/// </remarks>
public sealed class FocusNavigator : Component
{
    private readonly List<Focusable> _items = [];
    private readonly FocusActions _actions;

    // Confirm first, then the secondary actions.
    private readonly InputAction[] _presses;

    // Whether this navigator collects the items in its entity's subtree.
    private readonly bool _gathers;

    private bool _started;
    private bool _raising;

    // Set by the Interactable setter on a false-to-true transition, and cleared by the next OnStep,
    // whose read this navigator then skips.
    private bool _justTurnedInteractable;

    // Set when Leave drops the focused item, and cleared by every Move. The item may be live again
    // elsewhere before the next step, and the step repairs the focus all the same.
    private bool _focusDeparted;

    // The repeat of a held direction. It counts while any direction is held and none was pressed
    // this step.
    private HoldRepeat _repeat;

    // The item a held click is dragging, or null, and where that click pressed it.
    private Focusable? _dragging;
    private Vector2 _dragStart;

    // Wheel notches turned over the focused adjusting item and not yet raised as a whole step. It
    // keeps the sign of the turn and clears when the focus moves.
    private float _notches;

    /// <summary>
    /// Raised with the item the focus landed on, after that item's <see cref="Focusable.Focused"/>
    /// and before anything presses it in the same step.
    /// </summary>
    /// <remarks>
    /// The navigator also raises it for the starting item's focus. It does not raise it when the
    /// focus is released to no item.
    /// </remarks>
    public event Action<Focusable>? FocusChanged;

    /// <summary>
    /// Raised on the press edge of <see cref="FocusActions.Cancel"/>, after that step's focus move and
    /// press, at most once per step.
    /// </summary>
    /// <remarks>
    /// It is raised while this navigator holds no live focus too, and an empty menu can still be left. It
    /// is gated by <see cref="Interactable"/> as every other action is.
    /// </remarks>
    public event Action? Canceled;

    /// <summary>
    /// The item that has the focus, or null while this navigator holds none. Before the navigator
    /// starts, this names the item that will take the focus at start.
    /// </summary>
    public Focusable? Focused { get; private set; }

    /// <summary>
    /// Whether this navigator reads input, true by default. False holds the focus where it is and
    /// ignores every action and the pointer, as a menu does while a rebinding prompt owns the input.
    /// </summary>
    /// <remarks>
    /// The step on which it turns true reads nothing either. The press that ended the prompt never
    /// lands on the menu.
    /// </remarks>
    public bool Interactable
    {
        get;
        set
        {
            _justTurnedInteractable |= value && !field;
            field = value;
        }
    } = true;

    /// <summary>
    /// How many steps a direction must be held after its press before it first repeats. Defaults to
    /// 30, half a second at sixty steps.
    /// </summary>
    /// <remarks>Read on each step, and a new value takes effect next step.</remarks>
    public int RepeatDelay
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 30;

    /// <summary>
    /// How many steps pass between repeats of a held direction. Defaults to 6, a tenth of a second
    /// at sixty steps.
    /// </summary>
    /// <remarks>
    /// Zero makes a held direction move once on its press. Read on each step, and a new value takes
    /// effect next step.
    /// </remarks>
    public int RepeatInterval
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 6;

    /// <summary>
    /// Navigates the items it gathers from its entity's subtree, and the first item takes the focus when
    /// this navigator starts.
    /// </summary>
    /// <remarks>
    /// At start it gathers every <see cref="Focusable"/> under its entity in tree order: the entity, its
    /// components in attachment order, then each child's subtree in child order. A subtree under an
    /// entity holding another gathering navigator belongs to that navigator. After start, an item that
    /// joins the scene under it is appended, and one that leaves its scene drops out. A focus on the item
    /// that left moves on at this navigator's next step. <see cref="Add"/> and <see cref="Remove"/> still
    /// work.
    /// </remarks>
    /// <param name="actions">The actions that drive this navigator for its whole life.</param>
    /// <exception cref="ArgumentException"><see cref="FocusActions.Secondary"/> holds confirm.</exception>
    public FocusNavigator(FocusActions actions)
    {
        _actions = actions;
        _presses = Presses(actions);
        _gathers = true;
    }

    /// <summary>
    /// Navigates <paramref name="items"/>, and the first item takes the focus when this navigator
    /// starts. It gathers nothing.
    /// </summary>
    /// <remarks>
    /// Directions come from where the items sit, not from list order. One call serves a column, a
    /// row or a grid. Named neighbours are checked once every item is held, and the items may
    /// already name each other in a ring.
    /// </remarks>
    /// <param name="actions">The actions that drive this navigator for its whole life.</param>
    /// <param name="items">The items the focus moves between. Each must be non-null and listed once.</param>
    /// <exception cref="ArgumentException"><see cref="FocusActions.Secondary"/> holds confirm.</exception>
    public FocusNavigator(FocusActions actions, params ReadOnlySpan<Focusable> items)
    {
        _actions = actions;
        _presses = Presses(actions);

        foreach (Focusable item in items)
        {
            Append(item);
        }

        foreach (Focusable item in items)
        {
            RequireNeighboursHeld(item);
        }

        Focused = First();
    }

    /// <summary>The items the focus moves between, in list order.</summary>
    /// <remarks>
    /// The pointer hit-tests them in this order and a direction breaks ties by it. The next
    /// <see cref="Add"/> or <see cref="Remove"/> invalidates the span.
    /// </remarks>
    public ReadOnlySpan<Focusable> Items => CollectionsMarshal.AsSpan(_items);

    /// <summary>Appends <paramref name="item"/> to the end of the list.</summary>
    /// <remarks>
    /// The first item appended is the starting item. If the navigator has already started and holds
    /// no focus, the new item takes the focus immediately, or the first live item does when the new
    /// item is not live.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The item is already in <see cref="Items"/>, or it names a neighbour this navigator does not
    /// hold. Add the neighbour first, or name it once both are held.
    /// </exception>
    public void Add(Focusable item)
    {
        RequireNotRaising();
        Append(item);
        RequireNeighboursHeld(item);
        Seat(item);
    }

    /// <summary>
    /// Takes <paramref name="item"/> out of the list, and does nothing when this navigator does not
    /// hold it. Removing the focused item releases its focus and lands the focus immediately on the
    /// first live item in list order, or on no item.
    /// </summary>
    /// <returns>Whether the item was in <see cref="Items"/>.</returns>
    public bool Remove(Focusable item)
    {
        ArgumentNullException.ThrowIfNull(item);
        RequireNotRaising();

        if (!_items.Remove(item))
        {
            return false;
        }

        if (ReferenceEquals(Focused, item))
        {
            Move(_started ? FirstLive() : First());
        }

        return true;
    }

    /// <summary>
    /// Moves the focus onto <paramref name="item"/>, raising the same events an input move raises,
    /// and does nothing when that item already has the focus.
    /// </summary>
    /// <remarks>
    /// An item that is not live passes the focus to the first live item in list order. Called
    /// before this navigator starts, it sets the starting item and raises nothing.
    /// </remarks>
    /// <param name="item">The item to focus. This navigator must already hold it.</param>
    /// <exception cref="ArgumentException">The item is not in <see cref="Items"/>.</exception>
    public void Focus(Focusable item)
    {
        ArgumentNullException.ThrowIfNull(item);
        RequireNotRaising();

        if (!_items.Contains(item))
        {
            throw new ArgumentException($"{nameof(Focusable)} is not an item of this navigator. Add it first.", nameof(item));
        }

        // Before the start, liveness is not read yet.
        Move(!_started || Live(item) ? item : FirstLive());
    }

    /// <summary>Gives the starting item the focus.</summary>
    /// <remarks>
    /// Liveness is read here for the first time. A starting item that is not live passes the focus
    /// to the first live item in list order, or leaves this navigator holding no focus. The
    /// navigator raises nothing before this call.
    /// </remarks>
    protected internal override void OnStart()
    {
        // A gathered item may name a neighbour outside the subtree. A chain that reaches it moves nothing.
        if (_gathers && Entity is { } entity)
        {
            Gather(entity);
            Focused ??= First();
        }

        _started = true;

        if (Focused is not { } starting)
        {
            return;
        }

        Focused = Live(starting) ? starting : FirstLive();

        if (Focused is { } landing)
        {
            Reveal(landing);
            Raise(left: null, landing);
        }
    }

    /// <summary>
    /// Reads one step of the directions, the pointer and the presses, and raises at most one focus
    /// move and at most one press. A direction moves the focus on its press edge, again once it has
    /// been held <see cref="RepeatDelay"/> steps, and then every <see cref="RepeatInterval"/>
    /// steps.
    /// </summary>
    /// <remarks>
    /// One repeat counter serves every direction. A step holding two directions uses the first of up,
    /// down, left and right. A direction on the focused item's <see cref="Focusable.Adjusts"/> axis
    /// adjusts that item in place of moving the focus. The press actions are read on their press edge
    /// only.
    /// <para>
    /// A pointer that moved this step focuses the first item in list order whose bounds contain it.
    /// The pointer and its buttons reach only items on a <see cref="ScreenEntity"/>, and only where its
    /// <see cref="ClipRect"/> shows it. A mouse button bound to a press action presses the item under
    /// the pointer whether or not the pointer moved, and does nothing over empty space.
    /// </para>
    /// <para>
    /// A step that finds no live focus moves to one and reads nothing else but
    /// <see cref="FocusActions.Cancel"/>. A handler that takes the landing item out of its scene drops
    /// the press.
    /// </para>
    /// <para>
    /// A mouse button bound to confirm that presses an item captures the pointer for it until the
    /// button is released or the focus leaves the item. While captured, the pointer focuses nothing and
    /// the item raises <see cref="Focusable.Dragged"/> on the press and on each move.
    /// </para>
    /// <para>
    /// The wheel adjusts the focused item when that item has an <see cref="Focusable.Adjusts"/> axis and
    /// the pointer is over it. Each whole notch raises one adjust. The vertical wheel always counts, and
    /// the horizontal wheel counts only on a horizontal item.
    /// </para>
    /// </remarks>
    protected internal override void OnStep(in StepContext context)
    {
        bool justTurnedInteractable = _justTurnedInteractable;
        _justTurnedInteractable = false;

        if (!Interactable || justTurnedInteractable)
        {
            _repeat = default;
            _dragging = null;
            _notches = 0f;

            return;
        }

        InputState input = context.Input;

        Side? pressedSide = Direction(input, pressed: true);
        Side? heldSide = Direction(input, pressed: false);
        bool repeat = _repeat.Next(heldSide is not null, pressedSide is not null, RepeatDelay, RepeatInterval);

        if (_focusDeparted || Focused is not { } focused || !Live(focused))
        {
            Move(FirstLive());
            Cancel(input);

            return;
        }

        if (_dragging is not null && !input.IsHeld(_actions.Confirm, mouse: true))
        {
            _dragging = null;
        }

        bool captured = _dragging is not null;

        Focusable target = focused;
        bool fromPointer = false;

        // One hit test per step. Pointer focusing requires the pointer to have moved, but a click does
        // not. Clicking without nudging the mouse still picks the item under it.
        bool clicking = Clicked(input);
        bool hovering = input.PointerMoved && !captured;
        Focusable? under = hovering || clicking ? Under(input.Pointer) : null;

        if (hovering && under is not null)
        {
            target = under;
            fromPointer = true;
        }

        Focusable? adjusting = null;
        int adjust = 0;

        if ((pressedSide ?? (repeat ? heldSide : null)) is { } side)
        {
            if (target.Adjusts == AxisOf(side))
            {
                adjusting = target;
                adjust = side is Side.Right or Side.Up ? 1 : -1;
            }
            else if (Reached(target, side) is { } moved)
            {
                target = moved;
                fromPointer = false;
            }
        }

        InputAction? pressing = Pressing(input, under, out bool clicked);

        if (clicked)
        {
            target = under!;
            fromPointer = true;
        }

        // Move first. A press handler then finds this navigator already focused on the pressed item.
        Move(target, fromPointer);

        bool dragStarted = clicked && pressing == _actions.Confirm && ReferenceEquals(Focused, under);

        if (dragStarted)
        {
            _dragging = under;
            _dragStart = input.Pointer;
        }

        if (adjusting is not null && Reading && ReferenceEquals(Focused, adjusting) && Live(adjusting))
        {
            adjusting.Adjust(adjust);
        }

        Wheel(input);

        if (pressing is { } action && Reading && Focused is { } pressed && Live(pressed))
        {
            pressed.Press(action, _actions.Confirm);
        }

        if (_dragging is { } dragged)
        {
            if (!Reading || !Live(dragged))
            {
                _dragging = null;
            }
            else if (dragStarted || input.PointerMoved)
            {
                dragged.Drag(new PointerDrag(_dragStart, input.Pointer));
            }
        }

        Cancel(input);
    }

    private static InputAction[] Presses(FocusActions actions)
    {
        ImmutableArray<InputAction> secondary = actions.Secondary.IsDefault ? [] : actions.Secondary;

        return secondary.Contains(actions.Confirm)
            ? throw new ArgumentException($"{nameof(FocusActions.Secondary)} holds the confirm action. List only the other actions that press an item.", nameof(actions))
            : [actions.Confirm, .. secondary];
    }

    private bool Clicked(InputState input)
    {
        foreach (InputAction action in _presses)
        {
            if (input.WasPressed(action, mouse: true))
            {
                return true;
            }
        }

        return false;
    }

    // A click over empty space falls through to the action's other buttons.
    private InputAction? Pressing(InputState input, Focusable? under, out bool clicked)
    {
        foreach (InputAction action in _presses)
        {
            clicked = under is not null && input.WasPressed(action, mouse: true);

            if (clicked || input.WasPressed(action, mouse: false))
            {
                return action;
            }
        }

        clicked = false;

        return null;
    }

    // Raises one adjust per whole notch the wheel turned over the focused adjusting item. A turn
    // anywhere else clears the notches it had gathered.
    private void Wheel(InputState input)
    {
        Vector2 scroll = input.Scroll;

        if (scroll == Vector2.Zero)
        {
            return;
        }

        if (Focused is not { Adjusts: { } axis } item || !Live(item) || item.Entity is not ScreenEntity ||
            !item.Bounds.Contains(input.Pointer) || !ClipRect.Admits(item.Entity, input.Pointer))
        {
            _notches = 0f;

            return;
        }

        _notches += scroll.Y + (axis == Axis.Horizontal ? scroll.X : 0f);

        int whole = (int)_notches;
        _notches -= whole;

        int step = Math.Sign(whole);

        for (int i = 0; i < Math.Abs(whole) && Reading && ReferenceEquals(Focused, item) && Live(item); i++)
        {
            item.Adjust(step);
        }
    }

    // Appends an item that joined the scene under a gathering navigator after it started. Its named
    // neighbours may join later in the same batch.
    internal static void Join(Focusable item)
    {
        for (Entity? above = item.Entity; above is not null; above = above.Parent)
        {
            if (GathererOn(above) is not { } navigator)
            {
                continue;
            }

            item.Gatherer = navigator;

            if (navigator._started && !navigator._items.Contains(item))
            {
                navigator.RequireNotRaising();
                navigator._items.Add(item);
                navigator.Seat(item);

                // A torn-down menu added back gives its remembered focus back to the item it left with. The
                // repair the item's departure queued is dropped with it.
                if (ReferenceEquals(navigator.Focused, item) && item.Holder is null)
                {
                    item.HoldQuietly(navigator);
                    navigator._focusDeparted = false;
                }
            }

            return;
        }
    }

    // Drops a gathered item leaving its scene and raises nothing. The item stops being focused at once,
    // since a torn-down menu never steps again to clear it. The navigator's focus moves on at its next step.
    internal void Leave(Focusable item)
    {
        if (!_items.Remove(item) || !ReferenceEquals(Focused, item))
        {
            return;
        }

        _focusDeparted = true;

        if (ReferenceEquals(item.Holder, this))
        {
            item.HoldQuietly(null);
        }
    }

    // A handler earlier in the step may have turned Interactable off, or off and on again, and either
    // way the step's cancel and drag are dropped.
    private bool Reading => Interactable && !_justTurnedInteractable;

    private void Cancel(InputState input)
    {
        if (Reading && _actions.Cancel is { } cancel && input.WasPressed(cancel))
        {
            Canceled?.Invoke();
        }
    }

    // Returns the gathering navigator on `entity`, or null.
    private static FocusNavigator? GathererOn(Entity entity)
    {
        foreach (Component component in entity.Components)
        {
            if (component is FocusNavigator { _gathers: true } navigator)
            {
                return navigator;
            }
        }

        return null;
    }

    // Collects the items under `entity` in tree order, leaving out a subtree another gathering navigator
    // owns.
    private void Gather(Entity entity)
    {
        foreach (Component component in entity.Components)
        {
            if (component is Focusable item)
            {
                item.Gatherer = this;

                if (!_items.Contains(item))
                {
                    _items.Add(item);
                }
            }
        }

        foreach (Entity child in entity.Children)
        {
            if (GathererOn(child) is null)
            {
                Gather(child);
            }
        }
    }

    // Lands the focus on a new item when this navigator holds none.
    private void Seat(Focusable item)
    {
        if (Focused is not null)
        {
            return;
        }

        if (_started)
        {
            Move(Live(item) ? item : FirstLive());
        }
        else
        {
            Focused = item;
        }
    }

    // Returns the first of up, down, left and right the step reports, or null. One walk serves both
    // edges: a press moves the focus and a hold drives the repeat counter.
    private Side? Direction(InputState input, bool pressed)
    {
        for (Side side = Side.Up; side <= Side.Right; side++)
        {
            InputAction action = side switch
            {
                Side.Up => _actions.Up,
                Side.Down => _actions.Down,
                Side.Left => _actions.Left,
                _ => _actions.Right,
            };

            if (pressed ? input.WasPressed(action) : input.IsHeld(action))
            {
                return side;
            }
        }

        return null;
    }

    // Returns the item `side` reaches from `from`: the neighbour it names for that side, falling back
    // to geometry when it names none. Returns null when the side is blocked or the chain ends without
    // a live item this navigator holds.
    private Focusable? Reached(Focusable from, Side side)
    {
        if (Named(from, side) is not { } named)
        {
            return Neighbour(from, Toward(side));
        }

        Focusable step = named;

        // The item count bounds the walk, because a longer chain must have revisited an item and a
        // cycle reaches nothing a shorter walk would not.
        for (int i = 0; i < _items.Count; i++)
        {
            if (ReferenceEquals(step, from) || !_items.Contains(step))
            {
                return null;
            }

            if (Live(step))
            {
                return step;
            }

            if (Named(step, side) is not { } next)
            {
                return null;
            }

            step = next;
        }

        return null;
    }

    private static Focusable? Named(Focusable item, Side side) =>
        side switch
        {
            Side.Up => item.Up,
            Side.Down => item.Down,
            Side.Left => item.Left,
            _ => item.Right,
        };

    private static Vector2 Toward(Side side) =>
        side switch
        {
            Side.Up => -Vector2.UnitY,
            Side.Down => Vector2.UnitY,
            Side.Left => -Vector2.UnitX,
            _ => Vector2.UnitX,
        };

    // Returns the live item `direction` reaches from `from`, or the wrap target when nothing lies that
    // way. Items lying that way rank by dot(direction, delta) / |delta|², which lets a near diagonal beat
    // a far aligned item. Ties go to list order, and the wrap target is the item farthest the other way.
    private Focusable? Neighbour(Focusable from, Vector2 direction)
    {
        Vector2 origin = from.Bounds.Center;

        Focusable? best = null;
        float bestScore = 0f;
        Focusable? wrap = null;
        float wrapDistance = 0f;

        foreach (Focusable item in Items)
        {
            if (ReferenceEquals(item, from) || !Live(item))
            {
                continue;
            }

            Vector2 delta = item.Bounds.Center - origin;
            float along = Vector2.Dot(direction, delta);

            if (along > 0f)
            {
                float score = along / delta.LengthSquared();

                if (best is null || score > bestScore)
                {
                    best = item;
                    bestScore = score;
                }
            }
            else if (along < 0f && (wrap is null || -along > wrapDistance))
            {
                wrap = item;
                wrapDistance = -along;
            }
        }

        return best ?? wrap;
    }

    // An entity queued to leave, by itself or with an ancestor, drops out of focus in the same step
    // instead of one step later. An entity queued to join counts as live, which lets a menu
    // rebuilt during a step focus the row it just added when that step's queue drains.
    internal static bool Live(Focusable item) =>
        item.Entity is { } entity &&
        (entity.SceneOrNull is not null || entity.PendingScene is not null) &&
        !entity.IsRemovalPending &&
        entity.ShownInTree;

    // Returns the first screen item containing the pointer, or null. World items are skipped, because
    // the pointer is in canvas pixels and their bounds are in world units. A clip above an item cuts
    // it from the pointer where it cuts it from view, and a direction still reaches it.
    private Focusable? Under(Vector2 pointer)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            Focusable item = _items[i];

            if (Live(item) && item.Entity is ScreenEntity && item.Bounds.Contains(pointer) && ClipRect.Admits(item.Entity, pointer))
            {
                return item;
            }
        }

        return null;
    }

    private Focusable? FirstLive() => _items.Find(Live);

    private Focusable? First() => _items.Count > 0 ? _items[0] : null;

    private void Append(Focusable item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (_items.Contains(item))
        {
            throw new ArgumentException($"{nameof(Focusable)} is already an item of this navigator. List it once.", nameof(item));
        }

        _items.Add(item);
    }

    private void RequireNeighboursHeld(Focusable item)
    {
        for (Side side = Side.Up; side <= Side.Right; side++)
        {
            if (Named(item, side) is { } named && !_items.Contains(named))
            {
                throw new ArgumentException(
                    $"{nameof(Focusable)} names a {side} neighbour this navigator does not hold. Add that item first.",
                    nameof(item));
            }
        }
    }

    // Applies the move before raising anything. A handler then reads the focus it landed on. A landing
    // that did not come from the pointer scrolls its item into view first. A resting mouse never has
    // rows slide under it.
    private void Move(Focusable? landing, bool fromPointer = false)
    {
        _focusDeparted = false;

        if (ReferenceEquals(landing, Focused))
        {
            return;
        }

        Focusable? left = Focused;
        Focused = landing;
        _notches = 0f;
        _dragging = null;

        if (_started)
        {
            if (landing is not null && !fromPointer)
            {
                Reveal(landing);
            }

            Raise(left, landing);
        }
    }

    // Scrolls `item` into view in each scroll container above it, innermost first. A scroll moves the
    // item. Each container reads its bounds afresh.
    private static void Reveal(Focusable item)
    {
        if (item.Entity is not ScreenEntity)
        {
            return;
        }

        for (Entity? above = item.Entity.Parent; above is not null; above = above.Parent)
        {
            if (above is ScrollContainer scroller)
            {
                scroller.Reveal(item.Bounds);
            }
        }
    }

    private void Raise(Focusable? left, Focusable? landing)
    {
        _raising = true;

        try
        {
            // A departed item may already hold another navigator's focus, which this one leaves alone.
            if (left is not null && ReferenceEquals(left.Holder, this))
            {
                left.LoseFocus();
            }

            if (landing is { } item)
            {
                item.TakeFocus(this);
                FocusChanged?.Invoke(item);
            }
        }
        finally
        {
            _raising = false;
        }
    }

    private void RequireNotRaising()
    {
        if (_raising)
        {
            throw new InvalidOperationException(
                "A FocusNavigator cannot change its items or its focus while its own focus events are being raised.");
        }
    }

    private static Axis AxisOf(Side side) => side is Side.Left or Side.Right ? Axis.Horizontal : Axis.Vertical;

    private enum Side
    {
        Up,
        Down,
        Left,
        Right,
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("Items", Items.Length);
        panel.Field("Focused", Focused?.Entity);
    }
}
