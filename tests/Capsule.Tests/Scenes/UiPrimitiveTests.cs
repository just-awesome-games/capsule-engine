using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Rendering;

namespace Capsule.Tests.Scenes;

// The two drawn primitives an interface is built from: a filled rect and a nine-sliced panel, each in
// whichever space its entity lives in.
public sealed class UiPrimitiveTests
{
    private static readonly Sprite Panel = new(SceneFixtures.Atlas, new TextureRegion(0, 0, 12, 12));

    [Fact]
    public void AFilledRect_IsOneSpriteOverTheEngineWhiteTexel()
    {
        using SceneRun run = Run(new ColorRect(new Vector2(30f, 10f)) { Color = ColorRgba.Black });
        run.Step();

        SpriteIntent drawn = Assert.Single(run.Simulation.View.Sprites.ToArray());

        Assert.Equal(Sprite.White, drawn.Sprite);
        Assert.Equal(new Vector2(30f, 10f), drawn.Size);
        Assert.Equal(ColorRgba.Black, drawn.Color);
    }

    [Fact]
    public void AFilledRect_PreloadsNothing()
    {
        Scene scene = new();
        scene.Add(new Holder(new ColorRect(new Vector2(30f, 10f))));

        Assert.Empty(scene.CollectAssetPreloads().Textures);
    }

    [Fact]
    public void AFilledRect_LandsItsCornerOnThePositionPlusItsOffset()
    {
        ColorRect rect = new(new Vector2(30f, 10f)) { Offset = new Vector2(2f, 3f) };

        using SceneRun run = Run(rect);
        run.Step();

        // The holder sits at (4, 5).
        Assert.Equal(new ViewBounds(6f, 8f, 36f, 18f), rect.Bounds);
        Assert.Equal(new Vector2(6f, 8f), Assert.Single(run.Simulation.View.Sprites.ToArray()).Position);
    }

    [Fact]
    public void ANineSlicedPanel_PreloadsItsOwnTextureAndSlicesOnDraw()
    {
        NineSlice panel = new(Panel, new SliceInsets(3), new Vector2(40f, 30f));
        Scene scene = new();
        scene.Add(new Holder(panel));

        Assert.Equal([SceneFixtures.Atlas], scene.CollectAssetPreloads().Textures);

        using SceneRun run = new(scene);
        run.Step();

        Assert.Equal(9, run.Simulation.View.Sprites.Length);
        Assert.Equal(new ViewBounds(4f, 5f, 44f, 35f), panel.Bounds);
    }

    [Fact]
    public void APrimitiveOnAScreenEntity_DrawsOnTheScreenLayerFromItsAnchor()
    {
        ColorRect rect = new(new Vector2(30f, 10f));
        Holder holder = new(rect) { Space = RenderSpace.Screen, Anchor = Anchor.Center };

        Scene scene = new();
        scene.Add(holder);

        using SceneRun run = new(scene, canvas: new Vector2(100f, 50f));
        run.Step();

        // Half the canvas, then the entity's own (4, 5).
        Assert.Equal(new ViewBounds(54f, 30f, 84f, 40f), rect.Bounds);
        Assert.Equal(new Vector2(54f, 30f), Assert.Single(run.Simulation.View.ScreenSprites.ToArray()).Position);
    }

    [Fact]
    public void APrimitiveOnNoEntity_OccupiesNoRect()
    {
        Assert.True(new ColorRect(new Vector2(30f, 10f)).Bounds.IsEmpty);
        Assert.True(new NineSlice(Panel, new SliceInsets(3), new Vector2(40f, 30f)).Bounds.IsEmpty);
    }

    private static SceneRun Run(Component primitive)
    {
        Scene scene = new();
        scene.Add(new Holder(primitive));

        return new SceneRun(scene);
    }

    private sealed class Holder : Entity
    {
        internal Holder(Component drawn)
            : base(new Vector2(4f, 5f))
        {
            Add(drawn);
        }
    }
}
