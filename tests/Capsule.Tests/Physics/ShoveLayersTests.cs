using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Physics;

public sealed class ShoveLayersTests
{
    private const string Hero = "hero";
    private const string Ghost = "ghost";
    private const string Crowd = "crowd";
    private const string Lift = "lift";

    // A shove searches only these layers. A crowd of bodies standing round a pusher that moves none of
    // them is never gathered.
    [Fact]
    public void TheLayersALayerMoves_AreThoseOfTheBodiesItMoves_ThroughEveryChange()
    {
        Scene scene = new Scene().Started();
        SceneFixtures.Body moved = new(Vector2.Zero) { Collider = { Layer = Hero }, Mover = { MovedBy = new(Crowd) } };
        SceneFixtures.Body bystander = new(new Vector2(40f, 0f)) { Collider = { Layer = Crowd } };
        scene.Add(moved);
        scene.Add(bystander);

        Assert.Equal(Bit(scene, Hero), MovedBy(scene, Crowd));
        Assert.Equal(0UL, MovedBy(scene, Hero));

        moved.Collider.Layer = Ghost;
        Assert.Equal(Bit(scene, Ghost), MovedBy(scene, Crowd));

        moved.Mover.MovedBy = new(Lift);
        Assert.Equal(0UL, MovedBy(scene, Crowd));
        Assert.Equal(Bit(scene, Ghost), MovedBy(scene, Lift));

        scene.Remove(moved);
        Assert.Equal(0UL, MovedBy(scene, Lift));
    }

    private static ulong MovedBy(Scene scene, string layer) => scene.Physics.LayersMovedBy(scene.Collision.Layer(layer).Index);

    private static ulong Bit(Scene scene, string layer) => CollisionFilter.Of(scene.Collision.Layer(layer)).Bits;
}
