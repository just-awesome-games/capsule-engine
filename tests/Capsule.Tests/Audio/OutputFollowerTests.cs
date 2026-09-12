using Capsule.Runtime.Audio;

namespace Capsule.Tests.Audio;

// The policy that keeps sound on the system's default output: when a reopen happens, how a failed
// one is retried, and what a disconnected device does with no announcement. The device itself is
// faked — nothing here asserts what OpenAL does.
public sealed class OutputFollowerTests
{
    // A power of two, so the retry arithmetic below is exact.
    private const double Frame = 1.0 / 64.0;

    [Fact]
    public void AnAnnouncedChangeReopensOnTheNextUpdateAndOnlyOnce()
    {
        Fixture fixture = new();

        fixture.Follower.Update(Frame);
        fixture.Follower.DefaultChanged();
        fixture.Follower.DefaultChanged();
        fixture.Follower.Update(Frame);
        fixture.Follower.Update(Frame);

        Assert.Equal(1, fixture.Reopens);
    }

    [Fact]
    public void AFailedReopenIsRetriedAfterTheIntervalNotBefore()
    {
        Fixture fixture = new() { ReopenSucceeds = false };

        fixture.Follower.DefaultChanged();
        fixture.Follower.Update(Frame);
        fixture.Follower.Update(OutputFollower.RetrySeconds - Frame);
        Assert.Equal(1, fixture.Reopens);

        fixture.Follower.Update(Frame);
        Assert.Equal(2, fixture.Reopens);

        fixture.ReopenSucceeds = true;
        fixture.Follower.Update(OutputFollower.RetrySeconds);
        fixture.Follower.Update(OutputFollower.RetrySeconds);
        Assert.Equal(3, fixture.Reopens);
    }

    // A fresh announcement during the wait — the replacement output arriving — is acted on at once.
    [Fact]
    public void AnAnnouncementDuringTheRetryWaitReopensAtOnce()
    {
        Fixture fixture = new() { ReopenSucceeds = false };

        fixture.Follower.DefaultChanged();
        fixture.Follower.Update(Frame);
        fixture.Follower.DefaultChanged();
        fixture.Follower.Update(Frame);

        Assert.Equal(2, fixture.Reopens);
    }

    [Fact]
    public void ADisconnectedDeviceIsReopenedWithoutAnAnnouncement()
    {
        Fixture fixture = new() { Connected = false };

        fixture.Follower.Update(Frame);

        Assert.Equal(1, fixture.Reopens);
    }

    [Fact]
    public void AConnectedDeviceWithNoAnnouncementIsLeftAlone()
    {
        Fixture fixture = new();

        for (int i = 0; i < 120; i++)
        {
            fixture.Follower.Update(Frame);
        }

        Assert.Equal(0, fixture.Reopens);
    }

    private sealed class Fixture
    {
        internal Fixture() => Follower = new(Reopen, () => Connected);

        internal OutputFollower Follower { get; }

        internal int Reopens { get; private set; }

        internal bool ReopenSucceeds { get; set; } = true;

        internal bool Connected { get; set; } = true;

        private bool Reopen()
        {
            Reopens++;

            return ReopenSucceeds;
        }
    }
}
