using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Input;
using Capsule.Scenes.Rendering;

namespace Capsule.Scenes.Input;

/// <summary>
/// Moves a focus along an ordered list of <typeparamref name="T"/> items and says when one was
/// activated: the state machine behind a menu, a tab strip or a dialogue choice. One dimension, so the
/// list is a row or a column by how the game laid it out.
/// <para>
/// It draws nothing and owns nothing drawn. Showing which item has the focus is the game's — a colour
/// on the item, or a highlight it moves to the focused item's <see cref="Renderer.Bounds"/> — and so is
/// what activating one means. One <see cref="Step"/> per fixed step is the whole of the input it reads,
/// and a step allocates nothing.
/// </para>
/// </summary>
/// <typeparam name="T">
/// What the items are, so <see cref="Focused"/> and the events hand the game back the type it put in:
/// the <see cref="Label"/> of a text menu, or an entity's own renderer.
/// </typeparam>
public sealed class FocusNavigator<T>
    where T : Renderer
{
    private readonly List<T> _items = [];
    private readonly FocusActions _actions;

    /// <summary>
    /// Navigates <paramref name="items"/>, in the order given, with the first of them focused. A
    /// navigator built over none focuses nothing until one is added.
    /// </summary>
    /// <param name="actions">The actions this navigator is driven by, for its whole life.</param>
    /// <param name="items">The items focus moves along; each must be non-null and named once.</param>
    /// <exception cref="ArgumentNullException">Some item is null.</exception>
    public FocusNavigator(FocusActions actions, params ReadOnlySpan<T> items)
    {
        _actions = actions;

        for (int i = 0; i < items.Length; i++)
        {
            Add(items[i]);
        }
    }

    /// <summary>
    /// Raised with the item the focus landed on, from inside the <see cref="Step"/> that moved it and
    /// before any <see cref="Activated"/> of the same step. Not raised for the focus the first item
    /// added takes, nor on a step whose press wrapped onto the item already focused — a one-item list
    /// never reports a move. Handlers are bound by the same rule as
    /// <see cref="Physics.Collider2D.ContactEntered"/>: they run synchronously, in subscription order,
    /// and what they change is changed by the time the step's next stage runs.
    /// </summary>
    public event Action<T>? FocusChanged;

    /// <summary>
    /// Raised with the item activated, which is the one <see cref="Focused"/> then names, from inside
    /// the <see cref="Step"/> that activated it. At most once per step however many of the step's
    /// actions asked for it, and after that step's <see cref="FocusChanged"/>.
    /// </summary>
    public event Action<T>? Activated;

    /// <summary>How many items focus moves along.</summary>
    public int Count => _items.Count;

    /// <summary>
    /// Which item has the focus, as its position in the list, or -1 while there are no items. The
    /// first item added takes the focus, and a directional press moves by one from there.
    /// </summary>
    public int FocusedIndex { get; private set; } = -1;

    /// <summary>The item <see cref="FocusedIndex"/> names, or null while there are no items.</summary>
    public T? Focused => FocusedIndex < 0 ? null : _items[FocusedIndex];

    /// <summary>
    /// The items focus moves along, in list order, which is the order it walks them and the order the
    /// pointer hit-tests them in. Invalidated by the next <see cref="Add"/>.
    /// </summary>
    public ReadOnlySpan<T> Items => CollectionsMarshal.AsSpan(_items);

    /// <summary>
    /// Appends <paramref name="item"/> to the end of the list. The first one appended takes the focus,
    /// without raising <see cref="FocusChanged"/>; a later one leaves the focus where it is.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public void Add(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _items.Add(item);

        if (FocusedIndex < 0)
        {
            FocusedIndex = 0;
        }
    }

    /// <summary>
    /// Advances the focus by one step of <paramref name="input"/>, raising
    /// <see cref="FocusChanged"/> and then <see cref="Activated"/> for whatever this step did. Every
    /// action is read on its press edge alone, so a held direction moves the focus once and never
    /// repeats.
    /// <para>
    /// A pointer that moved this step and lies inside an item's <see cref="Renderer.Bounds"/> focuses
    /// that item; a pointer resting where it already was focuses nothing, so a mouse left lying on the
    /// menu never fights a player on a gamepad. A directional press then moves one item from wherever
    /// the focus stands, wrapping at either end, and a step holding both directions reads as backward.
    /// Items are hit-tested in list order and the first the pointer is inside wins; one whose bounds
    /// are empty is never under it. A click pressed over no item does nothing at all, and unlike the
    /// pointer's own focusing it does not need the pointer to have moved.
    /// </para>
    /// </summary>
    /// <param name="input">The run's input state, read for this step's edges and pointer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    public void Step(InputState input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (_items.Count == 0)
        {
            return;
        }

        int focused = FocusedIndex;
        bool activated = false;

        Vector2 pointer = input.Pointer;

        if (input.PointerMoved)
        {
            int hovered = IndexUnder(pointer);

            if (hovered >= 0)
            {
                FocusedIndex = hovered;
            }
        }

        if (input.WasPressed(_actions.Backward))
        {
            FocusedIndex = (FocusedIndex + _items.Count - 1) % _items.Count;
        }
        else if (input.WasPressed(_actions.Forward))
        {
            FocusedIndex = (FocusedIndex + 1) % _items.Count;
        }

        // The click's own hit test, which a resting pointer passes: a player who clicks without
        // nudging the mouse first still picks what is under it.
        if (_actions.Click is { } picked && input.WasPressed(picked))
        {
            int clicked = IndexUnder(pointer);

            if (clicked >= 0)
            {
                FocusedIndex = clicked;
                activated = true;
            }
        }

        activated |= input.WasPressed(_actions.Confirm);

        // Both raised with the focus already settled, and the move first: a handler told what was
        // activated finds the navigator naming that same item.
        if (FocusedIndex != focused)
        {
            FocusChanged?.Invoke(_items[FocusedIndex]);
        }

        if (activated)
        {
            Activated?.Invoke(_items[FocusedIndex]);
        }
    }

    // The first item the pointer is inside, or -1 where it is inside none of them.
    private int IndexUnder(Vector2 pointer)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Bounds.Contains(pointer))
            {
                return i;
            }
        }

        return -1;
    }
}
