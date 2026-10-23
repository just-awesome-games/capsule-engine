using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.UI;
using static Capsule.Tests.UI.FocusFixtures;

namespace Capsule.Tests.UI;

// An entity's type is what routes its renderers: a ScreenEntity is canvas pixels from an anchor on the
// layer drawn over the world, or in the world's order when its root sorts with it, and a plain entity is
// world units under the camera.
public sealed class ScreenSpaceTests
{
    private static readonly Vector2 Canvas = new(100f, 50f);

    [Fact]
    public void TheCanvas_IsInstalledBeforeAnythingStarts()
    {
        Vector2 seen = Vector2.Zero;
        Scene scene = new();
        scene.Add(new SceneFixtures.Starter(started => seen = started.Run.Canvas));

        using SimulationHost run = new(scene, run: new Run { Canvas = Canvas });

        Assert.Equal(Canvas, seen);
    }

    [Fact]
    public void AWorldEntity_DrawsOnTheWorldList()
    {
        using SimulationHost run = Run(new SceneFixtures.Holder(new Vector2(4f, 5f), Square()));
        run.Step();

        FrameView view = run.Simulation.View;

        Assert.Empty(view.ScreenSprites.ToArray());
        Assert.Equal(new Vector2(4f, 5f), Assert.Single(view.Sprites.ToArray()).Position);
    }

    [Fact]
    public void AScreenEntity_DrawsOnTheScreenListFromItsAnchor()
    {
        using SimulationHost run = Run(new ScreenHolder(Anchor.BottomRight, new Vector2(-10f, -20f), Square()));
        run.Step();

        FrameView view = run.Simulation.View;

        Assert.Empty(view.Sprites.ToArray());
        Assert.Equal(new Vector2(90f, 30f), Assert.Single(view.ScreenSprites.ToArray()).Position);
    }

    [Theory]
    [InlineData(0f, 0f, 10f, 10f)]
    [InlineData(0.5f, 0.5f, 60f, 35f)]
    [InlineData(1f, 1f, 80f, 30f)]
    public void EveryAnchor_IsThatFractionOfTheCanvas(float x, float y, float left, float top)
    {
        // Measured from the anchor. A far-edge anchor is reached by a negative offset.
        Vector2 offset = x + y > 1f ? new Vector2(-20f, -20f) : new Vector2(10f, 10f);

        using SimulationHost run = Run(new ScreenHolder(new Anchor(x, y), offset, Square()));
        run.Step();

        Assert.Equal(new Vector2(left, top), Assert.Single(run.Simulation.View.ScreenSprites.ToArray()).Position);
    }

    // A UI-scale option moves the run's canvas mid-run. Nothing caches the old one. The next step
    // anchors every screen entity against the new extent.
    [Fact]
    public void ACanvasSetDuringTheRun_IsWhatAScreenEntityAnchorsAgainstOnTheNextStep()
    {
        using SimulationHost run = Run(new ScreenHolder(Anchor.BottomRight, new Vector2(-10f, -20f), Square()));
        run.Step();
        Assert.Equal(new Vector2(90f, 30f), Assert.Single(run.Simulation.View.ScreenSprites.ToArray()).Position);

        run.Simulation.Run.Canvas = new Vector2(200f, 100f);
        run.Step();

        Assert.Equal(new Vector2(190f, 80f), Assert.Single(run.Simulation.View.ScreenSprites.ToArray()).Position);
    }

    // Anchors resolve on every read. A nested element follows a new canvas and a resized parent.
    [Fact]
    public void ANestedEntity_TracksACanvasResizeAndAParentResize()
    {
        ScreenEntity panel = new(Anchor.Center, Vector2.Zero) { Size = new Vector2(40f, 20f) };
        _ = new ScreenHolder(Anchor.BottomRight, new Vector2(-2f, -3f), Square()) { Parent = panel, Size = new Vector2(4f, 4f) };

        using SimulationHost run = Run(panel);
        run.Step();

        // The child's bottom-right corner sits (-2, -3) from the panel's, which spans (30, 15) to (70, 35).
        Assert.Equal(new Vector2(64f, 28f), Assert.Single(run.Simulation.View.ScreenSprites.ToArray()).Position);

        run.Simulation.Run.Canvas = new Vector2(200f, 100f);
        run.Step();

        Assert.Equal(new Vector2(114f, 53f), Assert.Single(run.Simulation.View.ScreenSprites.ToArray()).Position);

        panel.Size = new Vector2(60f, 30f);
        run.Step();

        Assert.Equal(new Vector2(124f, 58f), Assert.Single(run.Simulation.View.ScreenSprites.ToArray()).Position);
    }

    [Fact]
    public void ASpanAnchor_StretchesOverItsShareOfTheParentsPaddedRect()
    {
        ScreenEntity root = new(Anchor.Fill, Vector2.Zero) { Padding = new Insets(10f, 5f, 20f, 15f) };
        ScreenEntity bar = new(new Anchor(0.5f, 1f, 1f, 1f), new Vector2(1f, -2f)) { Parent = root, Size = new Vector2(999f, 4f) };
        ColorRect rect = new();
        bar.Add(rect);

        using SimulationHost run = Run(root);
        run.Step();

        // The inside is (10, 5) to (80, 35). X spans its right half and ignores the size, and Y is a point on its bottom edge.
        Assert.Equal(Rect.FromEdges(46f, 29f, 81f, 33f), rect.Bounds);
    }

    // Nested, the parent's uneven scale stretches the anchor point but never shears the turned rect.
    [Theory]
    [InlineData(false, 50f, 25f)]
    [InlineData(true, 100f, 25f)]
    public void ATurnedPointAnchoredEntity_TurnsItsRectAboutTheAnchoredPoint(bool nested, float x, float y)
    {
        ScreenEntity turned = new(Anchor.Center, Vector2.Zero) { Size = new Vector2(40f, 20f), Rotation = MathF.PI / 2f };
        turned.Add(new ColorRect());
        ScreenEntity root = turned;
        if (nested)
        {
            root = new ScreenEntity(Anchor.TopLeft, Vector2.Zero) { Size = new Vector2(100f, 50f), Scale = new Vector2(2f, 1f) };
            turned.Parent = root;
        }

        using SimulationHost run = Run(root);
        run.Step();

        SpriteIntent drawn = Assert.Single(run.Simulation.View.ScreenSprites.ToArray());
        Vector2 centre = drawn.Position + Vector2.Transform(drawn.Size / 2f, Matrix3x2.CreateRotation(drawn.Rotation));

        Assert.Equal(x, centre.X, 3);
        Assert.Equal(y, centre.Y, 3);
    }

    [Fact]
    public void AScreenEntity_InterpolatesItsPositionAsAWorldOneDoes()
    {
        Scene scene = new();
        scene.Add(new Mover());

        using SimulationHost run = new(scene, run: new Run { Canvas = Canvas });
        run.Step(2);

        SpriteIntent drawn = Assert.Single(run.Simulation.View.ScreenSprites.ToArray());

        Assert.Equal(new Vector2(2f, 0f), drawn.Position);
        Assert.Equal(new Vector2(1f, 0f), drawn.PreviousPosition);
    }

    [Fact]
    public void EveryRendererAScreenEntityHolds_FollowsItWithNoFlagOfItsOwn()
    {
        ScreenHolder holder = new(Anchor.TopLeft, new Vector2(1f, 2f), Square());
        holder.Add(new ColorRect(new Vector2(4f, 4f)) { Offset = new Vector2(10f, 0f) });

        using SimulationHost run = Run(holder);
        run.Step();

        Assert.Empty(run.Simulation.View.Sprites.ToArray());
        Assert.Equal(2, run.Simulation.View.ScreenSprites.Length);
    }

    [Fact]
    public void ABandedScreenEntity_StillDrawsOverEveryWorldEntity()
    {
        Scene scene = new();
        scene.Add(new SceneFixtures.Holder(Vector2.Zero, Square()) { ZIndex = 100 });
        scene.Add(new ScreenHolder(Anchor.TopLeft, Vector2.Zero, Square()) { ZIndex = -100 });

        using SimulationHost run = new(scene, run: new Run { Canvas = Canvas });
        run.Step();

        // Two lists, never one ordering: the screen layer is drawn after the world whatever the bands
        // say. A high world band cannot reach over it.
        Assert.Single(run.Simulation.View.Sprites.ToArray());
        Assert.Single(run.Simulation.View.ScreenSprites.ToArray());
    }

    // Y-sorting puts the band's world renderer at the lowest Y first, but a sorted root has no world Y and
    // draws before every one of them.
    [Fact]
    public void AScreenRootSortedWithTheWorld_DrawsAtItsBandAmongTheWorldsRenderers_FirstInAYSortedBand()
    {
        Scene scene = new() { YSort = true };
        scene.Add(new SceneFixtures.Holder(new Vector2(4f, 0f), Square()) { ZIndex = 0 });
        scene.Add(new SceneFixtures.Holder(new Vector2(0f, -1000f), Square()) { ZIndex = -5 });
        scene.Add(new ScreenHolder(Anchor.TopLeft, new Vector2(1f, 2f), Square()) { SortsWithWorld = true, ZIndex = -5 });
        scene.Add(new SceneFixtures.Holder(Vector2.Zero, Square()) { ZIndex = -10 });

        using SimulationHost run = new(scene, run: new Run { Canvas = Canvas });
        run.Step();

        Assert.Empty(run.Simulation.View.ScreenSprites.ToArray());
        Assert.Equal(
            [Vector2.Zero, new Vector2(1f, 2f), new Vector2(0f, -1000f), new Vector2(4f, 0f)],
            run.Simulation.View.Sprites.ToArray().Select(sprite => sprite.Position));
    }

    [Fact]
    public void SortingWithTheWorld_IsARootsAlone()
    {
        ScreenEntity root = new(Anchor.TopLeft, Vector2.Zero);
        ScreenEntity child = new(Anchor.TopLeft, Vector2.Zero) { Parent = root };
        ScreenEntity sorted = new(Anchor.TopLeft, Vector2.Zero) { SortsWithWorld = true };

        Assert.Throws<InvalidOperationException>(() => child.SortsWithWorld = true);
        Assert.Throws<InvalidOperationException>(() => sorted.Parent = root);
    }

    [Fact]
    public void AnAnchorThatIsNotFiniteOrSpansBackwards_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Anchor(float.NaN, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Anchor(0f, 0f, 1f, float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Anchor(1f, 0f, 0f, 1f));
    }

    private static SimulationHost Run(Entity holder) => SceneFixtures.Simulate(new Run { Canvas = Canvas }, holder);

    private static ColorRect Square() => new(new Vector2(8f, 8f));

    private sealed class Mover() : ScreenEntity(Anchor.TopLeft, Vector2.Zero)
    {
        private bool _added;

        protected internal override void OnStep(in StepContext context)
        {
            if (!_added)
            {
                _added = true;
                Add(new ColorRect(new Vector2(8f, 8f)));
            }

            Position += Vector2.UnitX;
        }
    }
}
