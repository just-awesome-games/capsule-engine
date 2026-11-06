using System.Numerics;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Rendering;

// A frame's flip mirrors the texels it reads. Its pivot and marks are measured on the frame as it draws, and
// they do not move. A flip on whatever draws it mirrors it again about the pivot.
public sealed class FlippedFrameTests
{
    private static readonly TextureHandle Atlas = new("atlas", ".png");

    // 8 texels square, its pivot 2 in from the left and 3 down.
    private static readonly Sprite Upright = new(Atlas, new TextureRegion(16, 0, 8, 8), new Vector2(2f, 3f));

    // Each axis on its own: the frame's flip reaches the texture coordinates, and an intent's flip over it cancels.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AFramesFlip_ReachesTheDrawnTexels_AndAnIntentsFlipCancelsIt(bool frameX, bool frameY)
    {
        SpriteIntent intent = At(Upright with { FlipX = frameX, FlipY = frameY }, Vector2.Zero);
        SpriteIntent flipped = intent with { FlipX = true, FlipY = true };
        SpriteFlags expected = (frameX ? SpriteFlags.FlipX : SpriteFlags.None) | (frameY ? SpriteFlags.FlipY : SpriteFlags.None);

        Assert.Equal(expected, SpriteDrawList.FlagsOf(intent));
        Assert.Equal(expected ^ (SpriteFlags.FlipX | SpriteFlags.FlipY), SpriteDrawList.FlagsOf(flipped));
    }

    // Only the intent's flip swings the frame about its pivot, whichever way the frame's texels read.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyAnIntentsFlip_MovesTheDrawnRect(bool flippedFrame)
    {
        SpriteIntent intent = At(Upright with { FlipX = flippedFrame }, new Vector2(20f, 20f));

        Assert.Equal(new Vector2(2f, 3f), intent.DrawOrigin);
        Assert.Equal(new Vector2(6f, 3f), (intent with { FlipX = true }).DrawOrigin);
        Assert.True(intent.TryGetSweptBounds(out Rect bounds));
        Assert.Equal(Rect.FromEdges(18f, 17f, 26f, 25f), bounds);
    }

    // A flipped frame tiled reads its last, cropped copy from the region's far side, as the frame draws it.
    [Fact]
    public void AFlippedFrameTiled_CropsTheSideItsMirrorPutsAtTheFarEnd()
    {
        FrameView view = new();

        view.Add(At(Upright with { Pivot = Vector2.Zero, FlipX = true }, Vector2.Zero), new Vector2(12f, 0f));

        ReadOnlySpan<SpriteIntent> copies = view.Sprites;
        Assert.Equal(2, copies.Length);
        Assert.True(copies[0].MirrorsX);
        Assert.Equal(new TextureRegion(16, 0, 8, 8), copies[0].Sprite.Region);
        Assert.True(copies[1].MirrorsX);
        Assert.Equal(new TextureRegion(20, 0, 4, 8), copies[1].Sprite.Region);
        Assert.True(copies[1].TryGetSweptBounds(out Rect last));
        Assert.Equal(Rect.FromEdges(8f, 0f, 12f, 8f), last);
    }

    // A panel's insets cut the frame as it draws: its left corner comes from the region's right, mirrored.
    [Fact]
    public void AFlippedPanel_CutsItsNearSlicesFromTheRegionsFarSide()
    {
        Sprite frame = new(Atlas, new TextureRegion(10, 20, 12, 12), FlipX: true);
        FrameView view = new();

        view.Add(new NineSliceIntent(frame, new SliceInsets(2, 3, 4, 3), Vector2.Zero, Vector2.Zero, new Vector2(40f, 30f), ColorRgba.White));

        ReadOnlySpan<SpriteIntent> slices = view.Sprites;
        Assert.Equal(new TextureRegion(20, 20, 2, 3), slices[0].Sprite.Region);
        Assert.Equal(Vector2.Zero, slices[0].Position);
        Assert.Equal(new TextureRegion(14, 20, 6, 3), slices[1].Sprite.Region);
        Assert.Equal(new TextureRegion(10, 20, 4, 3), slices[2].Sprite.Region);
        Assert.Equal(new Vector2(36f, 0f), slices[2].Position);
        Assert.True(slices[0].MirrorsX);
        Assert.True(slices[0].TryGetSweptBounds(out Rect corner));
        Assert.Equal(Rect.FromEdges(0f, 0f, 2f, 3f), corner);
    }

    // Marks are where the frame's author measured them: the frame's flip leaves them, and a renderer's mirrors them.
    [Fact]
    public void AFlippedFramesMarks_StandAsWritten_AndARenderersFlipMirrorsThem()
    {
        SpriteMarks marks = new(
            sockets: [new SpriteSocket("muzzle", new Vector2(7f, 4f))],
            boxes: [new SpriteBox("hurt", Rect.FromEdges(1f, 2f, 3f, 6f))]);
        Entity body = new EntityHierarchyFixtures.Node(new Vector2(100f, 50f));
        SpriteRenderer renderer = new(Upright with { Marks = marks, FlipX = true });
        body.Add(renderer);
        Entity muzzle = renderer.Socket("muzzle");
        BoxCollider2D box = renderer.Box("hurt");

        Assert.Equal(new Vector2(5f, 1f), muzzle.Position);
        Assert.Equal(new Vector2(99f, 49f), box.Bounds.Min);
        Assert.Equal(new Vector2(101f, 53f), box.Bounds.Max);

        renderer.FlipX = true;

        Assert.Equal(new Vector2(-5f, 1f), muzzle.Position);
        Assert.Equal(new Vector2(99f, 49f), box.Bounds.Min);
        Assert.Equal(new Vector2(101f, 53f), box.Bounds.Max);
    }

    private static SpriteIntent At(in Sprite frame, Vector2 position) =>
        new(
            frame,
            position,
            position,
            PreviousRotation: 0f,
            Rotation: 0f,
            new Vector2(frame.Region.Width, frame.Region.Height),
            FlipX: false,
            FlipY: false,
            ColorRgba.White);
}
