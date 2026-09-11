using Capsule.Input;

namespace Capsule.Scenes.Input;

/// <summary>
/// The actions a <see cref="FocusNavigator{T}"/> reads, declared once beside the game's own actions
/// and handed to every navigator it drives.
/// </summary>
/// <param name="Backward">Moves the focus one item towards the start of the list.</param>
/// <param name="Forward">Moves the focus one item towards the end of it.</param>
/// <param name="Confirm">Activates the focused item wherever the pointer is.</param>
/// <param name="Click">
/// Activates the item under the pointer, and only that item. Null — the default — leaves the
/// navigator deaf to the pointer's buttons however the game bound them.
/// </param>
public readonly record struct FocusActions(
    InputAction Backward,
    InputAction Forward,
    InputAction Confirm,
    InputAction? Click = null);
