using System.Runtime.CompilerServices;

namespace Capsule.Animation;

// One call of an async Routine method: its boxed state machine, what it waits on, and the call awaiting it. A
// sequence resumes calls only from its own loop. No call is resumed or released while its own MoveNext is on the
// stack.
internal abstract class RoutineCall
{
    internal RoutineCallStatus Status { get; set; }

    // Bumped when a sequence or another call claims the call, and again on release. A Routine matches only the unclaimed
    // call it was built for. Starting or awaiting it twice is refused, and so is keeping it past its call.
    internal int Generation { get; private set; }

    internal RoutineCall? Caller { get; set; }

    internal RoutineCall? Callee { get; set; }

    // The wait the call is parked on.
    internal Wait Wait { get; set; }

    internal int TicksLeft { get; set; }

    internal Exception? Error { get; set; }

    internal abstract void MoveNext();

    internal bool TryClaim(int generation)
    {
        if (generation != Generation)
        {
            return false;
        }

        Generation++;

        return true;
    }

    internal void Release()
    {
        Generation++;
        Status = RoutineCallStatus.Running;
        Caller = null;
        Callee = null;
        Wait = default;
        TicksLeft = 0;
        Error = null;
        Recycle();
    }

    private protected abstract void Recycle();
}

internal sealed class RoutineCall<TStateMachine> : RoutineCall
    where TStateMachine : IAsyncStateMachine
{
    private TStateMachine _machine = default!;

    // Thread-static. Hosts stepping runs on parallel threads keep separate pools.
    [field: ThreadStatic]
    private static Stack<RoutineCall<TStateMachine>>? Pool { get; set; }

    internal static RoutineCall<TStateMachine> Rent() =>
        Pool is { } pool && pool.TryPop(out RoutineCall<TStateMachine>? call) ? call : new RoutineCall<TStateMachine>();

    internal void Hold(ref TStateMachine stateMachine) => _machine = stateMachine;

    internal override void MoveNext() => _machine.MoveNext();

    private protected override void Recycle()
    {
        _machine = default!;
        (Pool ??= new Stack<RoutineCall<TStateMachine>>()).Push(this);
    }
}

internal enum RoutineCallStatus
{
    Running,
    Waiting,
    AwaitingCallee,
    Finished,
}
