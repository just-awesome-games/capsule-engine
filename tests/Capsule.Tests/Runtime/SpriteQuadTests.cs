using System.Numerics;
using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Runtime;

public sealed class SpriteQuadTests
{
    // A 64 by 64 texture.
    private const float Texel = 1f / 64f;

    // The origin is in region texels and is scaled with the region, so the drawn rect sits origin
    // times scale up and left of the position; the texture coordinates are the region's edges
    // over the texture's extent.
    [Fact]
    public void Unrotated_PlacesTheScaledRegionAboutItsScaledOrigin()
    {
        SpriteQuad quad = SpriteQuad.Place(
            new Vector2(100f, 50f),
            new Vector2(4f, 8f),
            new Vector2(2f, 0.5f),
            regionX: 16,
            regionY: 32,
            regionWidth: 8,
            regionHeight: 8,
            Texel,
            Texel,
            rotation: 0f,
            flipX: false,
            flipY: false);

        Assert.Equal(new Vector2(92f, 46f), quad.TopLeft);
        Assert.Equal(new Vector2(108f, 46f), quad.TopRight);
        Assert.Equal(new Vector2(92f, 50f), quad.BottomLeft);
        Assert.Equal(new Vector2(108f, 50f), quad.BottomRight);
        Assert.Equal(new Vector2(0.25f, 0.5f), quad.TexTopLeft);
        Assert.Equal(new Vector2(0.375f, 0.625f), quad.TexBottomRight);
    }

    // A quarter turn clockwise in the Y-down space: the top edge, 4 wide, now runs down the
    // screen from the position, and the left edge, 6 tall, runs left of it.
    [Fact]
    public void Rotated_TurnsClockwiseAboutTheOrigin()
    {
        SpriteQuad quad = SpriteQuad.Place(
            new Vector2(10f, 20f),
            Vector2.Zero,
            Vector2.One,
            regionX: 0,
            regionY: 0,
            regionWidth: 4,
            regionHeight: 6,
            Texel,
            Texel,
            MathF.PI / 2f,
            flipX: false,
            flipY: false);

        AssertClose(new Vector2(10f, 20f), quad.TopLeft);
        AssertClose(new Vector2(10f, 24f), quad.TopRight);
        AssertClose(new Vector2(4f, 20f), quad.BottomLeft);
        AssertClose(new Vector2(4f, 24f), quad.BottomRight);
    }

    // A flip swaps the texture coordinates on that axis and moves no corner.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Flip_SwapsTextureCoordinatesAndLeavesTheCorners(bool flipX, bool flipY)
    {
        SpriteQuad upright = Place(flipX: false, flipY: false);
        SpriteQuad flipped = Place(flipX, flipY);

        Assert.Equal(upright.TopLeft, flipped.TopLeft);
        Assert.Equal(upright.BottomRight, flipped.BottomRight);
        Assert.Equal(flipX ? upright.TexBottomRight.X : upright.TexTopLeft.X, flipped.TexTopLeft.X);
        Assert.Equal(flipX ? upright.TexTopLeft.X : upright.TexBottomRight.X, flipped.TexBottomRight.X);
        Assert.Equal(flipY ? upright.TexBottomRight.Y : upright.TexTopLeft.Y, flipped.TexTopLeft.Y);
        Assert.Equal(flipY ? upright.TexTopLeft.Y : upright.TexBottomRight.Y, flipped.TexBottomRight.Y);
    }

    private static SpriteQuad Place(bool flipX, bool flipY) =>
        SpriteQuad.Place(
            new Vector2(30f, 40f),
            new Vector2(2f, 2f),
            new Vector2(3f, 3f),
            regionX: 8,
            regionY: 16,
            regionWidth: 8,
            regionHeight: 4,
            Texel,
            Texel,
            rotation: 0f,
            flipX,
            flipY);

    private static void AssertClose(Vector2 expected, Vector2 actual)
    {
        Assert.Equal(expected.X, actual.X, 0.0001);
        Assert.Equal(expected.Y, actual.Y, 0.0001);
    }
}
