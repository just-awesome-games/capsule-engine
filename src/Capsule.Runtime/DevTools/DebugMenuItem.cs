using Capsule.Input;

namespace Capsule.Runtime.DevTools;

// One row of a debug menu and exactly what activating it does. A hotkey is one of the overlay's
// actions that also activates the item; a repeating item activates again while its hotkey stays
// held. An item with no action is drawn and never focused: a blank line or a note between rows.
internal readonly record struct DebugMenuItem(string Label, Action? Activate, InputAction? Hotkey = null, bool Repeats = false);
