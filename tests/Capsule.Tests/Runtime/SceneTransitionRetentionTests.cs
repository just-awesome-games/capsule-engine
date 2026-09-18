using System.Numerics;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Tests.Allocation;
using Capsule.Tests.Scenes;
using Capsule.UI;

namespace Capsule.Tests.Runtime;

// The whole managed heap is measured, so nothing else may allocate on another thread meanwhile.
[Collection(StageAllocationCollection.Name)]
public sealed class SceneTransitionRetentionTests
{
    private const int WarmUpTransitions = 10;
    private const int MeasuredTransitions = 50;

    // The whole managed heap is compared across the measured transitions, so the slack covers the
    // GC's own bookkeeping and whatever the runtime lazily allocates on first use of a path it did
    // not touch during warm-up; a retained scene would cost tens of kilobytes per transition.
    private const long ToleranceBytes = 16 * 1024;

    private static readonly TextureHandle Atlas = new("retention/atlas", ".png");
    private static readonly Sprite Frame = new(Atlas, new TextureRegion(0, 0, 16, 16));

    [Fact]
    public void ASceneTransition_RetainsNothingOfTheSceneItReplaced()
    {
        static Scene Resolve(in SceneTransition target) =>
            target.SceneType == typeof(Lobby) ? new Lobby() : new Arena();

        using SceneHost host = new(SceneTransition.ToScene(typeof(Lobby), null), Resolve, new Run());

        long tick = 0;
        Transition(host, WarmUpTransitions, ref tick);
        long before = RetainedBytes();

        Transition(host, MeasuredTransitions, ref tick);
        long after = RetainedBytes();

        Assert.IsType<Lobby>(host.Scene);
        Assert.InRange(after - before, long.MinValue, ToleranceBytes);
    }

    // Every step requests the other scene, so one step is one transition.
    private static void Transition(SceneHost host, int count, ref long tick)
    {
        Type expected = host.Scene.GetType();
        for (int index = 0; index < count; index++)
        {
            host.Step(SceneFixtures.Step(tick++));
            Assert.NotEqual(expected, host.Scene.GetType());
            expected = host.Scene.GetType();
        }
    }

    private static long RetainedBytes()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        return GC.GetTotalMemory(true);
    }

    // Two scenes of the shape a game's are: bodies with colliders in the collision world, sprites,
    // a HUD label, and each asking for the other on its first step.
    private sealed class Lobby : Scene
    {
        internal Lobby()
        {
            for (int index = 0; index < 24; index++)
            {
                Add(new Prop(new Vector2(index * 20f, 0f)));
            }

            Add(new Hud("Lobby"));
        }

        protected internal override void CollectAssets(AssetCollection assets) => assets.Add([Atlas]);

        protected override void OnStep(in StepContext context) => Run.RequestScene<Arena>();
    }

    private sealed class Arena : Scene
    {
        internal Arena()
        {
            for (int index = 0; index < 40; index++)
            {
                Add(new Prop(new Vector2(index * 20f, index * 8f)));
            }

            Add(new Hud("Arena"));
        }

        protected internal override void CollectAssets(AssetCollection assets) => assets.Add([Atlas]);

        protected override void OnStep(in StepContext context) => Run.RequestScene<Lobby>();
    }

    private sealed class Prop : Entity
    {
        internal Prop(Vector2 position)
            : base(position)
        {
            BoxCollider2D collider = new(new Vector2(16f, 16f));
            Add(new SpriteRenderer(Frame));
            Add(collider);
            Add(new KinematicBody2D(collider));
        }
    }

    private sealed class Hud : ScreenEntity
    {
        internal Hud(string title)
            : base(Anchor.TopLeft, Vector2.Zero)
        {
            Add(new ColorRect(new Vector2(200f, 20f)));
            Add(new Label(BitmapFont.Default, title));
        }
    }
}
