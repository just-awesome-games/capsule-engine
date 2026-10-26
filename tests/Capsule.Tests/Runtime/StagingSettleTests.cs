using Capsule.Runtime.Gpu;

namespace Capsule.Tests.Runtime;

public sealed class StagingSettleTests
{
    private const long Quiet = 100;

    // A burst's buffer returns to its steady size once, after a full quiet period of frames that fit it.
    [Fact]
    public void EndFrame_AfterABurst_RecreatesOnceTheQuietPeriodHasPassed()
    {
        StagingSettle settle = new(Quiet);

        Assert.False(settle.EndFrame(StagingSettle.SteadyBytes * 4, overflowed: true, now: 0));
        Assert.False(settle.EndFrame(StagingSettle.SteadyBytes, overflowed: false, now: Quiet - 1));
        Assert.True(settle.EndFrame(StagingSettle.SteadyBytes, overflowed: false, now: Quiet));
        Assert.False(settle.EndFrame(0, overflowed: false, now: Quiet * 3));
    }

    // A frame that stages more than the steady size restarts the wait. A game whose every frame needs a
    // grown buffer keeps it.
    [Fact]
    public void EndFrame_AFrameAboveTheSteadySize_RestartsTheQuietPeriod()
    {
        StagingSettle settle = new(Quiet);

        settle.EndFrame(StagingSettle.SteadyBytes * 2, overflowed: true, now: 0);
        Assert.False(settle.EndFrame(StagingSettle.SteadyBytes + 1, overflowed: false, now: Quiet));
        Assert.False(settle.EndFrame(0, overflowed: false, now: Quiet * 2 - 1));
        Assert.True(settle.EndFrame(0, overflowed: false, now: Quiet * 2));
    }

    // Alignment can overflow the buffer with no more than the steady size staged. That frame still
    // starts the wait, including the first frame of a run.
    [Fact]
    public void EndFrame_AnOverflowWithinTheSteadySize_StartsTheQuietPeriod()
    {
        StagingSettle settle = new(Quiet);

        Assert.False(settle.EndFrame(StagingSettle.SteadyBytes, overflowed: true, now: Quiet * 5));
        Assert.False(settle.EndFrame(0, overflowed: false, now: Quiet * 6 - 1));
        Assert.True(settle.EndFrame(0, overflowed: false, now: Quiet * 6));
    }

    // A texture's band never outgrows the steady size unless one row does. Streaming a large page then
    // never grows the buffer past the size SDL's Vulkan driver gives a 64 MB block of its own.
    [Theory]
    [InlineData(4096 * 4, 64)]
    [InlineData(300 * 4, 873)]
    [InlineData(StagingSettle.SteadyBytes * 2, 1)]
    public void BandRows_FitTheSteadySize_OrHoldOneRow(int rowBytes, int expected)
    {
        int rows = StagingSettle.BandRows(rowBytes);

        Assert.Equal(expected, rows);
        Assert.True(rows == 1 || rows * rowBytes <= StagingSettle.SteadyBytes);
    }

    // A buffer that never grew or overflowed is never recreated, however long it stays quiet.
    [Fact]
    public void EndFrame_WithoutABurst_NeverRecreates()
    {
        StagingSettle settle = new(Quiet);

        Assert.False(settle.EndFrame(StagingSettle.SteadyBytes, overflowed: false, now: 0));
        Assert.False(settle.EndFrame(0, overflowed: false, now: Quiet * 10));
    }
}
