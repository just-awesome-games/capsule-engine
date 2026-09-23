using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.UI;

/// <summary>
/// Moves a focus between <see cref="Focusable"/> items and presses the focused one. This is the state
/// machine behind a menu, a tab strip, a grid or a talent tree. It steps itself and draws nothing. The
/// items supply the focus visuals and decide what pressing means. The items need not sit on this
/// component's entity, or even on one entity. An item counts as live while its
/// <see cref="Component.Entity"/> is in a scene, or queued to join one and not queued to leave, and is
/// shown by <see cref="Entity.Visible"/> up its ancestry. Only live items take part in directions, the
/// pointer and the press.
/// <para>
/// A direction first takes the neighbour the focused item names for that side. When the item names
/// none, the navigator ranks the items lying that way from the focused item's
/// <see cref="Focusable.Bounds"/> centre by <c>dot(direction, delta) / |delta|²</c> and takes the
/// highest score, which lets a near diagonal beat a far aligned item. Deltas of <c>(40, 0)</c> and
/// <c>(10, 10)</c> score 0.025 and 0.05, so right moves to the diagonal. Ties go to list order, and
/// when nothing lies that way the focus wraps to the item farthest the other way. An item that names
/// itself blocks that direction. A named item that is not live passes the move on to the item it names
/// in the same direction, which keeps a column working while one of its items is out of the scene. A
/// chain that names nothing further, returns to an item it already visited, or reaches an item this
/// navigator does not hold moves nothing.
/// </para>
/// </summary>
/// <remarks>
/// A focus change applies immediately, so handlers read the new focus. Changing this navigator's
/// items or its focus from inside one of its own focus events throws, because a second move from there
/// would leave two items claiming the focus.
/// </remarks>
public sealed class FocusNavigator : Component
{
    private readonly List<Focusable> _items = [];
    private readonly FocusActions _actions;

    private bool _started;
    private bool _raising;
    private bool _interactable = true;

    // Set by the Interactable setter on a false-to-true transition, and cleared by the next OnStep,
    // whose read this navigator then skips.
    private bool _justTurnedInteractable;

    // How many steps a direction has been held since its press. Counts up while any direction is held
    // and none was pressed this step, and a press edge resets it to zero.
    private int _heldSteps;
    private int _repeatDelay = 30;
    private int _repeatInterval = 6;

    /// <summary>
    /// Navigates <paramref name="items"/>, and the first item takes the focus when this navigator
    /// starts. List order does not imply layout, because directions come from where the items sit, so
    /// one call serves a column, a row or a grid. Named neighbours are checked once every item is held,
    /// so the items may already name each other in a ring.
    /// </summary>
    /// <param name="actions">The actions that drive this navigator for its whole life.</param>
    /// <param name="items">The items the focus moves between. Each must be non-null and listed once.</param>
    public FocusNavigator(FocusActions actions, params ReadOnlySpan<Focusable> items)
    {
        _actions = actions;

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

    /// <summary>
    /// Raised with the item the focus landed on, after that item's <see cref="Focusable.Focused"/> and
    /// before anything presses it in the same step. The navigator also raises it for the starting
    /// item's focus. It does not raise it when the focus is released to no item.
    /// </summary>
    public event Action<Focusable>? FocusChanged;

    /// <summary>
    /// The item that has the focus, or null while this navigator holds none. Before the navigator
    /// starts, this names the item that will take the focus at start.
    /// </summary>
    public Focusable? Focused { get; private set; }

    /// <summary>
    /// Whether this navigator reads input. False holds the focus where it is and ignores every action
    /// and the pointer, which is what a menu sets while a rebinding prompt owns the input. The step on
    /// which it turns true reads nothing either, so the press that ended the prompt never lands on the
    /// menu.
    /// </summary>
    public bool Interactable
    {
        get => _interactable;
        set
        {
            if (value && !_interactable)
            {
                _justTurnedInteractable = true;
            }

            _interactable = value;
        }
    }

    /// <summary>
    /// How many steps a direction must be held after its press before it first repeats. Defaults to
    /// 30, half a second at sixty steps. Read on each step, and a new value takes effect next step.
    /// </summary>
    public int RepeatDelay
    {
        get => _repeatDelay;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _repeatDelay = value;
        }
    }

    /// <summary>
    /// How many steps pass between repeats of a held direction. Defaults to 6, a tenth of a second at
    /// sixty steps. Zero makes a held direction move once on its press. Read on each step, and a new
    /// value takes effect next step.
    /// </summary>
    public int RepeatInterval
    {
        get => _repeatInterval;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _repeatInterval = value;
        }
    }

    /// <summary>
    /// The items the focus moves between, in list order. The pointer hit-tests them in this order and
    /// a direction breaks ties by it. The next <see cref="Add"/> or <see cref="Remove"/> invalidates
    /// the span.
    /// </summary>
    public ReadOnlySpan<Focusable> Items => CollectionsMarshal.AsSpan(_items);

    /// <summary>
    /// Appends <paramref name="item"/> to the end of the list. The first item appended is the starting
    /// item. If the navigator has already started and holds no focus, the new item takes the focus
    /// immediately, or the first live item does when the new item is not live.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The item is already in <see cref="Items"/>, or it names a neighbour this navigator does not
    /// hold. Add the neighbour first, or name it once both are held.
    /// </exception>
    public void Add(Focusable item)
    {
        RequireNotRaising();
        Append(item);
        RequireNeighboursHeld(item);

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
    /// Moves the focus onto <paramref name="item"/>, raising the same events an input move raises, and
    /// does nothing when that item already has the focus. An item that is not live passes the focus to
    /// the first live item in list order. Called before this navigator starts, it sets the starting
    /// item and raises nothing.
    /// </summary>
    /// <param name="item">The item to focus. This navigator must already hold it.</param>
    /// <exception cref="ArgumentException">The item is not in <see cref="Items"/>.</exception>
    public void Focus(Focusable item)
    {
        ArgumentNullException.ThrowIfNull(item);
        RequireNotRaising();

        if (!_items.Contains(item))
        {
            throw new ArgumentException($"{nameof(Focusable)} is not an item of this navigator; add it first.", nameof(item));
        }

        Move(_started ? Live(item) ? item : FirstLive() : item);
    }

    /// <summary>
    /// Gives the starting item the focus. Liveness is read here for the first time. A starting item that
    /// is not live passes the focus to the first live item in list order, or leaves this navigator
    /// holding no focus. The navigator raises nothing before this call.
    /// </summary>
    protected internal override void OnStart()
    {
        _started = true;

        if (Focused is not { } starting)
        {
            return;
        }

        Focused = Live(starting) ? starting : FirstLive();

        if (Focused is { } landing)
        {
            Raise(left: null, landing);
        }
    }

    /// <summary>
    /// Reads one step of the directions, the pointer and the presses, and raises at most one focus move
    /// and at most one press. A direction moves the focus on its press edge, again once it has been
    /// held <see cref="RepeatDelay"/> steps, and then every <see cref="RepeatInterval"/> steps. One
    /// counter serves every direction and runs while any direction is held and no direction was pressed
    /// this step. A step holding two directions uses the first of up, down, left and right.
    /// <see cref="FocusActions.Confirm"/> and <see cref="FocusActions.Click"/> are read on their press
    /// edge only.
    /// <para>
    /// A pointer that moved this step and lies inside an item's bounds focuses that item. A mouse
    /// resting on the menu therefore does not fight a player on a gamepad. Items are hit-tested in list order and
    /// the first one containing the pointer wins. An item with empty bounds is never under the pointer.
    /// Both the pointer and the click only reach items on a <see cref="ScreenEntity"/>. A click presses
    /// the item under the pointer whether or not the pointer moved, and does nothing over empty space.
    /// A step that finds no live focus spends itself moving to one and reads nothing else, so the item
    /// it lands on cannot be pressed by an action aimed at the previously focused item. For the same
    /// reason, a handler that takes the landing item out of its scene drops the press.
    /// </para>
    /// </summary>
    protected internal override void OnStep(in StepContext context)
    {
        bool justTurnedInteractable = _justTurnedInteractable;
        _justTurnedInteractable = false;

        if (!Interactable || justTurnedInteractable)
        {
            _heldSteps = 0;

            return;
        }

        InputState input = context.Input;

        Side? pressedSide = Direction(input, pressed: true);
        Side? heldSide = Direction(input, pressed: false);
        _heldSteps = heldSide is not null && pressedSide is null ? _heldSteps + 1 : 0;

        if (Focused is not { } focused || !Live(focused))
        {
            Move(FirstLive());

            return;
        }

        Focusable target = focused;
        bool press = false;

        // One hit test per step. Pointer focusing requires the pointer to have moved, but a click does
        // not, so clicking without nudging the mouse still picks the item under it.
        bool clicking = _actions.Click is { } click && input.WasPressed(click);
        Focusable? under = input.PointerMoved || clicking ? Under(input.Pointer) : null;

        if (input.PointerMoved && under is not null)
        {
            target = under;
        }

        bool repeat = _repeatInterval > 0 && _heldSteps >= _repeatDelay
            && (_heldSteps - _repeatDelay) % _repeatInterval == 0;
        if ((pressedSide ?? (repeat ? heldSide : null)) is { } side && Reached(target, side) is { } moved)
        {
            target = moved;
        }

        if (clicking && under is not null)
        {
            target = under;
            press = true;
        }

        press |= input.WasPressed(_actions.Confirm);

        // Move first. A press handler then finds this navigator already focused on the pressed item.
        Move(target);

        if (press && Focused is { } pressed && Live(pressed))
        {
            pressed.Press();
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
            return Neighbour(from, Axis(side));
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

    private static Vector2 Axis(Side side) =>
        side switch
        {
            Side.Up => -Vector2.UnitY,
            Side.Down => Vector2.UnitY,
            Side.Left => -Vector2.UnitX,
            _ => Vector2.UnitX,
        };

    // Returns the live item `direction` reaches from `from`, or the wrap target when nothing lies that
    // way.
    private Focusable? Neighbour(Focusable from, Vector2 direction)
    {
        Vector2 origin = Centre(from);

        Focusable? best = null;
        float bestScore = 0f;
        Focusable? wrap = null;
        float wrapDistance = 0f;

        for (int i = 0; i < _items.Count; i++)
        {
            Focusable item = _items[i];

            if (ReferenceEquals(item, from) || !Live(item))
            {
                continue;
            }

            Vector2 delta = Centre(item) - origin;
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

    // A zero-size box centres on its position, which keeps an item with no extent in every direction.
    private static Vector2 Centre(Focusable item)
    {
        Rect bounds = item.Bounds;

        return bounds.Position + (bounds.Size / 2f);
    }

    // An entity queued to leave has already stopped stepping, so the focus stops reaching it in the
    // same step instead of one step later. An entity queued to join counts as live, which lets a menu
    // rebuilt during a step focus the row it just added when that step's queue drains.
    private static bool Live(Focusable item) =>
        item.Entity is { } entity &&
        (entity.SceneOrNull is { } scene ? scene.Contains(entity) : entity.PendingScene is not null) &&
        entity.ShownInTree;

    // Returns the first screen item containing the pointer, or null. World items are skipped, because
    // the pointer is in canvas pixels and their bounds are in world units.
    private Focusable? Under(Vector2 pointer)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            Focusable item = _items[i];

            if (Live(item) && item.Entity is ScreenEntity && item.Bounds.Contains(pointer))
            {
                return item;
            }
        }

        return null;
    }

    private Focusable? FirstLive()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (Live(_items[i]))
            {
                return _items[i];
            }
        }

        return null;
    }

    private Focusable? First() => _items.Count > 0 ? _items[0] : null;

    private void Append(Focusable item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (_items.Contains(item))
        {
            throw new ArgumentException($"{nameof(Focusable)} is already an item of this navigator; list it once.", nameof(item));
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
                    $"{nameof(Focusable)} names a {side} neighbour this navigator does not hold; add that item first.",
                    nameof(item));
            }
        }
    }

    // Applies the move before raising anything. A handler then reads the focus it landed on.
    private void Move(Focusable? landing)
    {
        if (ReferenceEquals(landing, Focused))
        {
            return;
        }

        Focusable? left = Focused;
        Focused = landing;

        if (_started)
        {
            Raise(left, landing);
        }
    }

    private void Raise(Focusable? left, Focusable? landing)
    {
        _raising = true;

        try
        {
            left?.LoseFocus();

            if (landing is { } item)
            {
                item.TakeFocus();
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
        panel.Field("Focused", Focused?.Entity?.GetType().Name);
    }
}
