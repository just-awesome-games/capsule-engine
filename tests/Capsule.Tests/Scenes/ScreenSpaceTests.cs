using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Rendering;

namespace Capsule.Tests.Scenes;

// An entity's type is what routes its renderers: a ScreenEntity is canvas pixels from an anchor on the
// layer drawn over the world, and a plain entity is world units under the camera.
public sealed class ScreenSpaceTests
{
    private static readonly Vector2 Canvas = new(100f, 50f);

    [Fact]
    public void ARun_OpensOnTheStandardCanvasUnlessItIsGivenOne()
    {
        using SceneRun standard = new(new Scene());
        using SceneRun declared = new(new Scene(), canvas: Canvas);

        Assert.Equal(SceneDefaults.StandardCanvas, standard.Scene.Canvas);
        Assert.Equal(Canvas, declared.Scene.Canvas);
    }

    [Fact]
    public void TheCanvas_TravelsOnTheFrameTheHostDraws()
    {
        using SceneRun run = new(new Scene(), canvas: Canvas);
        run.Step();

        Assert.Equal(Canvas, run.Simulation.View.Canvas);
    }

    [Fact]
    public void TheCanvas_IsInstalledBeforeAnythingStarts()
    {
        Vector2 seen = Vector2.Zero;
        Scene scene = new();
        scene.Add(new SceneFixtures.Starter(started => seen = started.Canvas));

        using SceneRun run = new(scene, canvas: Canvas);

        Assert.Equal(Canvas, seen);
    }

    [Fact]
    public void AWorldEntity_DrawsOnTheWorldList()
    {
        using SceneRun run = Run(new WorldHolder(new Vector2(4f, 5f)));
        run.Step();

        FrameView view = run.Simulation.View;

        Assert.Empty(view.ScreenSprites.ToArray());
        Assert.Equal(new Vector2(4f, 5f), Assert.Single(view.Sprites.ToArray()).Position);
    }

    [Fact]
    public void AScreenEntity_DrawsOnTheScreenListFromItsAnchor()
    {
        using SceneRun run = Run(new ScreenHolder(Anchor.BottomRight, new Vector2(-10f, -20f)));
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
        // Measured from the anchor, so a far-edge anchor is reached by a negative offset.
        Vector2 offset = x + y > 1f ? new Vector2(-20f, -20f) : new Vector2(10f, 10f);

        using SceneRun run = Run(new ScreenHolder(new Anchor(x, y), offset));
        run.Step();

        Assert.Equal(new Vector2(left, top), Assert.Single(run.Simulation.View.ScreenSprites.ToArray()).Position);
    }

    [Fact]
    public void AScreenEntity_InterpolatesItsPositionAsAWorldOneDoes()
    {
        Scene scene = new();
        scene.Add(new Mover());

        using SceneRun run = new(scene, canvas: Canvas);
        run.Run(2);

        SpriteIntent drawn = Assert.Single(run.Simulation.View.ScreenSprites.ToArray());

        Assert.Equal(new Vector2(2f, 0f), drawn.Position);
        Assert.Equal(new Vector2(1f, 0f), drawn.PreviousPosition);
    }

    [Fact]
    public void EveryRendererAScreenEntityHolds_FollowsItWithNoFlagOfItsOwn()
    {
        ScreenHolder holder = new(Anchor.TopLeft, new Vector2(1f, 2f));
        holder.Add(new ColorRect(new Vector2(4f, 4f)) { Offset = new Vector2(10f, 0f) });

        using SceneRun run = Run(holder);
        run.Step();

        Assert.Empty(run.Simulation.View.Sprites.ToArray());
        Assert.Equal(2, run.Simulation.View.ScreenSprites.Length);
    }

    [Fact]
    public void ABandedScreenEntity_StillDrawsOverEveryWorldEntity()
    {
        Scene scene = new();
        scene.Add(new WorldHolder(Vector2.Zero) { ZIndex = 100 });
        scene.Add(new ScreenHolder(Anchor.TopLeft, Vector2.Zero) { ZIndex = -100 });

        using SceneRun run = new(scene, canvas: Canvas);
        run.Step();

        // Two lists, never one ordering: the screen layer is drawn after the world whatever the bands
        // say, so a high world band cannot reach over it.
        Assert.Single(run.Simulation.View.Sprites.ToArray());
        Assert.Single(run.Simulation.View.ScreenSprites.ToArray());
    }

    [Fact]
    public void AnAnchorThatIsNotFinite_IsRefused()
    {
        ScreenHolder holder = new(Anchor.TopLeft, Vector2.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() => holder.Anchor = new Anchor(float.NaN, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenHolder(new Anchor(0f, float.NaN), Vector2.Zero));
    }

    private static SceneRun Run(Entity holder)
    {
        Scene scene = new();
        scene.Add(holder);

        return new SceneRun(scene, canvas: Canvas);
    }

    private sealed class WorldHolder : Entity
    {
        internal WorldHolder(Vector2 position)
            : base(position)
        {
            Add(new ColorRect(new Vector2(8f, 8f)));
        }
    }

    private sealed class ScreenHolder : ScreenEntity
    {
        internal ScreenHolder(Anchor anchor, Vector2 offset)
            : base(anchor, offset)
        {
            Add(new ColorRect(new Vector2(8f, 8f)));
        }
    }

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
