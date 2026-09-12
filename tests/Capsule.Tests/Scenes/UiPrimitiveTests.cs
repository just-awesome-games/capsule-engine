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
    public void AFilledRect_LandsItsCornerOnThePositionPlusItsOffset()
    {
        ColorRect rect = new(new Vector2(30f, 10f)) { Offset = new Vector2(2f, 3f) };

        using SceneRun run = Run(rect);
        run.Step();

        // The holder sits at (4, 5).
        Assert.Equal(new Rect(6f, 8f, 36f, 18f), rect.Bounds);
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
        Assert.Equal(new Rect(4f, 5f, 44f, 35f), panel.Bounds);
    }

    [Fact]
    public void APrimitiveOnAScreenEntity_DrawsOnTheScreenLayerFromItsAnchor()
    {
        ColorRect rect = new(new Vector2(30f, 10f));
        ScreenEntity holder = new(Anchor.Center, new Vector2(4f, 5f));
        holder.Add(rect);

        Scene scene = new();
        scene.Add(holder);

        using SceneRun run = new(scene, canvas: new Vector2(100f, 50f));
        run.Step();

        // Half the canvas, then the entity's own (4, 5).
        Assert.Equal(new Rect(54f, 30f, 84f, 40f), rect.Bounds);
        Assert.Equal(new Vector2(54f, 30f), Assert.Single(run.Simulation.View.ScreenSprites.ToArray()).Position);
    }

    // What a game's own renderer is handed: the position already resolved in the space its entity draws
    // in, so a custom renderer lands on the screen layer from its anchor without arithmetic of its own.
    [Fact]
    public void ACustomRenderer_ReadsItsResolvedPositionsInItsEntitysSpace()
    {
        Probe probe = new();
        Scene scene = new();
        scene.Add(new Drifter(probe));

        using SceneRun run = new(scene, canvas: new Vector2(100f, 50f));
        run.Run(2);

        // Two steps from the canvas's centre, which the anchor resolved to (50, 25).
        Assert.Equal(new Vector2(52f, 25f), probe.Current);
        Assert.Equal(new Vector2(51f, 25f), probe.Previous);
        Assert.Equal(new Rect(52f, 25f, 54f, 27f), probe.Bounds);

        SpriteIntent drawn = Assert.Single(run.Simulation.View.ScreenSprites.ToArray());

        Assert.Equal(probe.Current, drawn.Position);
        Assert.Equal(probe.Previous, drawn.PreviousPosition);
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

    private sealed class Drifter : ScreenEntity
    {
        internal Drifter(Component drawn)
            : base(Anchor.Center, Vector2.Zero)
        {
            Add(drawn);
        }

        protected internal override void OnStep(in StepContext context) => Position += Vector2.UnitX;
    }

    // A renderer of the kind a game writes itself: it places its intent from the resolved positions
    // rather than reaching for its entity's space origin.
    private sealed class Probe : Renderer
    {
        internal Vector2 Current { get; private set; }

        internal Vector2 Previous { get; private set; }

        public override Rect Bounds => new(RenderPosition, new Vector2(2f, 2f));

        public override void Draw(FrameView view)
        {
            ArgumentNullException.ThrowIfNull(view);

            Current = RenderPosition;
            Previous = PreviousRenderPosition;

            view.Add(new SpriteIntent(
                Sprite.White,
                Previous,
                Current,
                new Vector2(2f, 2f),
                FlipX: false,
                FlipY: false,
                ColorRgba.White));
        }
    }
}
