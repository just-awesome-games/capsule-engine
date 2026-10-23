using System.ComponentModel;

namespace Capsule.Localization;

/// <summary>An ordered list of texts the game ships in every language, a <c>CapsuleStrings</c> member.</summary>
/// <remarks>
/// A string table authors it as a JSON array of texts, and each element is an ordinary text keyed by its
/// position, as <c>speech.0</c>. Every language holds the source language's count.
/// </remarks>
public readonly struct TextList
{
    private readonly StringTable? _table;

    private readonly int _first;

    /// <summary>How many texts the list holds, or 0 for the default list.</summary>
    public int Count { get; }

    /// <summary>Names the <paramref name="count"/> keys from <paramref name="first"/> in <paramref name="table"/>. Generated code calls it.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TextList(StringTable table, int first, int count)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentOutOfRangeException.ThrowIfNegative(first);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, table.KeyCount - first);

        _table = table;
        _first = first;
        Count = count;
    }

    /// <summary>The text at <paramref name="index"/>, in authored order.</summary>
    public TextKey this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

            return new TextKey(_table!, _first + index);
        }
    }
}
