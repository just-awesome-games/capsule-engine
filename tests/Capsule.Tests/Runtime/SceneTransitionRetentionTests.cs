using System.Numerics;
using System.Runtime.CompilerServices;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.UI;

namespace Capsule.Tests.Runtime;

public sealed class SceneTransitionRetentionTests
{
    private const int Transitions = 10;

    private static readonly TextureHandle Atlas = new("retention/atlas", ".png");
    private static readonly Sprite Frame = new(Atlas, new TextureRegion(0, 0, 16, 16));

    // Reachability is asked of the replaced objects themselves. A whole-heap byte count would also see
    // whatever the test host and earlier tests' leftover threads hold at that moment.
    [Fact]
    public void ASceneTransition_RetainsNothingOfTheSceneItReplaced()
    {
        static Scene Resolve(in SceneTransition target) =>
            target.SceneType == typeof(Lobby) ? new Lobby() : new Arena();

        using SceneHost host = new(SceneTransition.ToScene(typeof(Lobby), null), Resolve, new Run());

        List<WeakReference> replaced = [];
        for (long tick = 0; tick < Transitions; tick++)
        {
            Replace(host, tick, replaced);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.All(replaced, reference => Assert.False(reference.IsAlive, $"{reference.Target} outlived its scene."));
    }

    // Steps once, which swaps in the other scene, and records the outgoing scene, its entities and
    // their components. Nothing on this stack outlives the call to keep them alive.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Replace(SceneHost host, long tick, List<WeakReference> replaced)
    {
        Scene outgoing = host.Scene;
        Assert.False(outgoing.Entities.IsEmpty);

        replaced.Add(new WeakReference(outgoing));
        foreach (Entity entity in outgoing.Entities)
        {
            replaced.Add(new WeakReference(entity));
            foreach (Component component in entity.Components)
            {
                replaced.Add(new WeakReference(component));
            }
        }

        host.Step(SceneFixtures.Step(tick));
        Assert.NotSame(outgoing, host.Scene);
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
