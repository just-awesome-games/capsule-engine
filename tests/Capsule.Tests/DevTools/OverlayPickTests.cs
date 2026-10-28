using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.DevTools.Overlay;
using Capsule.Runtime.Rendering;
using Capsule.Scenes;
using static Capsule.Tests.DevTools.OverlayFixtures;

namespace Capsule.Tests.DevTools;

// A pick reads back what the held frame draws under a world point. A click on the world selects it.
public sealed class OverlayPickTests
{
    // A layer held back by its scroll factor is drawn away from its position. The pick finds it where it is drawn.
    [Fact]
    public void APick_FindsAScrolledLayerWhereTheFrameDrawsIt()
    {
        Pile pile = new();
        pile.Back.ScrollFactor = Vector2.Zero;
        using SceneSimulation simulation = new(pile);
        Vector2 parallax = new(-40f, -30f);
        Vector2 drawn = ScrollLayout.Place(Pile.BackOnly, ScrollLayout.Corner(Vector2.Zero, parallax, Vector2.Zero), Vector2.Zero, snap: false, scale: 1f);

        Assert.Equal([pile.Back], Pick(simulation, drawn, parallax));
        Assert.Empty(Pick(simulation, Pile.BackOnly, parallax));
    }

    // Root first, then down to what drew, then on down the stack, and round again. A pick replaces the entity
    // panel on top. Back returns to the root.
    [Fact]
    public void ClicksOnOneSpot_CycleDownTheStackOnOnePanel()
    {
        using OverlayRig rig = Framing(new Pile());
        rig.Open();
        rig.PlaceWorld();
        Vector2 overlap = Window(rig.World!.Value, Pile.Overlap);

        List<string?> titles = [];
        for (int click = 0; click < 4; click++)
        {
            Click(rig, overlap);
            titles.Add(rig.Overlay.Title);

            Assert.Equal(2, rig.Overlay.Depth);
        }

        Assert.Equal(["Holder", "Front", "Back", "Holder"], titles);
        Assert.EndsWith("Holder", rig.Overlay.Scene.Readout()[2], StringComparison.Ordinal);

        Click(rig, Window(rig.World!.Value, Pile.BackOnly));

        Assert.Equal("Back", rig.Overlay.Title);

        rig.Press(Key.Backspace);

        Assert.Equal(1, rig.Overlay.Depth);
    }

    private static Entity[] Pick(SceneSimulation simulation, Vector2 point, Vector2 parallax = default)
    {
        List<DrawRun> runs = [];
        simulation.DrawRuns = runs;
        List<Entity> stack = [];
        Picker.Pick(simulation.View, runs.ToArray(), point, parallax, stack);

        return [.. stack];
    }

    private static void Click(OverlayRig rig, Vector2 window)
    {
        rig.Frame(DeviceSnapshot.Empty.WithPointer(window).With(MouseButton.Left));
        rig.Frame(DeviceSnapshot.Empty.WithPointer(window));
    }

    private static Vector2 Window(in WorldPlacement world, Vector2 point) =>
        world.Present.Origin + (new Vector2(world.Fit.X, world.Fit.Y) * world.Present.Scale) + ((point - world.TopLeft) * world.PixelsPerUnit);

    // A wide back rect, and over its centre a smaller one drawn by the child of an entity that draws nothing.
    private sealed class Pile : Scene
    {
        internal static readonly Vector2 Overlap = new(228f, 108f);
        internal static readonly Vector2 BackOnly = new(205f, 85f);

        internal Entity Back { get; } = Drawn("Back", new Vector2(200f, 80f), 64f);

        internal Entity Holder { get; } = new Piece(new Vector2(220f, 100f)) { Name = "Holder", ZIndex = 1 };

        internal Entity Front { get; } = Drawn("Front", Vector2.Zero, 16f);

        internal Pile()
        {
            Camera.ViewportSize = new Vector2(320f, 180f);
            Camera.Center = new Vector2(160f, 90f);
            Add(Back);
            Add(Holder);
            Front.Parent = Holder;
        }

        private static Entity Drawn(string name, Vector2 position, float size)
        {
            Piece entity = new(position) { Name = name };
            entity.Add(new ColorRect(new Vector2(size, size)));

            return entity;
        }
    }

    // The entity under the pointer leaves and joins again, which numbers its name afresh, while the pointer
    // stays where it was.
    [Fact]
    public void ThePointerLine_FollowsTheNameAJoinGives()
    {
        Scene scene = new();
        Piece piece = new(Vector2.Zero);
        scene.Add(piece);
        using SimulationHost host = new(scene);
        OverlayScene overlay = new("`");
        overlay.SetPointer(Vector2.Zero, piece, (-1, -1));
        Assert.EndsWith("Piece", overlay.Readout()[2], StringComparison.Ordinal);

        scene.Remove(piece);
        host.Step();
        scene.Add(piece);
        host.Step();
        overlay.SetPointer(Vector2.Zero, piece, (-1, -1));

        Assert.EndsWith("Piece (1)", overlay.Readout()[2], StringComparison.Ordinal);
    }

    private sealed class Piece(Vector2 position) : Entity(position);
}
