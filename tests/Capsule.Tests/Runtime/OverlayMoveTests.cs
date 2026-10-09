using System.Numerics;
using Capsule.Input;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using static Capsule.Tests.Runtime.OverlayFixtures;
using Body = Capsule.Tests.Scenes.SceneFixtures.Body;

namespace Capsule.Tests.Runtime;

// G moves the entity whose panel is on top to the world point under the pointer, inside one stepped tick.
public sealed class OverlayMoveTests
{
    // Over the world, clear of the menu. It lands inside the yard's zone.
    private static readonly Vector2 WorldPoint = new(700f, 300f);

    // The camera follows the hero's child, so it cuts with the subtree. A first step settles the camera,
    // whose first settle would cut anyway.
    [Fact]
    public void Move_LandsTheEntityInOneTickWithNoInterpolationItsContactsSettledAndTheCameraCut()
    {
        Yard yard = new();
        using OverlayRig rig = Framing(yard);
        OpenPanel(rig, downs: 1, "Hero");
        rig.Press(Key.Right);
        rig.PlaceWorld();
        Vector2 point = rig.World!.Value.ToWorld(WorldPoint);

        PressAt(rig, Key.G);

        Assert.Equal(2, rig.Scheduler.Tick);
        Assert.Equal(point, yard.Hero.Position);
        Assert.Equal(yard.Hero.World, yard.Hero.PreviousWorld);
        Assert.Equal(1, yard.Entered);
        CameraView from = rig.Host.View.Camera.At(0f);
        Assert.Equal(from, rig.Host.View.Camera.At(1f));
        Assert.Equal(yard.Eye.WorldPosition, from.Center);
        Assert.Contains("Moved Hero", rig.Overlay.Scene.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void MoveWhileHidden_LandsAChildsWorldPositionOnThePoint()
    {
        Yard yard = new();
        using OverlayRig rig = Framing(yard);
        Vector2 point = OpenPanel(rig, downs: 2, "Eye");
        rig.Press(Key.H);
        Assert.True(rig.Overlay.IsHidden);

        PressAt(rig, Key.G);

        Assert.Equal(1, rig.Scheduler.Tick);
        Assert.Equal(point.X, yard.Eye.WorldPosition.X, 4);
        Assert.Equal(point.Y, yard.Eye.WorldPosition.Y, 4);
        Assert.Equal(Yard.HeroStart, yard.Hero.Position);
    }

    // Nothing is moved by default, not even the camera's subject.
    [Fact]
    public void MoveWithNoEntityPanelOnTop_StepsNothingAndSaysWhy()
    {
        Yard yard = new();
        using OverlayRig rig = Framing(yard);
        rig.Open();
        rig.Press(Key.S);
        rig.PlaceWorld();

        PressAt(rig, Key.G);

        Assert.Equal(0, rig.Scheduler.Tick);
        Assert.Equal(Yard.HeroStart, yard.Hero.Position);
        Assert.Contains("Open its panel", rig.Overlay.Scene.Status, StringComparison.Ordinal);
    }

    // Opens the entity panel `downs` rows below the scene page's Camera row and returns the world point
    // under WorldPoint.
    private static Vector2 OpenPanel(OverlayRig rig, int downs, string title)
    {
        rig.Open();
        rig.Press(Key.S);
        for (int down = 0; down < downs; down++)
        {
            rig.Press(Key.Down);
        }

        rig.Press(Key.Enter);
        Assert.Equal(title, rig.Overlay.Title);
        rig.PlaceWorld();

        return rig.World!.Value.ToWorld(WorldPoint);
    }

    private static void PressAt(OverlayRig rig, Key key)
    {
        rig.Frame(DeviceSnapshot.Of(key).WithPointer(WorldPoint));
        rig.Frame(DeviceSnapshot.Empty.WithPointer(WorldPoint));
    }

    // A hero left of a zone that holds every point the pointer can land on, and a camera following the
    // hero's child.
    private sealed class Yard : Scene
    {
        internal static readonly Vector2 HeroStart = new(-100f, 0f);

        internal Yard()
        {
            Camera.ViewportSize = new Vector2(320f, 180f);
            Hero.Collider.ReportsContacts = true;
            Hero.Collider.Detects = new("zone");
            Hero.Collider.ContactEntered += _ => Entered++;
            Eye.Parent = Hero;
            Add(Hero);

            Entity zone = new Zone();
            Add(zone);
        }

        internal Body Hero { get; } = new(HeroStart) { Name = "Hero" };

        internal Entity Eye { get; } = new Marker(new Vector2(4f, -10f)) { Name = "Eye" };

        internal int Entered { get; private set; }

        protected override void OnStart() => Camera.Follow(Eye);
    }

    private sealed class Marker(Vector2 position) : Entity(position);

    private sealed class Zone : Entity
    {
        internal Zone()
            : base(new Vector2(-40f, -200f)) => Add(new BoxCollider2D(new Vector2(400f, 400f)) { Layer = "zone" });
    }
}
