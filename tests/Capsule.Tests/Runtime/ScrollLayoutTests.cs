using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Runtime;

public sealed class ScrollLayoutTests
{
    private static readonly Vector2 Span = new(320, 180);
    private static readonly Vector2 ScrollCenter = new(160, 90);

    // A layer's corner is the frame's moved by (1 - factor) of the way from the frame's centre to the
    // scroll centre: the scroll centre's frame at 0, halfway at 0.5, the frame's own at 1, and past it
    // above 1.
    [Theory]
    [InlineData(0f, 0f, 0f, 0f)]
    [InlineData(0.5f, 0.5f, 100f, 50f)]
    [InlineData(1f, 1f, 200f, 100f)]
    [InlineData(1.2f, 1.2f, 240f, 120f)]
    [InlineData(0.5f, 1f, 100f, 100f)]
    public void TheCorner_FollowsTheFramesCentreByTheFactorAboutTheScrollCenter(float factorX, float factorY, float cornerX, float cornerY)
    {
        Vector2 offset = new(200, 100);
        Vector2 topLeft = ScrollCenter - (Span / 2f) + offset;
        Vector2 parallax = ScrollLayout.Parallax(topLeft, Span, ScrollCenter);

        Vector2 corner = ScrollLayout.Corner(topLeft, parallax, new Vector2(factorX, factorY));

        Assert.Equal(-offset, parallax);
        Assert.Equal(cornerX, corner.X, 3);
        Assert.Equal(cornerY, corner.Y, 3);
    }

    // The frame's rect is the one the camera placed, interpolated, confined and cut. A layer pins
    // with a bounded camera, cuts with a teleport and interpolates with the alpha.
    [Fact]
    public void TheCorner_TakesTheFramesOwnPlacement()
    {
        Rect bounds = new(0, 0, 640, 180);
        CameraView pinned = new(new Vector2(600, 90), new Vector2(700, 90), Span, Bounds: bounds, ScrollCenter: ScrollCenter);
        Vector2 atEdge = Layer(pinned, 0f, 0.5f);
        Assert.Equal(atEdge, Layer(pinned, 1f, 0.5f));
        Assert.Equal(new Vector2(160, 0), atEdge);

        CameraView cut = new(new Vector2(500, 90), new Vector2(500, 90), Span, ScrollCenter: ScrollCenter);
        Assert.Equal(Layer(cut, 0f, 0.5f), Layer(cut, 1f, 0.5f));

        CameraView sweeping = new(new Vector2(160, 90), new Vector2(360, 90), Span, ScrollCenter: ScrollCenter);
        Assert.Equal(Vector2.Zero, Layer(sweeping, 0f, 0.5f));
        Assert.Equal(new Vector2(50, 0), Layer(sweeping, 0.5f, 0.5f));
        Assert.Equal(new Vector2(100, 0), Layer(sweeping, 1f, 0.5f));
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
            Vector2 topLeft = new(step * 0.37f, 0f);
            Vector2 corner = ScrollLayout.Corner(topLeft, ScrollLayout.Parallax(topLeft, Span, ScrollCenter), layerFactor);

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

    // At a fractional scale a row of abutting tiles, centre-pivoted and in every quarter turn a tile
    // map draws, covers each surface pixel exactly once. A quad drawn as the renderer draws it starts
    // where the last one ended, on a whole pixel. Every other tile's turn is a hair off its quarter
    // turn, as an interpolated spin settles, and it still draws exactly its snapped rect.
    [Fact]
    public void AbuttingQuarterTurnedQuads_ShareEveryEdgeAtAFractionalScale()
    {
        const float scale = 1.3f;
        const float tile = 16f;
        Vector2 layerCorner = new(1203.37f, 88.61f);
        Vector2 half = new(tile / 2f);
        float previousRight = float.NaN;

        for (int index = 0; index < 24; index++)
        {
            float rotation = ((index % 4) * (MathF.PI / 2f)) + (index % 2 == 0 ? 0f : 1e-4f);
            Vector2 centre = layerCorner + new Vector2(5.5f + (index * tile), 3.25f);

            Assert.True(ScrollLayout.TryPlaceSnapped(centre, half, new Vector2(tile), rotation, layerCorner, Vector2.Zero, scale, out Vector2 corner, out Vector2 pixels, out int turns));

            SpriteQuad quad = SpriteQuad.PlaceQuarterTurned(corner, pixels / (tile * scale), 0, 0, 16, 16, 1f / 16f, 1f / 16f, turns, flipX: false, flipY: false);
            float[] xs = [quad.TopLeft.X, quad.TopRight.X, quad.BottomLeft.X, quad.BottomRight.X];
            float[] ys = [quad.TopLeft.Y, quad.TopRight.Y, quad.BottomLeft.Y, quad.BottomRight.Y];
            Assert.Equal(2, xs.Distinct().Count());
            Assert.Equal(2, ys.Distinct().Count());

            float left = xs.Min() * scale;
            float right = xs.Max() * scale;
            Assert.Equal(MathF.Round(left), left, 2);
            Assert.Equal(MathF.Round(right), right, 2);
            Assert.InRange(right - left, 19.99f, 21.01f);
            Assert.InRange((ys.Max() - ys.Min()) * scale, 19.99f, 21.01f);
            if (index > 0)
            {
                Assert.Equal(previousRight, left, 2);
            }

            previousRight = right;
        }
    }

    private static Vector2 Layer(in CameraView camera, float alpha, float factor)
    {
        Rect frame = camera.Place(alpha, Span);

        return ScrollLayout.Corner(frame.Position, ScrollLayout.Parallax(frame.Position, Span, camera.ScrollCenter), new Vector2(factor, factor));
    }
}
