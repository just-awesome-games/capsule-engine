using Capsule.Input;

namespace Capsule.Runtime.DevTools;

// One item of a menu — a label, exactly what activating it does, the overlay hotkey that also
// activates it, whether that hotkey repeats while held and whether activating it opens a menu —
// and nothing of how it is drawn or focused, which is the scene's; an item with no action is
// drawn and never focused. An opener's hotkey fires only while the root menu is current, so a
// submenu is never stacked from inside another; any other hotkey fires at any depth, so the run
// can be stepped while an entity page is watched.
internal readonly record struct MenuItem(
    string Label,
    Action? Activate,
    InputAction? Hotkey = null,
    bool Repeats = false,
    bool OpensMenu = false);
