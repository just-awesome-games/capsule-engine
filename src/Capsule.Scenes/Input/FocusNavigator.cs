using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Input;
using Capsule.Rendering;

namespace Capsule.Scenes.Input;

/// <summary>
/// Moves a focus between <see cref="Focusable"/> items and presses the one that has it: the state
/// machine behind a menu, a tab strip, a grid or a talent tree. It steps itself, so a scene attaches
/// one and reads nothing; the items carry what the focus looks like and what pressing one means.
/// <para>
/// The items need not be on this component's entity, and need not share one: a navigator is where a
/// set of items is declared to be one focus, wherever in the scene they are. It draws nothing, and a
/// step allocates nothing.
/// </para>
/// <para>
/// A direction ranks the items lying that way from the focused one's <see cref="Focusable.Bounds"/>
/// centre by <c>dot(direction, delta) / |delta|²</c>, highest first: alignment and closeness trade
/// against each other rather than one being settled before the other, so a near diagonal can win
/// over a far aligned item — pressing right with deltas of <c>(40, 0)</c> and <c>(10, 10)</c> scores
/// them 0.025 and 0.05 and moves to the diagonal. Ties go to list order, and where nothing lies that
/// way the focus wraps to the item farthest the other way.
/// </para>
/// <para>
/// An item is <em>live</em> while its <see cref="Component.Entity"/> is one a scene still holds, and
/// only live items take part: the directions, the pointer and the press all pass over an item whose
/// entity has left its scene or is queued to leave it this step. A focus found on one is released —
/// its <see cref="Focusable.Unfocused"/> raised — and the first live item in list order takes it, or
/// this navigator holds none until one is live again. An item that re-enters a scene is live again;
/// it never leaves <see cref="Items"/>.
/// </para>
/// </summary>
public sealed class FocusNavigator : Component
{
    private readonly List<Focusable> _items = [];
    private readonly FocusActions _actions;

    private bool _started;
    private bool _raising;
    private Focusable? _queued;

    /// <summary>
    /// Navigates <paramref name="items"/>, the first of which is the starting item.
    /// </summary>
    /// <param name="actions">The actions this navigator is driven by, for its whole life.</param>
    /// <param name="items">The items the focus moves between; each must be non-null and named once.</param>
    /// <exception cref="ArgumentNullException">Some item is null.</exception>
    public FocusNavigator(FocusActions actions, params ReadOnlySpan<Focusable> items)
    {
        _actions = actions;

        for (int i = 0; i < items.Length; i++)
        {
            Add(items[i]);
        }
    }

    /// <summary>
    /// Raised with the item the focus landed on, after that item's <see cref="Focusable.Focused"/>
    /// and before anything that step presses it. Raised for the focus the starting item takes at
    /// this navigator's start, and not where the focus does not move or where it is released to no
    /// item at all. Handlers run synchronously, in subscription order.
    /// </summary>
    public event Action<Focusable>? FocusChanged;

    /// <summary>
    /// Which item has the focus, or null while this navigator holds none. Before it starts, this is
    /// the item that will take the focus then.
    /// </summary>
    public Focusable? Focused { get; private set; }

    /// <summary>
    /// The items the focus moves between, in list order, which is the order the pointer hit-tests
    /// them in and the order ties in a direction are broken by. Invalidated by the next
    /// <see cref="Add"/>.
    /// </summary>
    public ReadOnlySpan<Focusable> Items => CollectionsMarshal.AsSpan(_items);

    /// <summary>
    /// Appends <paramref name="item"/> to the end of the list. The first one appended is the starting
    /// item: it takes the focus at this navigator's start, or at once where this navigator has
    /// already started.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public void Add(Focusable item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _items.Add(item);

        if (Focused is not null)
        {
            return;
        }

        if (_started)
        {
            Request(item);

            return;
        }

        Focused = item;
    }

    /// <summary>
    /// Moves the focus onto <paramref name="item"/>, raising exactly what an input move raises; a
    /// no-op, raising nothing, where that item already has it. Called before this navigator starts it
    /// names the starting item instead, raising nothing. This is how a game opens a menu on something
    /// other than its first item, or restores the focus it left.
    /// <para>
    /// A call arriving from inside this navigator's own focus events is queued, not applied: the
    /// sequence under way finishes, then the request runs as its own full sequence from the item that
    /// just landed, and only the last request one sequence queued is kept.
    /// </para>
    /// </summary>
    /// <param name="item">
    /// The item to focus, which this navigator must already hold. Naming one that is not live
    /// releases the focus instead, as a step finding it not live would; named before this navigator
    /// starts, it is the start that reads its liveness.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    /// <exception cref="ArgumentException">The item is not one of <see cref="Items"/>.</exception>
    public void Focus(Focusable item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!_items.Contains(item))
        {
            throw new ArgumentException($"{nameof(Focusable)} is not an item of this navigator.", nameof(item));
        }

        Request(item);
    }

    /// <summary>
    /// Hands the starting item the focus: its <see cref="Focusable.Focused"/> is raised, then
    /// <see cref="FocusChanged"/>. Its liveness is read here for the first time, so a starting item
    /// that is not live hands the focus to the first live item in list order, or leaves this
    /// navigator holding none. Nothing is raised before this.
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
            Announce(left: null, landing);
        }
    }

    /// <summary>
    /// Reads one step of the directions, the pointer and the presses, raising at most one focus move
    /// and at most one press.
    /// <para>
    /// Every action is read on its press edge alone, so a held direction moves the focus once and
    /// never repeats, and a step holding two directions reads the first of up, down, left and right.
    /// A direction resolves by the geometry this class documents: the live item lying that way with
    /// the highest <c>dot(direction, delta) / |delta|²</c>, or the wrap where none lies that way.
    /// </para>
    /// <para>
    /// A pointer that moved this step and lies inside an item's bounds focuses it; a pointer resting
    /// where it already was focuses nothing, so a mouse left lying on the menu never fights a player
    /// on a gamepad. Items are hit-tested in list order and the first the pointer is inside wins; one
    /// whose bounds are empty is never under it. A click pressed over no item does nothing at all,
    /// and unlike the pointer's own focusing it does not need the pointer to have moved. The pointer
    /// and the click reach items on a <see cref="ScreenEntity"/> only; a world-space item is reached
    /// by the directions and <see cref="FocusActions.Confirm"/> alone.
    /// </para>
    /// <para>
    /// A step that finds no live focus spends itself repairing one and reads nothing else, so the
    /// item it lands on is never pressed by an action aimed at the item that held the focus before.
    /// For the same reason a handler of this step's own focus move that takes the landing item out of
    /// its scene drops the press rather than redirecting it; the next step repairs the focus.
    /// </para>
    /// </summary>
    protected internal override void OnStep(in StepContext context)
    {
        if (Focused is not { } focused || !Live(focused))
        {
            Move(FirstLive());

            return;
        }

        InputState input = context.Input;
        Focusable target = focused;
        bool press = false;

        if (input.PointerMoved && Under(input.Pointer) is { } hovered)
        {
            target = hovered;
        }

        if (Stepped(input, target) is { } moved)
        {
            target = moved;
        }

        // The click's own hit test, which a resting pointer passes: a player who clicks without
        // nudging the mouse first still picks what is under it.
        if (_actions.Click is { } click && input.WasPressed(click) && Under(input.Pointer) is { } clicked)
        {
            target = clicked;
            press = true;
        }

        press |= input.WasPressed(_actions.Confirm);

        // The move first and the focus already settled: a handler told an item was pressed finds
        // this navigator naming that same item.
        Move(target);

        // The move's own handlers may have taken the landing item out of its scene, and a press is
        // aimed at one item: it is dropped rather than handed to a fallback.
        if (press && Focused is { } pressed && Live(pressed))
        {
            pressed.Press();
        }
    }

    // The direction this step asks for, resolved against `from`, or null where it asks for no move or
    // the direction it asks for reaches nothing.
    private Focusable? Stepped(InputState input, Focusable from)
    {
        if (input.WasPressed(_actions.Up))
        {
            return Neighbour(from, -Vector2.UnitY);
        }

        if (input.WasPressed(_actions.Down))
        {
            return Neighbour(from, Vector2.UnitY);
        }

        if (input.WasPressed(_actions.Left))
        {
            return Neighbour(from, -Vector2.UnitX);
        }

        return input.WasPressed(_actions.Right) ? Neighbour(from, Vector2.UnitX) : null;
    }

    // The item `direction` reaches from `from`: the live item lying that way whose
    // dot(direction, delta) / |delta|² is highest, so alignment and closeness trade against each
    // other rather than being ranked one after the other; failing that the wrap, which is the live
    // item farthest the other way. Null where no other item is live.
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

    // A zero-size box has its centre at its position, so an item with no extent still takes part in
    // every direction.
    private static Vector2 Centre(Focusable item)
    {
        Rect bounds = item.Bounds;

        return bounds.Position + (bounds.Size / 2f);
    }

    // Whether a scene still holds the item's entity. An entity queued to leave this step has already
    // stopped stepping, so the focus must stop reaching it then rather than a step later.
    private static bool Live(Focusable item) =>
        item.Entity is { Scene: { } scene } entity && scene.Keeps(entity);

    // The first screen item the pointer is inside, or null where it is inside none of them. A world
    // item is skipped rather than tested: the pointer is canvas pixels and its bounds are world
    // units, so the comparison would be arithmetic between two unrelated spaces.
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

    // A move asked for from outside the step: queued while this navigator's events are being raised,
    // and otherwise applied at once, with an item that is not live standing for the release of the
    // focus. Before the start there is no scene to be live in, so the item is recorded as asked for
    // and the start resolves it.
    private void Request(Focusable item)
    {
        if (_raising)
        {
            _queued = ReferenceEquals(item, Focused) ? null : item;

            return;
        }

        Move(_started ? (Live(item) ? item : FirstLive()) : item);
    }

    // One move, raised in the order the game writes its handlers against. Before this navigator has
    // started it only names the item that will take the focus then.
    private void Move(Focusable? landing)
    {
        if (ReferenceEquals(landing, Focused))
        {
            return;
        }

        Focusable? left = Focused;
        Focused = landing;

        if (!_started)
        {
            return;
        }

        Announce(left, landing);
    }

    // The ordered sequence, and then every move a handler inside it asked for, each run whole from
    // the item the one before it landed on. The flag is what keeps a nested request out of the
    // middle of a sequence, where it would leave two items claiming the focus.
    private void Announce(Focusable? left, Focusable? landing)
    {
        while (true)
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

            if (_queued is not { } next)
            {
                return;
            }

            _queued = null;
            left = landing;
            landing = Live(next) ? next : FirstLive();

            if (ReferenceEquals(landing, left))
            {
                return;
            }

            Focused = landing;
        }
    }
}
