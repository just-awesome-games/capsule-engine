using System.Numerics;
using Capsule.Navigation;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class PathFollowerAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public void FollowingAPath_AllocatesNothingPerStep()
    {
        Path2D path = new(Vector2.Zero, new Vector2(40f, 0f), new Vector2(40f, 40f), new Vector2(0f, 40f));
        PathFollower2D follower = new(path) { Speed = 90f, Loop = LoopMode.PingPong };
        using SimulationHost run = SceneFixtures.Simulate(new SceneFixtures.Holder(Vector2.Zero, follower));

        StepMeasurement.Report(output, "a path follower turning at both ends", StepMeasurement.Bytes(run));
    }
}
