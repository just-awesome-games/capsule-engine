using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Capsule.Animation;

/// <summary>
/// Runs a <see cref="Routine"/>: scripted beats written as an <see langword="async"/> method that awaits
/// <see cref="Wait"/> values. Its owner steps it each step, and the routine runs in that step's order.
/// </summary>
/// <remarks>
/// A sequence holds whenever its owner does, through a pause or a freeze. A routine that pauses the scene
/// belongs to the scene or to an entity whose step mode keeps it stepping. An owner that leaves the
/// scene stops stepping it.
/// <para>
/// <see cref="Stop"/> abandons the routine where it stands, and its <see langword="finally"/> blocks do not run. An
/// owner undoes what the routine set where it stops it. When the routine throws, the sequence stops and the exception
/// propagates from the call that ran it. A condition that throws on a step abandons it the same way.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// private readonly Sequence _showDoor = new();
///
/// protected override void OnStep(in StepContext context) => _showDoor.Step();
///
/// private async Routine ShowDoor(Entity followed)
/// {
///     Scene.Camera.Follow(_door);
///     await Wait.Ticks(40);
///     _door.Opening = true;
///     await Wait.Until(_door, static door =&gt; door.IsOpen);
///     Scene.Camera.Follow(followed);
/// }
/// </code>
/// </example>
public sealed class Sequence
{
    private RoutineCall? _root;

    // The innermost call, parked on a wait. Null while the routine runs.
    private RoutineCall? _parked;

    // Set while the routine runs, which may not start, stop or step the sequence running it.
    private bool _inRoutine;

    /// <summary>Whether a routine is running. False before the first start and once the routine returns or is stopped.</summary>
    public bool IsRunning => _root is not null;

    /// <summary>
    /// Stops any running routine, then runs <paramref name="routine"/> up to its first wait before returning.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Called from this sequence's own routine, or given a routine that has already run or is running.
    /// </exception>
    public void Start(Routine routine)
    {
        ThrowIfInRoutine();
        RoutineCall? call = routine.Claim();
        Abandon();
        if (call is null)
        {
            return;
        }

        _root = call;
        Run(call);
    }

    /// <summary>
    /// Spends one tick of the current wait, or checks its condition, and runs the routine on to its next
    /// wait once it is over. On a sequence that is not running it does nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called from this sequence's own routine.</exception>
    public void Step()
    {
        ThrowIfInRoutine();
        if (_parked is not { } parked)
        {
            return;
        }

        if (parked.Wait.HasCondition ? Check(parked.Wait) : --parked.TicksLeft <= 0)
        {
            parked.Wait = default;
            Run(parked);
        }
    }

    /// <summary>Abandons the routine where it stands. Its <see langword="finally"/> blocks do not run.</summary>
    /// <exception cref="InvalidOperationException">Called from this sequence's own routine.</exception>
    public void Stop()
    {
        ThrowIfInRoutine();
        Abandon();
    }

    // Resumes calls from this loop alone: a finished call returns to its caller, and an awaited call starts here. No
    // call is resumed or released while its own MoveNext is on the stack.
    private void Run(RoutineCall call)
    {
        _parked = null;
        _inRoutine = true;
        try
        {
            while (true)
            {
                call.Status = RoutineCallStatus.Running;
                call.MoveNext();
                if (call.Status == RoutineCallStatus.Waiting)
                {
                    _parked = call;

                    return;
                }

                if (call.Status == RoutineCallStatus.AwaitingCallee)
                {
                    call = call.Callee!;

                    continue;
                }

                Debug.Assert(call.Status == RoutineCallStatus.Finished, "A call's MoveNext returns parked or finished.");
                if (call.Caller is { } caller)
                {
                    call = caller;

                    continue;
                }

                _root = null;
                Exception? error = call.Error;
                call.Release();
                if (error is not null)
                {
                    ExceptionDispatchInfo.Throw(error);
                }

                return;
            }
        }
        finally
        {
            _inRoutine = false;
        }
    }

    private bool Check(Wait wait)
    {
        _inRoutine = true;
        try
        {
            return wait.IsMet();
        }
        catch
        {
            Abandon();

            throw;
        }
        finally
        {
            _inRoutine = false;
        }
    }

    // Releases the parked call and every call awaiting it, innermost first.
    private void Abandon()
    {
        RoutineCall? call = _parked ?? _root;
        _root = null;
        _parked = null;
        while (call is not null)
        {
            RoutineCall? caller = call.Caller;
            call.Release();
            call = caller;
        }
    }

    private void ThrowIfInRoutine()
    {
        if (_inRoutine)
        {
            throw new InvalidOperationException(
                "A sequence cannot start, stop or step itself from its own routine. Return from the routine to end it.");
        }
    }
}
