using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Capsule.Animation;

public readonly partial struct Routine
{
    /// <summary>Awaits a <see cref="Routine"/> from another routine. Game code awaits the routine instead.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public readonly struct Awaiter : ICriticalNotifyCompletion
    {
        internal RoutineCall? Call { get; }

        internal Awaiter(RoutineCall? call) => Call = call;

        /// <summary>Whether the awaited routine is already over. Only an empty one is, since a routine starts when awaited.</summary>
        public bool IsCompleted => Call is null;

        /// <summary>Releases the finished call, and rethrows what it threw.</summary>
        public void GetResult()
        {
            if (Call is not { } call)
            {
                return;
            }

            Exception? error = call.Error;
            call.Release();
            if (error is not null)
            {
                ExceptionDispatchInfo.Throw(error);
            }
        }

        /// <summary>Refuses a continuation, since only an <see langword="async"/> routine awaits a routine.</summary>
        /// <exception cref="InvalidOperationException">Always.</exception>
        public void OnCompleted(Action continuation) => throw new InvalidOperationException(Builder.OutsideRoutine);

        /// <summary>Refuses a continuation, since only an <see langword="async"/> routine awaits a routine.</summary>
        /// <exception cref="InvalidOperationException">Always.</exception>
        public void UnsafeOnCompleted(Action continuation) => throw new InvalidOperationException(Builder.OutsideRoutine);
    }
}
