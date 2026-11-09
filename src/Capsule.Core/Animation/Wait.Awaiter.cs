using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Capsule.Animation;

public readonly partial struct Wait
{
    /// <summary>Awaits a <see cref="Wait"/> from a <see cref="Routine"/>. Game code awaits the wait instead.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public readonly struct Awaiter : ICriticalNotifyCompletion
    {
        internal Wait Wait { get; }

        internal Awaiter(Wait wait) => Wait = wait;

        /// <summary>Whether the wait is over at the await: zero ticks, or a condition that already reads true.</summary>
        public bool IsCompleted => Wait.HasCondition ? Wait.IsMet() : Wait.TickCount == 0;

        /// <summary>Does nothing. A wait has no result.</summary>
        public void GetResult()
        {
        }

        /// <summary>Refuses a continuation, since only an <see langword="async"/> routine awaits a wait.</summary>
        /// <exception cref="InvalidOperationException">Always.</exception>
        public void OnCompleted(Action continuation) => throw new InvalidOperationException(Routine.Builder.OutsideRoutine);

        /// <summary>Refuses a continuation, since only an <see langword="async"/> routine awaits a wait.</summary>
        /// <exception cref="InvalidOperationException">Always.</exception>
        public void UnsafeOnCompleted(Action continuation) => throw new InvalidOperationException(Routine.Builder.OutsideRoutine);
    }
}
