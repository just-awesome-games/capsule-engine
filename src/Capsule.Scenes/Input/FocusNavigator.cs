using System.Numerics;
using Capsule.Input;
using Capsule.Scenes.Rendering;

namespace Capsule.Scenes.Input;

/// <summary>
/// Moves a focus along an ordered list of <see cref="Renderer"/> items and says when one was
/// activated: the state machine behind a menu, a tab strip or a dialogue choice. One dimension, so the
/// list is a row or a column by how the game laid it out.
/// <para>
/// It draws nothing and owns nothing drawn. Showing which item has the focus is the game's — a colour
/// on the item, or a highlight it moves to the focused item's <see cref="Renderer.Bounds"/> — and so is
/// what activating one means. The game names its own actions, and one <see cref="Step(InputState, InputAction, InputAction, InputAction)"/>
/// per fixed step is the whole of the input it reads; a step allocates nothing.
/// </para>
/// </summary>
public sealed class FocusNavigator
{
    private readonly List<Renderer> _items = [];

    /// <summary>
    /// Navigates <paramref name="items"/>, in the order given, with the first of them focused. A
    /// navigator built over none focuses nothing until one is added.
    /// </summary>
    /// <param name="items">The items focus moves along; each must be non-null and named once.</param>
    /// <exception cref="ArgumentNullException">Some item is null.</exception>
    public FocusNavigator(params ReadOnlySpan<Renderer> items)
    {
        for (int i = 0; i < items.Length; i++)
        {
            Add(items[i]);
        }
    }

    /// <summary>How many items focus moves along.</summary>
    public int Count => _items.Count;

    /// <summary>
    /// Which item has the focus, as its position in the list, or -1 while there are no items. The
    /// first item added takes the focus, and a directional press moves by one from there.
    /// </summary>
    public int FocusedIndex { get; private set; } = -1;

    /// <summary>The item <see cref="FocusedIndex"/> names, or null while there are no items.</summary>
    public Renderer? Focused => FocusedIndex < 0 ? null : _items[FocusedIndex];

    /// <summary>
    /// Whether the most recent step moved the focus to a different item. False before the first step,
    /// and false on a step whose press wrapped onto the item already focused — a one-item list never
    /// reports a move.
    /// </summary>
    public bool FocusChanged { get; private set; }

    /// <summary>
    /// Whether the most recent step activated an item, which is the one <see cref="Focused"/> then
    /// names. At most once per step however many of the step's actions asked for it.
    /// </summary>
    public bool Activated { get; private set; }

    /// <summary>
    /// Appends <paramref name="item"/> to the end of the list. The first one appended takes the focus,
    /// without that counting as a move; a later one leaves the focus where it is.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public void Add(Renderer item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _items.Add(item);

        if (FocusedIndex < 0)
        {
            FocusedIndex = 0;
        }
    }

    /// <summary>
    /// Advances the focus by one step of <paramref name="input"/>, then reports through
    /// <see cref="FocusedIndex"/>, <see cref="Focused"/>, <see cref="FocusChanged"/> and
    /// <see cref="Activated"/>. Every action is read on its press edge alone, so a held direction
    /// moves the focus once and never repeats.
    /// <para>
    /// A pointer that moved this step and lies inside an item's <see cref="Renderer.Bounds"/> focuses
    /// that item; a pointer resting where it already was focuses nothing, so a mouse left lying on the
    /// menu never fights a player on a gamepad. A directional press then moves one item from wherever
    /// the focus stands, wrapping at either end, and a step holding both directions reads as
    /// <paramref name="backward"/>. Items are hit-tested in list order and the first the pointer is
    /// inside wins; one whose bounds are empty is never under it.
    /// </para>
    /// </summary>
    /// <param name="input">The run's input state, read for this step's edges and pointer.</param>
    /// <param name="backward">Moves the focus one item towards the start of the list.</param>
    /// <param name="forward">Moves the focus one item towards the end of it.</param>
    /// <param name="confirm">Activates the focused item wherever the pointer is.</param>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    public void Step(InputState input, InputAction backward, InputAction forward, InputAction confirm) =>
        Advance(input, backward, forward, confirm, click: null);

    /// <summary>
    /// Advances the focus as <see cref="Step(InputState, InputAction, InputAction, InputAction)"/>
    /// does, and additionally lets <paramref name="click"/> pick an item with the pointer: pressed
    /// while the pointer is inside an item, it focuses and activates that item in the same step, and
    /// pressed over no item it does nothing at all. Unlike the pointer's own focusing, it does not
    /// need the pointer to have moved.
    /// </summary>
    /// <param name="input">The run's input state, read for this step's edges and pointer.</param>
    /// <param name="backward">Moves the focus one item towards the start of the list.</param>
    /// <param name="forward">Moves the focus one item towards the end of it.</param>
    /// <param name="confirm">Activates the focused item wherever the pointer is.</param>
    /// <param name="click">Activates the item under the pointer, and only that item.</param>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    public void Step(InputState input, InputAction backward, InputAction forward, InputAction confirm, InputAction click) =>
        Advance(input, backward, forward, confirm, click);

    private void Advance(InputState input, InputAction backward, InputAction forward, InputAction confirm, InputAction? click)
    {
        ArgumentNullException.ThrowIfNull(input);

        int focused = FocusedIndex;

        FocusChanged = false;
        Activated = false;

        if (_items.Count == 0)
        {
            return;
        }

        Vector2 pointer = input.Pointer;

        if (input.PointerMoved)
        {
            int hovered = IndexUnder(pointer);

            if (hovered >= 0)
            {
                FocusedIndex = hovered;
            }
        }

        if (input.WasPressed(backward))
        {
            FocusedIndex = (FocusedIndex + _items.Count - 1) % _items.Count;
        }
        else if (input.WasPressed(forward))
        {
            FocusedIndex = (FocusedIndex + 1) % _items.Count;
        }

        // The click's own hit test, which a resting pointer passes: a player who clicks without
        // nudging the mouse first still picks what is under it.
        if (click is { } picked && input.WasPressed(picked))
        {
            int clicked = IndexUnder(pointer);

            if (clicked >= 0)
            {
                FocusedIndex = clicked;
                Activated = true;
            }
        }

        if (input.WasPressed(confirm))
        {
            Activated = true;
        }

        FocusChanged = FocusedIndex != focused;
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
