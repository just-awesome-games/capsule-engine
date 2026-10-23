using System.ComponentModel;

namespace Capsule.Localization;

/// <summary>One text the game ships in every language, a <c>CapsuleStrings</c> member.</summary>
/// <remarks>
/// A keyed <c>Label</c> shows it in the run's active language. A scene document names it by its key path,
/// as <c>"hints.jump"</c>.
/// </remarks>
public readonly struct TextKey
{
    private readonly StringTable? _table;

    private readonly int _index;

    /// <summary>Names the key at <paramref name="index"/> in <paramref name="table"/>. Generated code calls it.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TextKey(StringTable table, int index)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, table.KeyCount);

        _table = table;
        _index = index;
    }

    internal StringTable Table => _table!;

    internal int Index => _index;

    // How many arguments the text takes.
    internal int ArgumentCount => _table!.ArgumentCount(_index);

    // Which arguments select a plural form, as bit i for argument i.
    internal int PluralMask => _table!.PluralMask(_index);

    /// <summary>The key path, as <c>options.windowScale</c>, or empty for the default key.</summary>
    public override string ToString() => _table is null ? string.Empty : _table.KeyPath(_index);

    // Refuses the default key, which names no text.
    internal TextKey Required(string parameter) =>
        _table is null
            ? throw new ArgumentException("The text key is default and names no text. Pass a CapsuleStrings member.", parameter)
            : this;
}
