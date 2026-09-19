using Capsule.Input;

namespace Capsule.UI;

/// <summary>
/// The actions a <see cref="FocusNavigator"/> reads. Declare them once beside the game's own actions and hand
/// them to every navigator the game drives.
/// </summary>
/// <param name="Up">
/// Moves the focus to the item the focused one names above it, or, when it names none, to the item above that
/// best trades alignment against closeness. See <see cref="FocusNavigator"/> for the named neighbour, the
/// score and the wrap.
/// </param>
/// <param name="Down">Moves the focus to the item below, chosen the same way.</param>
/// <param name="Left">Moves the focus to the item on the left, chosen the same way.</param>
/// <param name="Right">Moves the focus to the item on the right, chosen the same way.</param>
/// <param name="Confirm">Presses the focused item wherever the pointer is.</param>
/// <param name="Click">
/// Presses the item under the pointer, and only that item. Null, the default, makes the navigator ignore the
/// pointer's buttons however the game bound them.
/// </param>
public readonly record struct FocusActions(
    InputAction Up,
    InputAction Down,
    InputAction Left,
    InputAction Right,
    InputAction Confirm,
    InputAction? Click = null);
