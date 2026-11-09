using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Capsule.Animation;

namespace Capsule.Tests.Animation;

public sealed class SequenceTests
{
    private readonly List<string> _log = [];

    private bool _open;

    private int _checks;

    // An unoptimized build emits each async state machine as a class the call allocates.
    private static bool Optimized =>
        typeof(SequenceTests).Assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };

    [Fact]
    public void CallingARoutineRunsNothing_UntilStartRunsItToItsFirstWait()
    {
        Sequence sequence = new();
        Routine routine = Run();
        Assert.Empty(_log);

        sequence.Start(routine);
        Assert.Equal(["a"], _log);
        sequence.Step();
        sequence.Step();
        Assert.Equal(["a"], _log);
        sequence.Step();
        Assert.Equal(["a", "b"], _log);
        Assert.False(sequence.IsRunning);

        async Routine Run()
        {
            _log.Add("a");
            await Wait.Ticks(3);
            await Wait.Ticks(0);
            await default(Wait);
            await default(Routine);
            _log.Add("b");
        }
    }

    // A condition already true at its await costs no step. No beat can miss an edge raised before it waits.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUntilWait_IsCheckedAtItsAwaitAndThenOnEachStep(bool passesState)
    {
        _open = true;
        Sequence sequence = new();
        sequence.Start(Run());

        Assert.Equal(["a", "b"], _log);
        sequence.Step();
        Assert.Equal(["a", "b"], _log);
        _open = true;
        sequence.Step();
        Assert.Equal(["a", "b", "c"], _log);

        async Routine Run()
        {
            _log.Add("a");
            await Opened();
            _log.Add("b");
            _open = false;
            await Opened();
            _log.Add("c");
        }

        Wait Opened() => passesState ? Wait.Until(this, static test => test._open) : Wait.Until(() => _open);
    }

    // Back-to-back calls of one method share a pooled box. A released box must never resume.
    [Fact]
    public void AwaitedRoutines_RunInlineInOrder_ThroughNestingAndBackToBackCalls()
    {
        Sequence sequence = new();
        sequence.Start(Outer());
        while (sequence.IsRunning)
        {
            sequence.Step();
        }

        Assert.Equal(["x>", "x.", "x<", "y>", "y.", "y<", "z", "end"], _log);

        async Routine Outer()
        {
            await Middle("x");
            await Middle("y");
            await Inner("z", 0);
            _log.Add("end");
        }

        async Routine Middle(string name)
        {
            _log.Add($"{name}>");
            await Inner($"{name}.", 2);
            _log.Add($"{name}<");
        }

        async Routine Inner(string name, int ticks)
        {
            await Wait.Ticks(ticks);
            _log.Add(name);
        }
    }

    // A pooled call outlives its routine. A state it held would keep a despawned owner and its scene alive.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APooledCall_LetsGoOfTheStateItWaitedOn(bool stops)
    {
        Sequence sequence = new();
        WeakReference state = StartWaitingOnState(sequence);
        if (stops)
        {
            sequence.Stop();
        }
        else
        {
            sequence.Step();
        }

        Assert.False(sequence.IsRunning);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Assert.False(state.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference StartWaitingOnState(Sequence sequence)
    {
        StrongBox<bool> gate = new();
        sequence.Start(Run(gate));
        gate.Value = true;

        return new WeakReference(gate);

        static async Routine Run(StrongBox<bool> gate) => await Wait.Until(gate, static gate => gate.Value);
    }

    [Fact]
    public void StopAndARestart_AbandonTheRoutineWithoutRunningItsFinallyBlocks()
    {
        Sequence sequence = new();
        sequence.Start(Outer("first"));
        sequence.Start(Outer("second"));
        sequence.Stop();
        sequence.Step();

        Assert.Empty(_log);
        Assert.False(sequence.IsRunning);

        async Routine Outer(string name)
        {
            try
            {
                await Inner();
            }
            finally
            {
                _log.Add($"{name} released");
            }
        }

        async Routine Inner()
        {
            await Wait.Ticks(10);
            _log.Add("resumed");
        }
    }

    [Theory]
    [InlineData("start twice")]
    [InlineData("await a started run")]
    [InlineData("start a finished run")]
    public void ARoutine_RunsOnce(string misuse)
    {
        Sequence sequence = new();
        Sequence other = new();
        Routine held = Held();
        sequence.Start(held);

        switch (misuse)
        {
            case "start twice":
                Assert.Throws<InvalidOperationException>(() => other.Start(held));
                Assert.True(sequence.IsRunning);
                break;
            case "await a started run":
                Assert.Throws<InvalidOperationException>(() => other.Start(Awaiting(held)));
                break;
            default:
                sequence.Stop();
                Assert.Throws<InvalidOperationException>(() => other.Start(held));
                break;
        }

        async Routine Held() => await Wait.Ticks(5);

        async Routine Awaiting(Routine routine) => await routine;
    }

    [Fact]
    public void AwaitingAnythingButWaitsAndRoutines_ThrowsFromTheRoutine()
    {
        Sequence sequence = new();

        Assert.Throws<InvalidOperationException>(() => sequence.Start(Run()));
        Assert.False(sequence.IsRunning);

        async Routine Run() => await Task.Yield();
    }

    public static TheoryData<string, string> Reentries => new()
    {
        { "Start", "beat" }, { "Stop", "beat" }, { "Step", "beat" },
        { "Start", "condition" }, { "Stop", "condition" }, { "Step", "condition" },
    };

    [Theory]
    [MemberData(nameof(Reentries))]
    public void CallingASequenceFromItsOwnRoutine_Throws(string call, string place)
    {
        Sequence sequence = new();
        Action reenter = call switch
        {
            "Start" => () => sequence.Start(default),
            "Stop" => sequence.Stop,
            _ => sequence.Step,
        };

        Assert.Throws<InvalidOperationException>(() => sequence.Start(place == "beat" ? Calling() : Waiting()));
        Assert.False(sequence.IsRunning);

        async Routine Calling()
        {
            await Wait.Ticks(0);
            reenter();
        }

        async Routine Waiting() => await Wait.Until(() =>
        {
            reenter();
            return true;
        });
    }

    // An exception the routine throws unwinds it as any async method does. A step's condition check abandons it.
    [Theory]
    [InlineData("beat", true)]
    [InlineData("awaited routine", true)]
    [InlineData("condition at its await", true)]
    [InlineData("condition on a step", false)]
    public void AnExceptionFromTheRoutine_PropagatesAndLeavesTheSequenceStopped(string place, bool unwinds)
    {
        bool failing = place == "condition at its await";
        Sequence sequence = new();

        Assert.Throws<InvalidOperationException>(() =>
        {
            sequence.Start(Outer());
            failing = true;
            sequence.Step();
        });
        Assert.False(sequence.IsRunning);
        Assert.Equal(unwinds ? ["released"] : [], _log);

        async Routine Outer()
        {
            try
            {
                if (place == "awaited routine")
                {
                    await Throwing();
                }
                else if (place == "beat")
                {
                    await Wait.Ticks(1);
                    throw new InvalidOperationException("broken");
                }

                await Wait.Until(() => failing ? throw new InvalidOperationException("broken") : false);
            }
            finally
            {
                _log.Add("released");
            }
        }

        async Routine Throwing()
        {
            await Wait.Ticks(1);
            throw new InvalidOperationException("broken");
        }
    }

    [Fact]
    public void Stepping_AllocatesNothing()
    {
        int checks = 0;
        Func<bool> everyOther = () => ++checks % 2 == 0;
        Sequence sequence = new();
        sequence.Start(Run());

        // One whole cycle first. The first condition wait of a process builds the delegate every later one shares.
        for (int i = 0; i < 4; i++)
        {
            sequence.Step();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            sequence.Step();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(checks > 300);

        async Routine Run()
        {
            while (true)
            {
                await Wait.Ticks(2);
                await Wait.Until(everyOther);
            }
        }
    }

    [Fact]
    public void StartingNestedRoutinesAgain_AllocatesNothing_InAnOptimizedBuild()
    {
        Assert.SkipUnless(Optimized, "An unoptimized build allocates each async state machine, by the compiler's design.");
        Sequence sequence = new();
        RunAndStop();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            RunAndStop();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        void RunAndStop()
        {
            sequence.Start(Outer());
            for (int step = 0; step < 6; step++)
            {
                sequence.Step();
            }

            sequence.Start(Outer());
            sequence.Stop();
        }

        async Routine Outer()
        {
            await Inner();
            await Inner();
        }

        async Routine Inner()
        {
            await Wait.Ticks(2);
            await Wait.Until(this, static test => ++test._checks % 2 == 0);
        }
    }
}
