using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

public sealed class RenderIntentTests
{
    [Fact]
    public void TheThreeChannelConstructor_IsOpaque()
    {
        Assert.Equal(new ColorRgba(1, 2, 3, 255), new ColorRgba(1, 2, 3));
    }

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
        FrameView view = new() { Camera = camera };

        view.Clear();

        Assert.Equal(camera, view.Camera);
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
}
