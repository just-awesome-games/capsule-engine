using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Rendering;

namespace Capsule.Tests.Scenes;

// An entity's space is what routes its renderers: the canvas the run settled, the anchor a screen
// position is measured from, and one layer drawn over the other.
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
    public void AWorldEntity_DrawsOnTheWorldListAndIgnoresItsAnchor()
    {
        using SceneRun run = Run(RenderSpace.World, Anchor.BottomRight, new Vector2(4f, 5f));
        run.Step();

        FrameView view = run.Simulation.View;

        Assert.Empty(view.ScreenSprites.ToArray());
        Assert.Equal(new Vector2(4f, 5f), Assert.Single(view.Sprites.ToArray()).Position);
    }

    [Fact]
    public void AScreenEntity_DrawsOnTheScreenListFromItsAnchor()
    {
        using SceneRun run = Run(RenderSpace.Screen, Anchor.BottomRight, new Vector2(-10f, -20f));
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
        // Measured from the anchor, so a far-edge anchor is reached by a negative position.
        Vector2 position = x + y > 1f ? new Vector2(-20f, -20f) : new Vector2(10f, 10f);

        using SceneRun run = Run(RenderSpace.Screen, new Anchor(x, y), position);
        run.Step();

        Assert.Equal(new Vector2(left, top), Assert.Single(run.Simulation.View.ScreenSprites.ToArray()).Position);
    }

    [Fact]
    public void AScreenEntity_InterpolatesItsPositionAsAWorldOneDoes()
    {
        Mover mover = new(RenderSpace.Screen);
        Scene scene = new();
        scene.Add(mover);

        using SceneRun run = new(scene, canvas: Canvas);
        run.Run(2);

        SpriteIntent drawn = Assert.Single(run.Simulation.View.ScreenSprites.ToArray());

        Assert.Equal(new Vector2(2f, 0f), drawn.Position);
        Assert.Equal(new Vector2(1f, 0f), drawn.PreviousPosition);
    }

    [Fact]
    public void AnEntityThatMovesToTheScreen_TakesEveryRendererItHoldsWithIt()
    {
        Holder holder = new(RenderSpace.World, Anchor.TopLeft, new Vector2(1f, 2f));
        Scene scene = new();
        scene.Add(holder);

        using SceneRun run = new(scene, canvas: Canvas);
        run.Step();
        Assert.Single(run.Simulation.View.Sprites.ToArray());

        holder.Space = RenderSpace.Screen;
        run.Step();

        Assert.Empty(run.Simulation.View.Sprites.ToArray());
        Assert.Single(run.Simulation.View.ScreenSprites.ToArray());
    }

    [Fact]
    public void ABandedScreenEntity_StillDrawsOverEveryWorldEntity()
    {
        Holder world = new(RenderSpace.World, Anchor.TopLeft, Vector2.Zero) { ZIndex = 100 };
        Holder screen = new(RenderSpace.Screen, Anchor.TopLeft, Vector2.Zero) { ZIndex = -100 };

        Scene scene = new();
        scene.Add(world);
        scene.Add(screen);

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
        Holder holder = new(RenderSpace.Screen, Anchor.TopLeft, Vector2.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() => holder.Anchor = new Anchor(float.NaN, 0f));
    }

    private static SceneRun Run(RenderSpace space, Anchor anchor, Vector2 position)
    {
        Scene scene = new();
        scene.Add(new Holder(space, anchor, position));

        return new SceneRun(scene, canvas: Canvas);
    }

    private sealed class Holder : Entity
    {
        internal Holder(RenderSpace space, Anchor anchor, Vector2 position)
            : base(position)
        {
            Space = space;
            Anchor = anchor;
            Add(new ColorRect(new Vector2(8f, 8f)));
        }
    }

    private sealed class Mover(RenderSpace space) : Entity(Vector2.Zero)
    {
        private bool _added;

        protected internal override void OnStep(in StepContext context)
        {
            if (!_added)
            {
                _added = true;
                Space = space;
                Add(new ColorRect(new Vector2(8f, 8f)));
            }

            Position += Vector2.UnitX;
        }
    }
}
