using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Rendering;

namespace Capsule.Diagnostics;

/// <summary>
/// How a scene, entity or component offers itself to the development overlay from its
/// <c>OnDebugPanel</c> hook: a labelled value to read, a command to run, or a toggle to flip, one
/// row per call. The overlay shows each hook's rows as one section under the owner's heading,
/// its fields first in write order, then its commands and toggles in write order under a
/// <c>Commands</c> sub-heading; a section that wrote neither shows <c>&lt;Nothing to show&gt;</c>.
/// Write-only, as <see cref="DebugDraw"/> is: nothing written here
/// is read back by game code, so a run whose panels were never opened reaches the same state as
/// one whose were. A command or toggle runs while the overlay holds the simulation, inside the
/// one fixed step the overlay then runs through the ordinary input path — after the step has
/// begun and ahead of the scene's own, so its sounds and the scene transition it requests are
/// that step's and are consumed with it — after which every open page is rebuilt; one that
/// throws is not caught.
/// Every verb is compiled out of an assembly that does not define <c>CAPSULE_DEVELOPMENT</c> —
/// the call, its argument expressions and any lambda among them are absent, so a shipping build
/// spends nothing on them — and Capsule's build defines that symbol for a consuming game whenever
/// <c>CapsuleShipping</c> is not <c>true</c>.
/// <para>
/// Values are formatted in the invariant culture: a float or double at its shortest round-trip
/// form, a <see cref="Vector2"/> as <c>(x, y)</c>, a <see cref="ColorRgba"/> as <c>#rrggbbaa</c>, an
/// enum by its name, a null string as <c>null</c>. The overlay lays the labels out in one column and the values in the next, so a
/// label is a short noun and a value is one line.
/// </para>
/// </summary>
public sealed class DebugPanel
{
    private const string Null = "null";

    private readonly List<DebugPanelRow> _rows = [];

    /// <summary>A panel holding no rows.</summary>
    public DebugPanel()
    {
    }

    // The rows in the order they were written: a heading row for each component the walk reached
    // and a row for each call. Invalidated by the next write or Clear.
    internal ReadOnlySpan<DebugPanelRow> Rows => CollectionsMarshal.AsSpan(_rows);

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/>; null shows as <c>null</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is null.</exception>
    [Conditional(Development.Symbol)]
    public void Field(string label, string? value) => Write(label, value ?? Null);

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/>, as <c>True</c> or <c>False</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is null.</exception>
    [Conditional(Development.Symbol)]
    public void Field(string label, bool value) => Write(label, value ? bool.TrueString : bool.FalseString);

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/>; an <see cref="int"/> lands here.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is null.</exception>
    [Conditional(Development.Symbol)]
    public void Field(string label, long value) => Write(label, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/> at its shortest round-trip form.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is null.</exception>
    [Conditional(Development.Symbol)]
    public void Field(string label, float value) => Write(label, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/> at its shortest round-trip form.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is null.</exception>
    [Conditional(Development.Symbol)]
    public void Field(string label, double value) => Write(label, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/> as <c>(x, y)</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is null.</exception>
    [Conditional(Development.Symbol)]
    public void Field(string label, Vector2 value) => Write(label, Format(value));

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/> as <c>#rrggbbaa</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is null.</exception>
    [Conditional(Development.Symbol)]
    public void Field(string label, ColorRgba value) =>
        Write(label, string.Create(CultureInfo.InvariantCulture, $"#{value.R:x2}{value.G:x2}{value.B:x2}{value.A:x2}"));

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/> by its name.</summary>
    /// <typeparam name="TEnum">The enum type; a value outside its names shows as its number.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is null.</exception>
    [Conditional(Development.Symbol)]
    public void Field<TEnum>(string label, TEnum value)
        where TEnum : struct, Enum =>
        Write(label, value.ToString());

    /// <summary>
    /// Offers <paramref name="activate"/> under <paramref name="label"/>: the overlay runs it when
    /// the row is chosen, inside the one fixed step it then runs, and rebuilds every open page.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> or <paramref name="activate"/> is null.</exception>
    [Conditional(Development.Symbol)]
    public void Command(string label, Action activate)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(activate);

        _rows.Add(new DebugPanelRow(DebugPanelRowKind.Command, label, null, activate));
    }

    /// <summary>
    /// Offers a switch under <paramref name="label"/> showing <paramref name="value"/>: the overlay
    /// calls <paramref name="set"/> with the opposite when the row is chosen, inside the one fixed
    /// step it then runs, and rebuilds every open page, so the row shows what the hook then
    /// writes.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> or <paramref name="set"/> is null.</exception>
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

    // Every overload lands here rather than on the string overload: a [Conditional] call from
    // inside this assembly, which does not define the symbol, would itself be compiled out.
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
