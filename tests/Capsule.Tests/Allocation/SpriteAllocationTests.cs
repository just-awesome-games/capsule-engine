using Capsule.Animation;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Rendering;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class SpriteAllocationTests
{
    // A looping attack at a fractional speed: events raised and polled, and a box that enters, exits
    // and re-registers on every pass.
    [Fact]
    public void AnAnimatorRaisingEventsAndTogglingABox_AllocatesNothingOnceWarm()
    {
        SpriteBoxTests.Fighter fighter = new();
        SimulationHost run = SceneFixtures.Simulate(fighter, SpriteBoxTests.Target.InReach());
        BoxCollider2D box = fighter.Renderer.Box("hurt");
        box.Detects = new("enemy");
        box.ReportsContacts = true;
        int contacts = 0;
        box.ContactEntered += _ => contacts++;

        Sprite bare = SpriteBoxTests.Frame(2, null);
        Sprite hitting = SpriteBoxTests.Frame(0, new SpriteMarks(boxes: [new("hurt", Rect.FromEdges(0f, 0f, 8f, 8f))]));
        fighter.Animator.Speed = 1.5f;
        fighter.Animator.Play(new SpriteClip([bare, hitting, bare], [2, 2, 3], loop: true, [["windup"], ["swing", "hit"], []]));

        int reached = 0;
        long bytes = StepMeasurement.Bytes(
            _ =>
            {
                run.Step();
                reached += fighter.Animator.Reached("hit") ? 1 : 0;
            },
            warmup: 100,
            measured: 1000);

        Assert.Equal(0, bytes);
        Assert.True(reached > 0 && contacts > 0);
    }

    // A Play reports into the buffer the next step makes readable, and the two buffers alternate.
    [Fact]
    public void APlayReportingAnEventOnEitherBuffer_AllocatesNothingAfterConstruction()
    {
        SpriteBoxTests.Fighter fighter = new();
        SimulationHost run = SceneFixtures.Simulate(fighter, SpriteBoxTests.Target.InReach());
        Sprite bare = SpriteBoxTests.Frame(2, null);
        SpriteClip silent = new([bare, bare], [1, 1], loop: true);
        SpriteClip loud = new([bare, bare], [1, 1], loop: true, [["a"], ["b"]]);
        // Silent plays warm both buffers. The measured plays report.
        long bytes = StepMeasurement.Bytes(
            play =>
            {
                fighter.Animator.Play(play < 4 ? silent : loud, restart: true);
                run.Step();
            },
            warmup: 4,
            measured: 4);

        Assert.Equal(0, bytes);
    }
}
