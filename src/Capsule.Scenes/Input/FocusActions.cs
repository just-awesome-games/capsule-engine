using Capsule.Input;

namespace Capsule.Scenes.Input;

/// <summary>
/// The actions a <see cref="FocusNavigator"/> reads, declared once beside the game's own actions
/// and handed to every navigator it drives.
/// </summary>
/// <param name="Up">
/// Moves the focus to the item the focused one names above it, or, naming none, to the item above it
/// that is the best trade of alignment against closeness; see <see cref="FocusNavigator"/> for the
/// named neighbour, the score and the wrap.
/// </param>
/// <param name="Down">Moves it to the item below chosen the same way.</param>
/// <param name="Left">Moves it to the item to the left chosen the same way.</param>
/// <param name="Right">Moves it to the item to the right chosen the same way.</param>
/// <param name="Confirm">Presses the focused item wherever the pointer is.</param>
/// <param name="Click">
/// Presses the item under the pointer, and only that item. Null — the default — leaves the
/// navigator deaf to the pointer's buttons however the game bound them.
/// </param>
public readonly record struct FocusActions(
    InputAction Up,
    InputAction Down,
    InputAction Left,
    InputAction Right,
    InputAction Confirm,
    InputAction? Click = null);
