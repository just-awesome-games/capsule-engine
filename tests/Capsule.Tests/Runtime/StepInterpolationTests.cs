using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Runtime;

public sealed class StepInterpolationTests
{
    // A half-pixel coordinate is the worst case: the snap's midpoint rounding turns a one-ULP drift
    // into a whole pixel.
    private static readonly Vector2 OnASnapBoundary = new(153f, 74.5f);

    private static readonly Vector2 Output = new(1280f, 720f);

    private static IEnumerable<float> Alphas()
    {
        yield return 0.1f;
        yield return 0.3f;
        yield return 0.7f;
        yield return 1f / 3f;

        for (int step = 0; step <= 997; step++)
        {
            yield return step / 997f;
        }
    }

    [Fact]
    public void AStationaryPositionSnapsToTheSamePixelAtEveryAlpha()
    {
        Assert.All(Alphas(), alpha => Assert.Equal(
            new Vector2(153f, 74f),
            PixelGrid.SnapOffset(Vector2.Zero, StepInterpolation.Interpolate(OnASnapBoundary, OnASnapBoundary, alpha), 1f)));
    }

    [Fact]
    public void AStationaryCameraResolvesTheSameViewportAtEveryAlpha()
    {
        CameraView view = new(OnASnapBoundary, OnASnapBoundary, new Vector2(320f, 180f));

        Rect expected = view.Resolve(0f, Output);

        Assert.All(Alphas(), alpha => Assert.Equal(expected, view.Resolve(alpha, Output)));
    }
}
