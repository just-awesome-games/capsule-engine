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

    private RoutineCall<TStateMachine>? _nextFree;

    // The pool is a list threaded through its calls. A collection type here is instantiated once per async method, and
    // its array, interface and comparer types cost the executable about 2 KB per method. The pool is thread-static. Hosts
    // stepping runs on parallel threads keep separate pools.
    [field: ThreadStatic]
    private static RoutineCall<TStateMachine>? FirstFree { get; set; }

    internal static RoutineCall<TStateMachine> Rent()
    {
        if (FirstFree is not { } call)
        {
            return new RoutineCall<TStateMachine>();
        }

        FirstFree = call._nextFree;
        call._nextFree = null;

        return call;
    }

    internal void Hold(ref TStateMachine stateMachine) => _machine = stateMachine;

    internal override void MoveNext() => _machine.MoveNext();

    private protected override void Recycle()
    {
        _machine = default!;
        _nextFree = FirstFree;
        FirstFree = this;
    }
}

internal enum RoutineCallStatus
{
    Running,
    Waiting,
    AwaitingCallee,
    Finished,
}
