using Capsule.Input;

namespace Capsule.Runtime.DevTools;

// A row with no action is never focused. An opener's hotkey fires only at the root.
internal readonly record struct OverlayRow(
    string Label,
    Action? Activate,
    string? Value = null,
    InputAction? Hotkey = null,
    bool Repeats = false,
    bool OpensPage = false)
{
    // (x) for one choice among several, [x] for a toggle.
    internal static string Marked(bool on, string label, bool choice = false) =>
        (choice ? (on ? "(x) " : "( ) ") : (on ? "[x] " : "[ ] ")) + label;

    // Ordinal tiebreak: "Room" and "room" differ only by case, and List<T>.Sort is unstable.
    internal static int CompareLabels(string a, string b)
    {
        int result = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

        return result != 0 ? result : string.CompareOrdinal(a, b);
    }
}
