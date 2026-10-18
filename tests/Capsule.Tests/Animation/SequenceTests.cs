using Capsule.Animation;

namespace Capsule.Tests.Animation;

public sealed class SequenceTests
{
    private readonly List<string> _log = [];

    [Fact]
    public void StartRunsToTheFirstWait_AndATickWaitResumesOnItsNthStep()
    {
        Sequence sequence = new();
        sequence.Start(Beats());

        Assert.Equal(["a"], _log);
        sequence.Step();
        sequence.Step();
        Assert.Equal(["a"], _log);
        sequence.Step();
        Assert.Equal(["a", "b"], _log);
        Assert.False(sequence.IsRunning);

        IEnumerable<Wait> Beats()
        {
            _log.Add("a");
            yield return Wait.Ticks(3);
            yield return Wait.Ticks(0);
            yield return default;
            _log.Add("b");
        }
    }

    // A condition already true at its yield costs no step. No beat can miss an edge raised before it waits.
    [Fact]
    public void AnUntilWait_IsCheckedAtItsYieldAndThenOnEachStep()
    {
        bool open = true;
        Sequence sequence = new();
        sequence.Start(Beats());

        Assert.Equal(["a", "b"], _log);
        sequence.Step();
        Assert.Equal(["a", "b"], _log);
        open = true;
        sequence.Step();
        Assert.Equal(["a", "b", "c"], _log);

        IEnumerable<Wait> Beats()
        {
            _log.Add("a");
            yield return Wait.Until(() => open);
            _log.Add("b");
            open = false;
            yield return Wait.Until(() => open);
            _log.Add("c");
        }
    }

    [Fact]
    public void StopAndARestart_RunTheFinallyBlocksTheBeatsStandIn()
    {
        Sequence sequence = new();
        sequence.Start(Beats("first"));
        sequence.Start(Beats("second"));
        sequence.Stop();

        Assert.Equal(["first released", "second released"], _log);
        Assert.False(sequence.IsRunning);

        IEnumerable<Wait> Beats(string name)
        {
            try
            {
                yield return Wait.Ticks(10);
            }
            finally
            {
                _log.Add($"{name} released");
            }
        }
    }

    public static TheoryData<string, string> Reentries => new()
    {
        { "Start", "beat" }, { "Stop", "beat" }, { "Step", "beat" },
        { "Start", "condition" }, { "Stop", "condition" }, { "Step", "condition" },
        { "Start", "finally" }, { "Stop", "finally" }, { "Step", "finally" },
    };

    [Theory]
    [MemberData(nameof(Reentries))]
    public void CallingASequenceFromItsOwnBeats_Throws(string call, string place)
    {
        Sequence sequence = new();
        Action reenter = call switch
        {
            "Start" => () => sequence.Start([]),
            "Stop" => sequence.Stop,
            _ => sequence.Step,
        };

        if (place == "finally")
        {
            sequence.Start(Holding());
            Assert.Throws<InvalidOperationException>(sequence.Stop);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => sequence.Start(place == "beat" ? Calling() : Waiting()));
        }

        Assert.False(sequence.IsRunning);

        IEnumerable<Wait> Calling()
        {
            reenter();
            yield break;
        }

        IEnumerable<Wait> Waiting()
        {
            yield return Wait.Until(() =>
            {
                reenter();
                return true;
            });
        }

        IEnumerable<Wait> Holding()
        {
            try
            {
                yield return Wait.Ticks(1);
            }
            finally
            {
                reenter();
            }
        }
    }

    [Theory]
    [InlineData("beat")]
    [InlineData("condition at its yield")]
    [InlineData("condition on a step")]
    public void AnExceptionFromTheBeats_PropagatesAndLeavesTheSequenceStopped(string place)
    {
        bool failing = place == "condition at its yield";
        Sequence sequence = new();

        Assert.Throws<InvalidOperationException>(() =>
        {
            sequence.Start(Beats());
            failing = true;
            sequence.Step();
        });
        Assert.False(sequence.IsRunning);
        Assert.Equal(["released"], _log);

        IEnumerable<Wait> Beats()
        {
            try
            {
                if (place == "beat")
                {
                    yield return Wait.Ticks(1);
                    throw new InvalidOperationException("broken");
                }

                yield return Wait.Until(() => failing ? throw new InvalidOperationException("broken") : false);
            }
            finally
            {
                _log.Add("released");
            }
        }
    }

    [Fact]
    public void Stepping_AllocatesNothing()
    {
        int checks = 0;
        Sequence sequence = new();
        sequence.Start(Beats());
        sequence.Step();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            sequence.Step();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(checks > 300);

        IEnumerable<Wait> Beats()
        {
            Func<bool> everyOther = () => ++checks % 2 == 0;
            while (true)
            {
                yield return Wait.Ticks(2);
                yield return Wait.Until(everyOther);
            }
        }
    }
}
