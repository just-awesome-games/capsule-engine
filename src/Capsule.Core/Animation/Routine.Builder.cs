using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Capsule.Animation;

public readonly partial struct Routine
{
    /// <summary>
    /// Builds the <see cref="Routine"/> an <see langword="async"/> routine method returns. The compiler calls it, and
    /// game code does not.
    /// </summary>
    /// <remarks>
    /// It boxes each call's state machine into a pooled <c>RoutineCall</c> and parks it until its sequence starts it.
    /// An await of anything but a <see cref="Wait"/> or another routine ends the routine with an exception.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public struct Builder
    {
        internal const string OutsideRoutine =
            "A Wait or Routine is awaited only inside an async method that returns Routine. Make the method return Routine.";

        private const string ForeignAwait =
            "An async Routine method awaits only Wait values and other routines. Wait for anything else with Wait.Until.";

        private RoutineCall? _call;

        /// <summary>The routine being built.</summary>
        public readonly Routine Task => new(_call!);

        /// <summary>Creates a builder.</summary>
        public static Builder Create() => default;

        /// <summary>Boxes the call's state machine without running it.</summary>
        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine
        {
            RoutineCall<TStateMachine> call = RoutineCall<TStateMachine>.Rent();

            // Set before the copy. The boxed state machine's builder then holds the call too.
            _call = call;
            call.Hold(ref stateMachine);
        }

        /// <summary>Does nothing. The builder boxes the state machine itself.</summary>
        public readonly void SetStateMachine(IAsyncStateMachine stateMachine)
        {
        }

        /// <summary>Marks the call finished.</summary>
        public readonly void SetResult() => _call!.Status = RoutineCallStatus.Finished;

        /// <summary>Marks the call finished with what it threw.</summary>
        public readonly void SetException(Exception exception)
        {
            _call!.Error = exception;
            _call.Status = RoutineCallStatus.Finished;
        }

        /// <summary>Refuses the await, since a routine awaits only waits and routines.</summary>
        /// <exception cref="InvalidOperationException">Always.</exception>
        public readonly void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine =>
            throw new InvalidOperationException(ForeignAwait);

        /// <summary>Parks the call on a <see cref="Wait"/>, or on a routine it awaits, for its sequence to resume.</summary>
        /// <exception cref="InvalidOperationException">The awaiter is neither a wait's nor a routine's.</exception>
        public readonly void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            RoutineCall call = _call!;
            if (typeof(TAwaiter) == typeof(Wait.Awaiter))
            {
                Wait wait = Unsafe.As<TAwaiter, Wait.Awaiter>(ref awaiter).Wait;
                call.Wait = wait;
                call.TicksLeft = wait.TickCount;
                call.Status = RoutineCallStatus.Waiting;

                return;
            }

            if (typeof(TAwaiter) == typeof(Awaiter))
            {
                RoutineCall callee = Unsafe.As<TAwaiter, Awaiter>(ref awaiter).Call!;
                callee.Caller = call;
                call.Callee = callee;
                call.Status = RoutineCallStatus.AwaitingCallee;

                return;
            }

            throw new InvalidOperationException(ForeignAwait);
        }
    }
}
