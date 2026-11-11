using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Scenes;
using static Capsule.Tests.Scenes.EntityHierarchyFixtures;

namespace Capsule.Tests.Scenes;

// A sprite standing still out of view goes to rest, and a frame leaves it out without asking it to draw.
// Every frame these tests build also draws what it left out and throws where that would have drawn.
public sealed class RendererRestTests
{
    // A frame 200 wide whose pivot is its top-left corner. At Out it covers 400 to 600, right of a view that
    // ends at 320.
    private static readonly Sprite Wide = SceneFixtures.Frame(200, 8);
    private static readonly Vector2 Out = new(400f, 90f);

    [Fact]
    public void ASpriteStandingStillOutOfView_RestsAndIsStillCountedAsSubmitted()
    {
        using SimulationHost run = Rested(out Fixture fixture);

        run.Step();
        FrameView view = run.Simulation.View;

        Assert.True(fixture.Renderer.Resting);
        Assert.Equal([1], Order(view.Sprites));
        Assert.Equal(2, view.Metrics.Submitted);
    }

    [Fact]
    public void ACameraReachingASpriteAtRest_DrawsItAndLeavesItAtRest()
    {
        using SimulationHost run = Rested(out Fixture fixture);

        run.Scene.Camera.Teleport(new Vector2(500f, 90f));
        run.Step();

        Assert.Equal([400], Order(run.Simulation.View.Sprites));
        Assert.True(fixture.Renderer.Resting);
    }

    [Fact]
    public void AFrameOfTheSameSizeAndPivot_LeavesASpriteAtRest()
    {
        using SimulationHost run = Rested(out Fixture fixture);

        fixture.Renderer.Sprite = Wide with { Region = new TextureRegion(8, 8, 200, 8) };
        Assert.True(fixture.Renderer.Resting);

        fixture.Renderer.Sprite = SceneFixtures.Frame(100, 8);
        Assert.False(fixture.Renderer.Resting);
    }

    [Fact]
    public void ASpriteMovingOutOfView_DoesNotRestUntilItStandsStill()
    {
        using SimulationHost run = Rested(out Fixture fixture);
        fixture.Entity.Add(new Mover());

        run.Step();
        _ = run.Simulation.View;
        Assert.False(fixture.Renderer.Resting);

        fixture.Entity.Remove(fixture.Entity.Get<Mover>());
        for (int frame = 0; frame < 3; frame++)
        {
            run.Step();
            _ = run.Simulation.View;
        }

        Assert.True(fixture.Renderer.Resting);
    }

    // Each write changes what the sprite at rest draws or whether it draws. The frame after it is the frame of
    // a scene that made the same write with nothing at rest.
    [Theory]
    [InlineData("moved")]
    [InlineData("teleported")]
    [InlineData("parent moved")]
    [InlineData("parented under a hidden entity")]
    [InlineData("mirrored by scale")]
    [InlineData("offset")]
    [InlineData("flipped")]
    [InlineData("frame swapped")]
    [InlineData("variant set")]
    [InlineData("tiled")]
    [InlineData("hidden")]
    [InlineData("parent hidden")]
    [InlineData("faded out")]
    [InlineData("renderer hidden")]
    [InlineData("detached")]
    [InlineData("removed and added back")]
    [InlineData("scrolled")]
    public void AWriteToASpriteAtRest_DrawsTheFrameASceneWithNothingAtRestDraws(string write)
    {
        using SimulationHost rested = Rested(out Fixture resting);
        using SimulationHost fresh = Built(out Fixture awake);
        fresh.Step();
        fresh.Step();
        Assert.False(awake.Renderer.Resting);

        Write(write, resting, rested.Scene);
        Write(write, awake, fresh.Scene);
        rested.Step();
        fresh.Step();

        Assert.Equal(fresh.Simulation.View.Sprites.ToArray(), rested.Simulation.View.Sprites.ToArray());
        Assert.Equal(fresh.Simulation.View.Metrics, rested.Simulation.View.Metrics);
    }

    private static void Write(string write, Fixture fixture, Scene scene)
    {
        (Node parent, Node entity, SpriteRenderer renderer) = (fixture.Parent, fixture.Entity, fixture.Renderer);
        Vector2 into = new(-300f, 0f);
        switch (write)
        {
            case "moved":
                entity.Position += into;
                break;
            case "teleported":
                entity.Teleport(entity.Position + into);
                break;
            case "parent moved":
                parent.Position = into;
                break;
            case "parented under a hidden entity":
                Node other = new(Vector2.Zero) { Visible = false };
                scene.Add(other);
                entity.Parent = other;
                break;
            case "mirrored by scale":
                entity.Scale = new Vector2(-1f, 1f);
                break;
            case "offset":
                renderer.Offset = into;
                break;
            case "flipped":
                renderer.FlipX = true;
                break;
            case "frame swapped":
                renderer.Sprite = Wide with { Pivot = new Vector2(200f, 0f) };
                break;
            case "variant set":
                renderer.Variant = new SpriteVariant([Wide], [Wide with { Pivot = new Vector2(200f, 0f) }]);
                break;
            case "tiled":
                renderer.Tiling = new Vector2(float.PositiveInfinity, 0f);
                break;
            case "hidden":
                entity.Visible = false;
                break;
            case "parent hidden":
                parent.Visible = false;
                break;
            case "faded out":
                parent.Tint = ColorRgba.White with { A = 0 };
                break;
            case "renderer hidden":
                renderer.Visible = false;
                break;
            case "detached":
                entity.Remove(renderer);
                break;
            case "removed and added back":
                scene.Remove(parent);
                parent.Teleport(into);
                scene.Add(parent);
                break;
            case "scrolled":
                parent.ScrollFactor = new Vector2(2f);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(write), write, "Unknown write.");
        }
    }

    // A scene whose view spans 320 by 180 from the origin, holding a tagged sprite in view and a wide one out
    // of view under a parent at the origin. It has stepped and no frame of it is built.
    private static SimulationHost Built(out Fixture fixture)
    {
        Scene scene = new SceneFixtures.HookScene(SceneFixtures.Opens(SceneFixtures.Viewport / 2f));
        Node shown = new(new Vector2(0f, 90f));
        shown.Add(Tag(1));
        scene.Add(shown);

        Node parent = new(Vector2.Zero);
        Node entity = new(Out) { Parent = parent };
        SpriteRenderer renderer = new(Wide);
        entity.Add(renderer);
        scene.Add(parent);
        fixture = new Fixture(parent, entity, renderer);

        SimulationHost run = new(scene);
        run.Step();

        return run;
    }

    // The same scene after two frames of it: one dropped the wide sprite whole, and the next put it to rest.
    private static SimulationHost Rested(out Fixture fixture)
    {
        SimulationHost run = Built(out fixture);
        Assert.Equal([1], Order(run.Simulation.View.Sprites));
        run.Step();
        Assert.Equal([1], Order(run.Simulation.View.Sprites));
        Assert.True(fixture.Renderer.Resting);

        return run;
    }

    private sealed record Fixture(Node Parent, Node Entity, SpriteRenderer Renderer);
}
