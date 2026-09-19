using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Runtime;

public sealed class ScrollLayoutTests
{
    private static readonly Vector2 Span = new(320, 180);
    private static readonly Vector2 Origin = new(160, 90);

    // A layer's corner is the frame's corner scaled by the factor about the origin: the origin at
    // 0, halfway at 0.5, the frame's own at 1, and past it above 1.
    [Theory]
    [InlineData(0f, 0f, 0f, 0f)]
    [InlineData(0.5f, 0.5f, 100f, 50f)]
    [InlineData(1f, 1f, 200f, 100f)]
    [InlineData(1.2f, 1.2f, 240f, 120f)]
    [InlineData(0.5f, 1f, 100f, 100f)]
    public void TheCorner_FollowsTheFramesCornerByTheFactorAboutTheOrigin(float factorX, float factorY, float cornerX, float cornerY)
    {
        Vector2 offset = new(200, 100);
        Vector2 expected = new(cornerX, cornerY);

        Vector2 fromZero = ScrollLayout.Corner(offset, Vector2.Zero, new Vector2(factorX, factorY));
        Vector2 fromOrigin = ScrollLayout.Corner(Origin + offset, Origin, new Vector2(factorX, factorY));

        Assert.Equal(expected.X, fromZero.X, 3);
        Assert.Equal(expected.Y, fromZero.Y, 3);
        Assert.Equal(Origin.X + expected.X, fromOrigin.X, 3);
        Assert.Equal(Origin.Y + expected.Y, fromOrigin.Y, 3);
    }

    // At the origin every layer's corner is the frame's, so what is authored there is drawn there.
    [Fact]
    public void AtTheOrigin_EveryLayerIsWhereItWasAuthored()
    {
        Assert.Equal(Origin, ScrollLayout.Corner(Origin, Origin, Vector2.Zero));
        Assert.Equal(Origin, ScrollLayout.Corner(Origin, Origin, new Vector2(3f, -1f)));
    }

    // A factor-zero intent authored at the origin lands on the frame's corner whatever span the
    // fit placed the frame at, so a screen-fixed layer never drifts with the window's shape.
    [Theory]
    [InlineData(320f, 180f)]
    [InlineData(640f, 180f)]
    [InlineData(320f, 400f)]
    public void AFixedLayerAtTheOrigin_LandsOnTheFramesCornerAtAnySpan(float width, float height)
    {
        Vector2 span = new(width, height);
        CameraView camera = new(new Vector2(700, 300), new Vector2(731, 290), new Vector2(320, 180), ViewportFit.Expand);
        Rect frame = camera.Place(0.4f, span);

        Vector2 corner = ScrollLayout.Corner(frame.Position, Vector2.Zero, Vector2.Zero);
        Vector2 placed = ScrollLayout.Place(Vector2.Zero, corner, frame.Position, snap: false, 1f);

        Assert.Equal(Vector2.Zero, corner);
        Assert.Equal(frame.Position, placed);
    }

    // The frame's rect is the one the camera placed — interpolated, confined and cut — so a layer
    // pins with a bounded camera, cuts with a teleport and interpolates with the alpha.
    [Fact]
    public void TheCorner_TakesTheFramesOwnPlacement()
    {
        Rect bounds = new(0, 0, 640, 180);
        CameraView pinned = new(new Vector2(600, 90), new Vector2(700, 90), Span, Bounds: bounds, ScrollOrigin: Origin);
        Vector2 atEdge = Layer(pinned, 0f, 0.5f);
        Assert.Equal(atEdge, Layer(pinned, 1f, 0.5f));
        Assert.Equal(new Vector2(240, 45), atEdge);

        CameraView cut = new(new Vector2(500, 90), new Vector2(500, 90), Span, ScrollOrigin: Origin);
        Assert.Equal(Layer(cut, 0f, 0.5f), Layer(cut, 1f, 0.5f));

        CameraView sweeping = new(new Vector2(320, 180), new Vector2(520, 180), Span, ScrollOrigin: Origin);
        Assert.Equal(Origin, Layer(sweeping, 0f, 0.5f));
        Assert.Equal(new Vector2(210, 90), Layer(sweeping, 0.5f, 0.5f));
        Assert.Equal(new Vector2(260, 90), Layer(sweeping, 1f, 0.5f));
    }

    // With the layer's corner at the frame's, a position is placed as the world always was: snapped
    // to whole pixels from the frame's corner.
    [Fact]
    public void Place_WithTheFramesOwnCorner_IsTheWorldsPlacement()
    {
        Vector2 corner = new(10.3f, 20.7f);
        Vector2 position = new(15.5f, 25.1f);

        Assert.Equal(corner + PixelGrid.SnapOffset(corner, position, 3f), ScrollLayout.Place(position, corner, corner, snap: true, 3f));
        Assert.Equal(position, ScrollLayout.Place(position, corner, corner, snap: false, 3f));
    }

    // An entity's own motion composes with the camera term: its interpolated position is what is
    // placed, and it lands the same distance into the frame that it is into its layer.
    [Fact]
    public void Place_CarriesAnOffsetFromTheLayerIntoTheFrame()
    {
        Vector2 interpolated = StepInterpolation.Interpolate(new Vector2(40, 8), new Vector2(44, 8), 0.5f);

        Vector2 placed = ScrollLayout.Place(interpolated, new Vector2(30, 0), new Vector2(130, 100), snap: false, 1f);

        Assert.Equal(new Vector2(142, 108), placed);
    }

    // A camera moving right at a fractional speed: every layer's tiles step left by whole pixels,
    // never back, and two tiles that abut stay abutting.
    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(0.3f)]
    [InlineData(1f)]
    [InlineData(1.2f)]
    public void SnappingFromTheLayersCorner_IsMonotoneAndKeepsAdjacentTilesTogether(float factor)
    {
        const float scale = 3f;
        const float tile = 16f;
        Vector2 layerFactor = new(factor, 1f);
        float[] tiles = [0f, tile, tile * 2];
        float[] previous = [float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity];

        for (int step = 0; step < 400; step++)
        {
            Vector2 corner = ScrollLayout.Corner(Origin + new Vector2(step * 0.37f, 0f), Origin, layerFactor);

            for (int index = 0; index < tiles.Length; index++)
            {
                float placed = ScrollLayout.Place(new Vector2(tiles[index], 0f), corner, Vector2.Zero, snap: true, scale).X;
                float pixels = placed * scale;

                Assert.Equal(MathF.Round(pixels), pixels, 3);
                Assert.True(placed <= previous[index], $"step {step}: tile {index} moved from {previous[index]} to {placed}");
                previous[index] = placed;

                if (index > 0)
                {
                    Assert.Equal(tile, placed - previous[index - 1], 3);
                }
            }
        }
    }

    private static Vector2 Layer(in CameraView camera, float alpha, float factor)
    {
        Rect frame = camera.Place(alpha, Span);

        return ScrollLayout.Corner(frame.Position, camera.ScrollOrigin, new Vector2(factor, factor));
    }
}
