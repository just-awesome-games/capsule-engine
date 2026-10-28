using Capsule.Runtime.DevTools.Recordings;

namespace Capsule.Tests.DevTools;

// A --trace or --capture selection counts its ticks against a ceiling before the run where it can, and
// halts the run at the first tick past it where it cannot.
public sealed class TickSelectionTests
{
    // A tick two ranges share counts once, a closed range is cut at the run's last tick, and an open range
    // counts only against a known last tick.
    [Theory]
    [InlineData("0,3..4", null, 3L)]
    [InlineData("1..10,5..15,7", null, 15L)]
    [InlineData("50..60", 55L, 6L)]
    [InlineData("100..", 199L, 100L)]
    [InlineData("100..", null, 0L)]
    [InlineData("0..5,3..", 9L, 10L)]
    public void Count_CountsEachTickOnce_WithinTheRunsLastTick(string ticks, long? lastTick, long count) =>
        Assert.Equal(count, TickSelection.Count(TickRange.TryParseList(ticks)!, lastTick));

    [Fact]
    public void ASelectionPastTheCeiling_IsRefused_NamingTheFlagTheCountTheCeilingAndTheFix()
    {
        TickSelection ticks = new("--capture", TickRange.TryParseList("0..")!, ceiling: 600, force: false);

        Assert.Null(ticks.Refusal(lastTick: null));
        Assert.Null(ticks.Refusal(lastTick: 599));
        Assert.Equal(
            "--capture selects 4200 ticks, past its ceiling of 600. Name the ticks around the moment in question, or give --force to write them all.",
            ticks.Refusal(lastTick: 4199));
    }

    // A tick outside the selection is no step towards the ceiling, and every tick after the first one past
    // it is refused too.
    [Fact]
    public void Take_HaltsAtTheFirstTickPastTheCeiling()
    {
        TickSelection ticks = new("--trace", TickRange.TryParseList("2..")!, ceiling: 3, force: false);

        Assert.Equal([false, false, true, true, true, false, false], Enumerable.Range(0, 7).Select(tick => ticks.Take(tick)));
        Assert.Equal(5L, ticks.HaltedAt);
        Assert.Contains("stopped the run at tick 5, past its ceiling of 3 ticks, and kept the 3 it wrote to 'trace.csv'", ticks.HaltMessage("'trace.csv'"), StringComparison.Ordinal);
    }

    [Fact]
    public void Force_LiftsTheCeiling()
    {
        TickSelection ticks = new("--trace", TickRange.TryParseList("0..")!, ceiling: 3, force: true);

        Assert.Null(ticks.Refusal(lastTick: 100));
        Assert.All(Enumerable.Range(0, 10), tick => Assert.True(ticks.Take(tick)));
        Assert.Null(ticks.HaltedAt);
    }
}
