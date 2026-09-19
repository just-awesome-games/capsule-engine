using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

public sealed class TilingTests
{
    private static readonly TextureHandle Atlas = new("atlas", ".png");

    // A 16x8 frame at its corner, so a copy's position is its corner.
    private static readonly Sprite Frame = new(Atlas, new TextureRegion(4, 2, 16, 8));

    [Fact]
    public void ZeroTiling_DrawsTheFrameOnceAndUnchanged()
    {
        FrameView view = new();
        SpriteIntent sprite = At(new Vector2(3, 5));

        view.Add(sprite, Vector2.Zero);

        Assert.Equal([sprite], view.Sprites.ToArray());
    }

    // A finite extent covers that much from the frame's low edge, cropping the copy that reaches
    // past it to whole texels; an axis at zero draws that axis once.
    [Fact]
    public void AFiniteExtent_RepeatsAtTheDrawnSizeAndCropsTheFarCopy()
    {
        FrameView view = new();

        view.Add(At(new Vector2(3, 5)), new Vector2(40, 0));

        SpriteIntent[] copies = view.Sprites.ToArray();
        Assert.Equal(3, copies.Length);
        Assert.Equal([3f, 19f, 35f], copies.Select(copy => copy.Position.X));
        Assert.All(copies, copy => Assert.Equal(5f, copy.Position.Y));
        Assert.Equal(new Vector2(16, 8), copies[0].Size);
        Assert.Equal(new Vector2(16, 8), copies[1].Size);
        Assert.Equal(new Vector2(8, 8), copies[2].Size);
        Assert.Equal(new TextureRegion(4, 2, 8, 8), copies[2].Sprite.Region);
        Assert.Equal(new RenderMetrics(Submitted: 3, Visible: 3), view.Metrics);
    }

    // The period is the drawn extent, so a scaled frame repeats at its scaled size and the crop is
    // measured in its scaled texels.
    [Fact]
    public void AScaledFrame_RepeatsAtItsScaledExtent()
    {
        FrameView view = new();
        SpriteIntent scaled = At(Vector2.Zero) with { Size = new Vector2(32, 16) };

        view.Add(scaled, new Vector2(40, 40));

        SpriteIntent[] copies = view.Sprites.ToArray();
        Assert.Equal(6, copies.Length);
        Assert.Equal(new Vector2(32, 0), copies[1].Position);
        Assert.Equal(new Vector2(8, 16), copies[1].Size);
        Assert.Equal(new TextureRegion(4, 2, 4, 8), copies[1].Sprite.Region);
        Assert.Equal(new Vector2(0, 16), copies[2].Position);
        Assert.Equal(new Vector2(0, 32), copies[4].Position);
        Assert.Equal(new Vector2(32, 8), copies[4].Size);
        Assert.Equal(new Vector2(8, 8), copies[5].Size);
    }

    // A cropped copy keeps the near texels of the frame as drawn, which on a mirrored axis are the
    // far texels of the region, and stays where its index puts it.
    [Fact]
    public void AMirroredFrame_CropsFromTheFarSideOfItsRegion()
    {
        FrameView view = new();
        SpriteIntent mirrored = At(new Vector2(3, 5)) with { FlipX = true, Sprite = Frame with { Pivot = new Vector2(16, 0) } };

        view.Add(mirrored, new Vector2(24, 0));

        SpriteIntent[] copies = view.Sprites.ToArray();
        Assert.Equal(2, copies.Length);
        Assert.All(copies, copy => Assert.True(copy.FlipX));
        Assert.Equal(new TextureRegion(12, 2, 8, 8), copies[1].Sprite.Region);
        Assert.Equal(new Vector2(19, 5), copies[1].Position);
        Assert.True(copies[1].TryGetSweptBounds(out Rect far));
        Assert.Equal(new Rect(19, 5, 27, 13), far);
    }

    // The camera passes the frame in either direction: every copy on screen is there and none
    // off it is, whichever side of the authored frame the camera is on.
    [Theory]
    [InlineData(-100f, -112f, -96f)]
    [InlineData(100f, 80f, 96f)]
    public void AnUnboundedExtent_CoversTheCameraOnEitherSideOfTheFrame(float center, float firstCopy, float lastCopy)
    {
        FrameView view = new() { Camera = new CameraView(new Vector2(center, 4), new Vector2(20, 8)) };

        view.Add(At(Vector2.Zero), new Vector2(float.PositiveInfinity, 0));

        float[] corners = view.Sprites.ToArray().Select(copy => copy.Position.X).ToArray();
        Assert.Equal(firstCopy, corners.Min());
        Assert.Equal(lastCopy, corners.Max());
        Assert.Equal(2, corners.Length);
        Assert.All(corners, corner => Assert.Equal(0f, (corner % 16 + 16) % 16));
    }

    [Fact]
    public void AnUnboundedExtent_CoversTheWholeSweptRegionOfAMovingCamera()
    {
        FrameView view = new() { Camera = new CameraView(new Vector2(0, 4), new Vector2(64, 4), new Vector2(20, 8)) };

        view.Add(At(Vector2.Zero), new Vector2(float.PositiveInfinity, float.PositiveInfinity));

        Rect swept = view.Camera.SweptBounds;
        SpriteIntent[] copies = view.Sprites.ToArray();
        Assert.All(copies, copy =>
        {
            Assert.True(copy.TryGetSweptBounds(out Rect rect));
            Assert.True(rect.Intersects(swept));
        });

        float left = copies.Min(copy => copy.Position.X);
        float right = copies.Max(copy => copy.Position.X + copy.Size.X);
        Assert.True(left <= swept.Left);
        Assert.True(right >= swept.Right);
    }

    [Fact]
    public void AnUnboundedExtent_WithNothingToCullAgainst_DrawsTheFrameOnce()
    {
        FrameView view = new();
        SpriteIntent sprite = At(new Vector2(3, 5));

        view.Add(sprite, new Vector2(float.PositiveInfinity, float.PositiveInfinity));

        Assert.Equal([sprite], view.Sprites.ToArray());
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, float.NaN)]
    [InlineData(float.NegativeInfinity, 8f)]
    public void ATilingThatIsNegativeOrNaN_DrawsNothing(float x, float y)
    {
        FrameView view = new();

        view.Add(At(Vector2.Zero), new Vector2(x, y));

        Assert.Empty(view.Sprites.ToArray());
        Assert.Equal(new RenderMetrics(Submitted: 0, Visible: 0), view.Metrics);
    }

    // Each copy is culled on its own, so a finite run reaching past the camera costs only the copies
    // the camera can see.
    [Fact]
    public void CopiesAreCulledOneByOne()
    {
        FrameView view = new() { Camera = new CameraView(new Vector2(40, 4), new Vector2(20, 8)) };

        view.Add(At(Vector2.Zero), new Vector2(160, 0));

        Assert.Equal([16f, 32f, 48f], view.Sprites.ToArray().Select(copy => copy.Position.X));
    }

    private static SpriteIntent At(Vector2 corner) =>
        new(
            Frame,
            corner,
            corner,
            PreviousRotation: 0f,
            Rotation: 0f,
            new Vector2(16, 8),
            FlipX: false,
            FlipY: false,
            ColorRgba.White);
}
