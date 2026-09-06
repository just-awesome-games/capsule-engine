using System.Collections;
using System.Text;

namespace Capsule.Input;

/// <summary>
/// An immutable recording of device input: exactly one <see cref="DeviceSnapshot"/> per fixed step,
/// in step order. A tape and the run's initial state and step length are what a deterministic run
/// is reproduced from, so a tape is a run's input in full — there is no per-frame residue beside it.
/// </summary>
/// <remarks>
/// Two tapes are equal when they hold the same snapshots in the same order. Build one with
/// <see cref="InputScript"/>, or read one back with <see cref="Parse"/>.
/// </remarks>
public sealed class InputTape : IReadOnlyList<DeviceSnapshot>, IEquatable<InputTape>
{
    private static readonly InputTape EmptyTape = new([]);

    private readonly DeviceSnapshot[] _snapshots;

    // 0 means "not computed yet"; a content hash that lands on 0 is stored as 1. The tape is
    // immutable, so a torn read is impossible and a repeated computation is harmless.
    private int _hash;

    private InputTape(DeviceSnapshot[] snapshots) => _snapshots = snapshots;

    /// <summary>A tape of no steps.</summary>
    public static InputTape Empty => EmptyTape;

    /// <summary>The number of fixed steps this tape drives.</summary>
    public int Count => _snapshots.Length;

    /// <summary>A tape holding <paramref name="snapshots"/>, one per step, copied on the way in.</summary>
    public static InputTape Of(params ReadOnlySpan<DeviceSnapshot> snapshots) =>
        snapshots.IsEmpty ? EmptyTape : new InputTape(snapshots.ToArray());

    /// <summary>The snapshot the step at <paramref name="index"/> is driven by.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the tape.</exception>
    public DeviceSnapshot this[int index] => _snapshots[index];

    /// <summary>
    /// Reads the line-oriented tape text <see cref="ToText"/> writes. Blank lines and lines whose
    /// first non-blank character is <c>#</c> are ignored.
    /// </summary>
    /// <param name="text">Tape text; empty text is <see cref="Empty"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="FormatException">A line is malformed; the message names its 1-based number.</exception>
    public static InputTape Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<DeviceSnapshot> steps = [];
        int line = 0;

        foreach (ReadOnlySpan<char> raw in text.AsSpan().EnumerateLines())
        {
            line++;
            ReadOnlySpan<char> content = raw.Trim();
            if (content.IsEmpty || content[0] == InputTapeText.Comment)
            {
                continue;
            }

            InputTapeText.ParseLine(content, line, steps);
        }

        return steps.Count == 0 ? EmptyTape : new InputTape([.. steps]);
    }

    /// <summary>
    /// Writes this tape as line-oriented, run-length encoded text: one line per run of identical
    /// consecutive steps, holding the run's length and then the held keys, held pad buttons and
    /// non-zero axes of the step it repeats. Lines end with <c>\n</c>, and axis values round-trip
    /// exactly. <see cref="Parse"/> of the result equals this tape.
    /// </summary>
    public string ToText()
    {
        StringBuilder builder = new();

        for (int start = 0; start < _snapshots.Length;)
        {
            int end = start + 1;
            while (end < _snapshots.Length && _snapshots[end].Equals(_snapshots[start]))
            {
                end++;
            }

            InputTapeText.WriteLine(builder, _snapshots[start], end - start);
            start = end;
        }

        return builder.ToString();
    }

    /// <summary>An allocation-free enumerator over the tape's steps.</summary>
    public Enumerator GetEnumerator() => new(_snapshots);

    /// <summary>Whether <paramref name="other"/> holds the same snapshots in the same order.</summary>
    public bool Equals(InputTape? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || other._snapshots.Length != _snapshots.Length)
        {
            return false;
        }

        for (int i = 0; i < _snapshots.Length; i++)
        {
            if (!_snapshots[i].Equals(other._snapshots[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as InputTape);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (_hash != 0)
        {
            return _hash;
        }

        HashCode hash = new();
        hash.Add(_snapshots.Length);
        for (int i = 0; i < _snapshots.Length; i++)
        {
            hash.Add(_snapshots[i]);
        }

        int computed = hash.ToHashCode();
        _hash = computed == 0 ? 1 : computed;

        return _hash;
    }

    /// <summary>Whether both tapes hold the same snapshots in the same order.</summary>
    public static bool operator ==(InputTape? left, InputTape? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Whether the tapes differ in length or in any step.</summary>
    public static bool operator !=(InputTape? left, InputTape? right) => !(left == right);

    IEnumerator<DeviceSnapshot> IEnumerable<DeviceSnapshot>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Walks a tape's steps in step order.</summary>
    public struct Enumerator : IEnumerator<DeviceSnapshot>
    {
        private readonly DeviceSnapshot[] _snapshots;
        private int _index;

        internal Enumerator(DeviceSnapshot[] snapshots)
        {
            _snapshots = snapshots;
            _index = -1;
        }

        /// <summary>The step the enumerator stands on.</summary>
        public readonly DeviceSnapshot Current => _snapshots[_index];

        /// <summary>Advances to the next step; false once the tape is spent.</summary>
        public bool MoveNext() => ++_index < _snapshots.Length;

        readonly object IEnumerator.Current => Current;

        void IEnumerator.Reset() => _index = -1;

        readonly void IDisposable.Dispose()
        {
        }
    }
}
