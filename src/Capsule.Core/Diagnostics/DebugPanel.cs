using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Rendering;

namespace Capsule.Diagnostics;

/// <summary>
/// How a scene, entity or component offers itself to the development overlay from its
/// <c>OnDebugPanel</c> hook. The engine builds the panel and hands it to the hook.
/// </summary>
/// <remarks>
/// Each call writes one row, shown in write order: a labelled value to read, a command to run, or a
/// toggle to flip. It is write-only, like <see cref="DebugDraw"/>, and every verb is compiled out of
/// an assembly that does not define <c>CAPSULE_DEVELOPMENT</c>. Values are formatted in the invariant
/// culture.
/// <para>
/// A command or toggle runs while the overlay holds the simulation, inside the fixed step the overlay
/// then runs through the ordinary input path. It runs after that step has begun and before the
/// scene's own logic. Its sounds and any scene transition it requests belong to that step.
/// </para>
/// </remarks>
public sealed class DebugPanel
{
    private const string Null = "null";

    private readonly List<DebugPanelRow> _rows = [];

    internal DebugPanel()
    {
    }

    // The rows in the order they were written, with a heading row for each component the walk reached and
    // a row for each call. Invalidated by the next write or Clear.
    internal ReadOnlySpan<DebugPanelRow> Rows => CollectionsMarshal.AsSpan(_rows);

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/>. Null shows as <c>null</c>.</summary>
    [Conditional(Development.Symbol)]
    public void Field(string label, string? value) => Write(label, value ?? Null);

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/>, as <c>True</c> or <c>False</c>.</summary>
    [Conditional(Development.Symbol)]
    public void Field(string label, bool value) => Write(label, value ? bool.TrueString : bool.FalseString);

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/>. An <see cref="int"/> binds to this overload.</summary>
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

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/> as <see cref="Transform2D.ToString"/> spells it.</summary>
    [Conditional(Development.Symbol)]
    public void Field(string label, Transform2D value) => Write(label, value.ToString());

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/> as <c>#rrggbbaa</c>.</summary>
    [Conditional(Development.Symbol)]
    public void Field(string label, ColorRgba value) =>
        Write(label, string.Create(CultureInfo.InvariantCulture, $"#{value.R:x2}{value.G:x2}{value.B:x2}{value.A:x2}"));

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/> by its name.</summary>
    /// <typeparam name="TEnum">The enum type. A value outside its names shows as its number.</typeparam>
    [Conditional(Development.Symbol)]
    public void Field<TEnum>(string label, TEnum value)
        where TEnum : struct, Enum =>
        Write(label, value.ToString());

    /// <summary>
    /// Offers <paramref name="activate"/> under <paramref name="label"/>, run when the row is chosen.
    /// Every open page is rebuilt afterwards.
    /// </summary>
    [Conditional(Development.Symbol)]
    public void Command(string label, Action activate)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(activate);

        _rows.Add(new DebugPanelRow(DebugPanelRowKind.Command, label, null, activate));
    }

    /// <summary>
    /// Offers a switch under <paramref name="label"/> showing <paramref name="value"/>. Choosing the
    /// row calls <paramref name="set"/> with the opposite value, and the row then shows whatever the
    /// rebuilt hook writes.
    /// </summary>
    [Conditional(Development.Symbol)]
    public void Toggle(string label, bool value, Action<bool> set)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(set);

        _rows.Add(new DebugPanelRow(DebugPanelRowKind.Toggle, label, null, () => set(!value), value));
    }

    // Opens the section the following rows belong to, named for the component they describe.
    internal void Section(string heading)
    {
        ArgumentNullException.ThrowIfNull(heading);

        _rows.Add(new DebugPanelRow(DebugPanelRowKind.Heading, heading, null, null));
    }

    internal void Clear() => _rows.Clear();

    // Every overload calls this instead of the public string overload, because [Conditional] removes the
    // call site and the row would be lost wherever this assembly is built for shipping.
    private void Write(string label, string value)
    {
        ArgumentNullException.ThrowIfNull(label);

        _rows.Add(new DebugPanelRow(DebugPanelRowKind.Field, label, value, null));
    }

    // The shared spelling of a position, used by the overlay's own rows too.
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

// One row a panel holds. Value is set for a field, Activate for a command or toggle, and a toggle's
// Activate already carries the flip. On is the toggle's shown state.
internal readonly record struct DebugPanelRow(DebugPanelRowKind Kind, string Label, string? Value, Action? Activate, bool On = false)
{
    internal bool IsHeading => Kind == DebugPanelRowKind.Heading;
}
