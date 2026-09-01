using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

public sealed class RenderIntentTests
{
    [Fact]
    public void AView_ReadsBackTheQuadsAddedInOrder()
    {
        FrameView view = new();
        QuadIntent first = new(Vector2.Zero, Vector2.Zero, Vector2.One, ColorRgba.White);
        QuadIntent second = new(Vector2.One, Vector2.One, Vector2.One, ColorRgba.Black);

        view.AddQuad(first);
        view.AddQuad(second);

        Assert.Equal(2, view.Quads.Length);
        Assert.Equal(first, view.Quads[0]);
        Assert.Equal(second, view.Quads[1]);
    }

    [Fact]
    public void ClearingAView_LeavesTheCameraAlone()
    {
        CameraView camera = new(Vector2.One, new Vector2(10, 10));
        ColorRgba clearColor = new(12, 34, 56);
        FrameView view = new()
        {
            Camera = camera,
            ClearColor = clearColor,
            Sampling = TextureSampling.Point,
        };

        view.Clear();

        Assert.Equal(camera, view.Camera);
        Assert.Equal(clearColor, view.ClearColor);
        Assert.Equal(TextureSampling.Point, view.Sampling);
        Assert.Equal(default, view.Metrics);
    }

    [Fact]
    public void RewritingAView_YieldsOnlyTheNewQuads()
    {
        FrameView view = new();
        view.AddQuad(new QuadIntent(Vector2.Zero, Vector2.Zero, Vector2.One, ColorRgba.White));
        view.AddQuad(new QuadIntent(Vector2.One, Vector2.One, Vector2.One, ColorRgba.White));

        QuadIntent rewritten = new(Vector2.One, new Vector2(2, 2), Vector2.One, ColorRgba.Black);
        view.Clear();
        view.AddQuad(rewritten);

        Assert.Equal(1, view.Quads.Length);
        Assert.Equal(rewritten, view.Quads[0]);
    }

    [Fact]
    public void AView_RejectsQuadsThatStayOutsideTheCamera()
    {
        FrameView view = new()
        {
            Camera = new CameraView(new Vector2(5, 5), new Vector2(10, 10)),
        };

        view.AddQuad(new QuadIntent(new Vector2(-2, 2), new Vector2(-1, 2), Vector2.One, ColorRgba.White));
        view.AddQuad(new QuadIntent(new Vector2(10, 2), new Vector2(10, 2), Vector2.One, ColorRgba.White));
        view.AddQuad(new QuadIntent(new Vector2(2, 2), new Vector2(3, 2), Vector2.One, ColorRgba.White));

        Assert.Single(view.Quads.ToArray());
        Assert.Equal(new RenderMetrics(TotalQuads: 3, VisibleQuads: 1), view.Metrics);
        Assert.Equal(2, view.Metrics.CulledQuads);
    }

    [Fact]
    public void AView_KeepsAQuadWhoseMovementCrossesTheCamera()
    {
        FrameView view = new()
        {
            Camera = new CameraView(new Vector2(5, 5), new Vector2(10, 10)),
        };
        QuadIntent crossing = new(new Vector2(-4, 2), new Vector2(11, 2), Vector2.One, ColorRgba.White);

        view.AddQuad(crossing);

        Assert.Equal(crossing, Assert.Single(view.Quads.ToArray()));
    }

    [Fact]
    public void AView_KeepsAQuadTheCameraSweepsPast()
    {
        FrameView view = new()
        {
            Camera = new CameraView(new Vector2(5, 5), new Vector2(25, 5), new Vector2(10, 10)),
        };
        QuadIntent passed = new(new Vector2(14, 2), new Vector2(14, 2), Vector2.One, ColorRgba.White);

        view.AddQuad(passed);

        Assert.Equal(passed, Assert.Single(view.Quads.ToArray()));
    }

    [Fact]
    public void AView_RejectsAQuadWithANonPositiveExtent_MovingOrStill()
    {
        FrameView view = new()
        {
            Camera = new CameraView(new Vector2(5, 5), new Vector2(10, 10)),
        };

        view.AddQuad(new QuadIntent(new Vector2(0, 2), new Vector2(10, 2), new Vector2(-1, 1), ColorRgba.White));
        view.AddQuad(new QuadIntent(new Vector2(2, 0), new Vector2(2, 10), new Vector2(1, -1), ColorRgba.White));
        view.AddQuad(new QuadIntent(new Vector2(2, 2), new Vector2(2, 2), Vector2.Zero, ColorRgba.White));

        Assert.Empty(view.Quads.ToArray());
        Assert.Equal(new RenderMetrics(TotalQuads: 3, VisibleQuads: 0), view.Metrics);
    }

    [Fact]
    public void AView_RejectsAnUnknownSamplingMode()
    {
        FrameView view = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => view.Sampling = (TextureSampling)99);
    }
}
