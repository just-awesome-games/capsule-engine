using Capsule.Input;

namespace Capsule.Runtime.DevTools;

// One row of the overlay's current page, built afresh every overlay frame. A row with no action is
// drawn and never focused. An opener's hotkey fires only while the root page is current, and no page is
// stacked from inside a submenu. Any other hotkey fires at any depth, which lets the run be stepped
// while an entity panel is watched.
internal readonly record struct OverlayRow(
    string Label,
    Action? Activate,
    InputAction? Hotkey = null,
    bool Repeats = false,
    bool OpensMenu = false);
