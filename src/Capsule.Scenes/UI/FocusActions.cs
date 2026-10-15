using System.Collections.Immutable;
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
/// <param name="Confirm">
/// Raises <see cref="Focusable.Pressed"/>. A mouse button bound to it presses the item under the pointer and
/// drags it while held. Any other button presses the focused item.
/// </param>
/// <param name="Cancel">
/// Raises <see cref="FocusNavigator.Canceled"/>, as backing out of a menu does. Null, the default, raises
/// nothing.
/// </param>
/// <param name="Secondary">
/// Other actions that press an item, such as a reset, each raising <see cref="Focusable.SecondaryPressed"/>.
/// They land as <paramref name="Confirm"/> does and drag nothing.
/// </param>
public readonly record struct FocusActions(
    InputAction Up,
    InputAction Down,
    InputAction Left,
    InputAction Right,
    InputAction Confirm,
    InputAction? Cancel = null,
    ImmutableArray<InputAction> Secondary = default);
