using Capsule.Input;

namespace Capsule.Runtime.DevTools;

// One row of the overlay's current page. A row with no action is drawn and never focused. A root row's
// hotkey fires at any depth, except an opener's, which fires only at the root.
internal readonly record struct OverlayRow(
    string Label,
    Action? Activate,
    InputAction? Hotkey = null,
    bool Repeats = false,
    bool OpensMenu = false);
