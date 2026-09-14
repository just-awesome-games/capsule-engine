using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Capsule.Diagnostics;

/// <summary>
/// How an entity or component reports its state to the development overlay, one labelled value
/// at a time, from its <c>OnInspect</c> hook. Report-only, as <see cref="DebugDraw"/> is: nothing
/// written here is read back by game code, so a run that was inspected reaches the same state as
/// one that was not. Every <c>Field</c> overload is compiled out of an assembly that does not
/// define <c>CAPSULE_DEVELOPMENT</c> — the call and its argument expressions are absent, so a
/// shipping build spends nothing on them — and Capsule's build defines that symbol for a consuming
/// game whenever <c>CapsuleShipping</c> is not <c>true</c>.
/// <para>
/// Values are formatted in the invariant culture: a float or double at its shortest round-trip
/// form, a <see cref="Vector2"/> as <c>(x, y)</c>, an enum by its name, a null string as
/// <c>null</c>. The overlay lays the labels out in one column and the values in the next, so a
/// label is a short noun and a value is one line.
/// </para>
/// </summary>
public sealed class Inspector
{
    private const string Null = "null";

    private readonly List<InspectorRow> _rows = [];

    /// <summary>An inspector holding no rows.</summary>
    public Inspector()
    {
    }

    // The rows in the order they were written: a heading row for each component the walk reached
    // and a field row for each value. Invalidated by the next write or Clear.
    internal ReadOnlySpan<InspectorRow> Rows => CollectionsMarshal.AsSpan(_rows);

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

    /// <summary>Writes <paramref name="value"/> under <paramref name="label"/> by its name.</summary>
    /// <typeparam name="TEnum">The enum type; a value outside its names shows as its number.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is null.</exception>
    [Conditional(Development.Symbol)]
    public void Field<TEnum>(string label, TEnum value)
        where TEnum : struct, Enum =>
        Write(label, value.ToString());

    // Opens the section the rows that follow belong to, named for the component they describe.
    internal void Section(string heading)
    {
        ArgumentNullException.ThrowIfNull(heading);

        _rows.Add(new InspectorRow(heading, null));
    }

    internal void Clear() => _rows.Clear();

    // Every overload lands here rather than on the string overload: a [Conditional] call from
    // inside this assembly, which does not define the symbol, would itself be compiled out.
    private void Write(string label, string value)
    {
        ArgumentNullException.ThrowIfNull(label);

        _rows.Add(new InspectorRow(label, value));
    }

    // The one spelling of a position, shared with the overlay's own rows.
    internal static string Format(Vector2 value) =>
        string.Create(CultureInfo.InvariantCulture, $"({value.X}, {value.Y})");
}

// One row an inspector holds: a field with its value, or a heading — a null value — opening a
// component's section.
internal readonly record struct InspectorRow(string Label, string? Value)
{
    internal bool IsHeading => Value is null;
}
