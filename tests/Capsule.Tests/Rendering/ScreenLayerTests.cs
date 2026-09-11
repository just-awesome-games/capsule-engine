using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

// A frame carries two ordered lists: the world the camera culls, and the screen layer the canvas
// culls, which draws over the whole of the world whatever either one holds.
public sealed class ScreenLayerTests
{
    [Fact]
    public void ScreenIntent_LandsOnItsOwnListInTheOrderAdded()
    {
        FrameView view = Canvas(100f, 100f);

        view.Add(Quad(new Vector2(1f, 1f)), RenderSpace.World);
        view.Add(Quad(new Vector2(2f, 2f)), RenderSpace.Screen);
        view.Add(Quad(new Vector2(3f, 3f)), RenderSpace.Screen);

        Assert.Equal(new Vector2(1f, 1f), Assert.Single(view.Sprites.ToArray()).Position);
        Assert.Equal([new Vector2(2f, 2f), new Vector2(3f, 3f)], view.ScreenSprites.ToArray().Select(sprite => sprite.Position));
    }

    [Fact]
    public void TheSpaceTheFrameIsSetTo_IsWhereAnUnqualifiedAddLands()
    {
        FrameView view = Canvas(100f, 100f);
        view.Space = RenderSpace.Screen;

        view.Add(Quad(Vector2.Zero));
        view.Add(Text());

        // The rect, then one sprite per glyph of the run.
        Assert.Empty(view.Sprites.ToArray());
        Assert.Equal(4, view.ScreenSprites.Length);
    }

    [Fact]
    public void ScreenIntent_IsCulledAgainstTheCanvasRatherThanTheCamera()
    {
        // A camera looking at nothing near the canvas: what the screen layer is culled against is the
        // canvas alone.
        FrameView view = Canvas(10f, 10f);
        view.Camera = new CameraView(new Vector2(500f, 500f), new Vector2(4f, 4f));

        view.Add(Quad(new Vector2(1f, 1f)), RenderSpace.Screen);
        view.Add(Quad(new Vector2(40f, 1f)), RenderSpace.Screen);
        view.Add(Quad(new Vector2(-8f, 1f)), RenderSpace.Screen);

        Assert.Equal(new Vector2(1f, 1f), Assert.Single(view.ScreenSprites.ToArray()).Position);
        Assert.Equal(new RenderMetrics(Submitted: 3, Visible: 1), view.Metrics);
    }

    [Fact]
    public void ACulledScreenSprite_IsOneThatSweepsClearOfTheCanvasAcrossTheWholeStep()
    {
        FrameView view = Canvas(10f, 10f);

        // Off the canvas at both ends, but across it in between.
        view.Add(Quad(new Vector2(-20f, 1f)) with { Position = new Vector2(30f, 1f) }, RenderSpace.Screen);

        Assert.Single(view.ScreenSprites.ToArray());
    }

    [Fact]
    public void ANonPositiveCanvas_CullsNothing()
    {
        FrameView view = new();

        view.Add(Quad(new Vector2(-500f, -500f)), RenderSpace.Screen);

        Assert.Single(view.ScreenSprites.ToArray());
    }

    [Fact]
    public void Metrics_CountBothLists()
    {
        FrameView view = Canvas(100f, 100f);

        view.Add(Quad(Vector2.Zero), RenderSpace.World);
        view.Add(Quad(Vector2.Zero), RenderSpace.Screen);

        Assert.Equal(new RenderMetrics(Submitted: 2, Visible: 2), view.Metrics);
    }

    [Fact]
    public void ARewrittenFrame_DropsBothListsAndReturnsToWorldSpace()
    {
        FrameView view = Canvas(100f, 100f);
        view.Space = RenderSpace.Screen;
        view.Add(Quad(Vector2.Zero));

        view.Clear();

        Assert.Empty(view.ScreenSprites.ToArray());
        Assert.Equal(RenderSpace.World, view.Space);
        Assert.Equal(new RenderMetrics(Submitted: 0, Visible: 0), view.Metrics);
    }

    [Fact]
    public void TheEngineWhiteTexel_IsReservedAndPreloadsNothing()
    {
        AssetCollection assets = new();

        assets.Add(TextureHandle.White);
        assets.Add(Sprite.White.Texture);

        Assert.Empty(assets.Textures);
        Assert.Equal(new TextureRegion(0, 0, 1, 1), Sprite.White.Region);
        Assert.NotEqual(default, TextureHandle.White);
    }

    private static FrameView Canvas(float width, float height) => new() { Canvas = new Vector2(width, height) };

    private static SpriteIntent Quad(Vector2 position) =>
        new(Sprite.White, position, position, new Vector2(4f, 4f), FlipX: false, FlipY: false, ColorRgba.White);

    private static TextIntent Text() =>
        new(FontFixtures.Font(), "AB\nA", Vector2.Zero, Vector2.Zero, Vector2.One, ColorRgba.White);
}
