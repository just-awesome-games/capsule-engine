using Capsule.Input;

namespace Capsule.Runtime.DevTools;

// One item of a menu — a label, exactly what activating it does, the overlay hotkey that also
// activates it and whether that hotkey repeats while held — and nothing of how it is drawn or
// focused, which is the scene's; an item with no action is drawn and never focused.
internal readonly record struct MenuItem(string Label, Action? Activate, InputAction? Hotkey = null, bool Repeats = false);
