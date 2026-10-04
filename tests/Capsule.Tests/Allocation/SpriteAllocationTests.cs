using Capsule.Animation;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Rendering;

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
        SimulationHost run = SpriteBoxTests.Simulate(fighter);
        BoxCollider2D box = fighter.Renderer.Box("hurt");
        box.Detects = new("enemy");
        box.ReportsContacts = true;
        int contacts = 0;
        box.ContactEntered += _ => contacts++;

        Sprite bare = SpriteBoxTests.Frame(2, null);
        Sprite hitting = SpriteBoxTests.Frame(0, new SpriteMarks(boxes: [new("hurt", new Rect(0f, 0f, 8f, 8f))]));
        fighter.Animator.Speed = 1.5f;
        fighter.Animator.Play(new SpriteClip([bare, hitting, bare], [2, 2, 3], loop: true, [["windup"], ["swing", "hit"], []]));

        int reached = 0;
        for (int step = 0; step < 100; step++)
        {
            run.Step();
            reached += fighter.Animator.Reached("hit") ? 1 : 0;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int step = 0; step < 1000; step++)
        {
            run.Step();
            reached += fighter.Animator.Reached("hit") ? 1 : 0;
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(reached > 0 && contacts > 0);
    }
}
