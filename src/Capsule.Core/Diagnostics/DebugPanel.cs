using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Rendering;

namespace Capsule.Diagnostics;

/// <summary>
/// How a scene, entity or component offers itself to the development overlay from its
/// <c>OnDebugPanel</c> hook: a labelled value to read, a command to run, or a toggle to flip, one
/// row per call and shown in write order. Write-only, as <see cref="DebugDraw"/> is: nothing
/// written here is read back by game code, so a run whose panels were never opened reaches the same
/// state as one whose were. A command or toggle runs while the overlay holds the simulation, inside
/// the one fixed step the overlay then runs through the ordinary input path — after the step has
/// begun and ahead of the scene's own, so its sounds and the scene transition it requests are that
/// step's and are consumed with it — and one that throws is not caught. Every verb is compiled out
/// of an assembly that does not define <c>CAPSULE_DEVELOPMENT</c> — the call, its argument
/// expressions and any lambda among them are absent — and Capsule's build defines that symbol for
/// a consuming game whenever <c>CapsuleShipping</c> is not <c>true</c>.
/// <para>
/// Values are formatted in the invariant culture: a float or double at its shortest round-trip
/// form, a <see cref="Vector2"/> as <c>(x, y)</c>, a <see cref="ColorRgba"/> as <c>#rrggbbaa</c>, an
/// enum by its name, a null string as <c>null</c>. A label is a short noun and a value is one line.
/// Every verb throws <see cref="ArgumentNullException"/> for a null label, command or setter.
/// </para>
/// </summary>
public sealed class DebugPanel
{
    private const string Null = "null";

    private readonly List<DebugPanelRow> _rows = [];

    /// <summary>
    /// An empty panel, which a game constructs to call its own <c>OnDebugPanel</c> in a test; the
    /// overlay builds the one a running hook is handed.
    /// </summary>
    public DebugPanel()
    {
    }

    // The rows in the order they were written: a heading row for each component the walk reached
    // and a row for each call. Invalidated by the next write or Clear.
    internal ReadOnlySpan<DebugPanelRow> Rows => CollectionsMarshal.AsSpan(_rows);

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/>; null shows as <c>null</c>.</summary>
    [Conditional(Development.Symbol)]
    public void Field(string label, string? value) => Write(label, value ?? Null);

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/>, as <c>True</c> or <c>False</c>.</summary>
    [Conditional(Development.Symbol)]
    public void Field(string label, bool value) => Write(label, value ? bool.TrueString : bool.FalseString);

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/>; an <see cref="int"/> lands here.</summary>
    [Conditional(Development.Symbol)]
    public void Field(string label, long value) => Write(label, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/> at its shortest round-trip form.</summary>
    [Conditional(Development.Symbol)]
    public void Field(string label, float value) => Write(label, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/> at its shortest round-trip form.</summary>
    [Conditional(Development.Symbol)]
    public void Field(string label, double value) => Write(label, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/> as <c>(x, y)</c>.</summary>
    [Conditional(Development.Symbol)]
    public void Field(string label, Vector2 value) => Write(label, Format(value));

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/> as <c>#rrggbbaa</c>.</summary>
    [Conditional(Development.Symbol)]
    public void Field(string label, ColorRgba value) =>
        Write(label, string.Create(CultureInfo.InvariantCulture, $"#{value.R:x2}{value.G:x2}{value.B:x2}{value.A:x2}"));

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/> by its name.</summary>
    /// <typeparam name="TEnum">The enum type; a value outside its names shows as its number.</typeparam>
    [Conditional(Development.Symbol)]
    public void Field<TEnum>(string label, TEnum value)
        where TEnum : struct, Enum =>
        Write(label, value.ToString());

    /// <summary>
    /// Offers <paramref name="activate"/> under <paramref name="label"/>, run when the row is
    /// chosen; every open page is rebuilt afterwards.
    /// </summary>
    [Conditional(Development.Symbol)]
    public void Command(string label, Action activate)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(activate);

        _rows.Add(new DebugPanelRow(DebugPanelRowKind.Command, label, null, activate));
    }

    /// <summary>
    /// Offers a switch under <paramref name="label"/> showing <paramref name="value"/>:
    /// <paramref name="set"/> is called with the opposite when the row is chosen, and the row then
    /// shows whatever the rebuilt hook writes.
    /// </summary>
    [Conditional(Development.Symbol)]
    public void Toggle(string label, bool value, Action<bool> set)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(set);

        _rows.Add(new DebugPanelRow(DebugPanelRowKind.Toggle, label, null, () => set(!value), value));
    }

    // Opens the section the rows that follow belong to, named for the component they describe.
    internal void Section(string heading)
    {
        ArgumentNullException.ThrowIfNull(heading);

        _rows.Add(new DebugPanelRow(DebugPanelRowKind.Heading, heading, null, null));
    }

    internal void Clear() => _rows.Clear();

    // Every overload lands here rather than on the string overload: [Conditional] removes the call
    // site, so the row would be lost wherever this assembly is itself built for shipping.
    private void Write(string label, string value)
    {
        ArgumentNullException.ThrowIfNull(label);

        _rows.Add(new DebugPanelRow(DebugPanelRowKind.Field, label, value, null));
    }

    // The one spelling of a position, shared with the overlay's own rows.
    internal static string Format(Vector2 value) =>
        string.Create(CultureInfo.InvariantCulture, $"({value.X}, {value.Y})");
}

// What a panel row is: a section heading, a value to read, or a row that runs something.
internal enum DebugPanelRowKind
{
    Heading,
    Field,
    Command,
    Toggle,
}

// One row a panel holds. Value is set for a field only; Activate for a command or toggle, the
// toggle's already carrying the flip; On is the toggle's shown state.
internal readonly record struct DebugPanelRow(DebugPanelRowKind Kind, string Label, string? Value, Action? Activate, bool On = false)
{
    internal bool IsHeading => Kind == DebugPanelRowKind.Heading;
}
