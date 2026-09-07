using System.Collections;

namespace Capsule.Input;

/// <summary>
/// An immutable recording of device input: exactly one <see cref="DeviceSnapshot"/> per fixed step,
/// in step order. A tape and the run's initial state and step length are what a deterministic run
/// is reproduced from, so a tape is a run's input in full — there is no per-frame residue beside it.
/// </summary>
/// <remarks>
/// Two tapes are equal when they hold the same snapshots in the same order. Build one with
/// <see cref="InputScript"/>.
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

    // A tape holding snapshots, one per step, copied on the way in. Internal because a game
    // authors a tape with InputScript or replays a recorded one; nothing else builds one.
    internal static InputTape Of(params ReadOnlySpan<DeviceSnapshot> snapshots) =>
        snapshots.IsEmpty ? EmptyTape : new InputTape(snapshots.ToArray());

    /// <summary>The snapshot the step at <paramref name="index"/> is driven by.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the tape.</exception>
    public DeviceSnapshot this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _snapshots.Length);

            return _snapshots[index];
        }
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
