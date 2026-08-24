using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

public sealed class RenderIntentTests
{
    [Fact]
    public void AColor_CarriesItsChannels()
    {
        ColorRgba color = new(1, 2, 3, 4);

        Assert.Equal(1, color.R);
        Assert.Equal(2, color.G);
        Assert.Equal(3, color.B);
        Assert.Equal(4, color.A);
    }

    [Fact]
    public void TheThreeChannelConstructor_IsOpaque()
    {
        Assert.Equal(new ColorRgba(1, 2, 3, 255), new ColorRgba(1, 2, 3));
    }

    [Fact]
    public void NamedColors_AreTheirCanonicalValues()
    {
        Assert.Equal(new ColorRgba(0, 0, 0, 255), ColorRgba.Black);
        Assert.Equal(new ColorRgba(255, 255, 255, 255), ColorRgba.White);
    }

    [Fact]
    public void Colors_CompareByChannel()
    {
        Assert.NotEqual(ColorRgba.White, ColorRgba.Black);
        Assert.Equal(ColorRgba.White.GetHashCode(), new ColorRgba(255, 255, 255).GetHashCode());
    }

    [Fact]
    public void AQuad_CarriesWhereItWasAndWhereItIs()
    {
        QuadIntent quad = new(new Vector2(1, 2), new Vector2(3, 4), new Vector2(5, 6), ColorRgba.White);

        Assert.Equal(new Vector2(1, 2), quad.PreviousPosition);
        Assert.Equal(new Vector2(3, 4), quad.Position);
        Assert.Equal(new Vector2(5, 6), quad.Size);
        Assert.Equal(ColorRgba.White, quad.Color);
    }

    [Fact]
    public void Quads_CompareByValue()
    {
        QuadIntent quad = new(Vector2.Zero, Vector2.One, new Vector2(2, 2), ColorRgba.White);
        QuadIntent same = new(Vector2.Zero, Vector2.One, new Vector2(2, 2), ColorRgba.White);
        QuadIntent moved = quad with { Position = new Vector2(9, 9) };

        Assert.True(quad == same);
        Assert.True(quad != moved);
        Assert.Equal(quad.GetHashCode(), same.GetHashCode());
        Assert.Contains("Position", quad.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ADefaultCamera_SpansNothing()
    {
        CameraView camera = default;

        Assert.Equal(Vector2.Zero, camera.Center);
        Assert.Equal(Vector2.Zero, camera.Size);
    }

    [Fact]
    public void Cameras_CompareByValue()
    {
        CameraView camera = new(new Vector2(1, 2), new Vector2(16, 9));
        CameraView same = new(new Vector2(1, 2), new Vector2(16, 9));
        CameraView panned = camera with { Center = Vector2.Zero };

        Assert.True(camera == same);
        Assert.True(camera != panned);
        Assert.Equal(camera.GetHashCode(), same.GetHashCode());
        Assert.Contains("Center", camera.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ANewView_ShowsNothing()
    {
        FrameView view = new();

        Assert.Equal(default, view.Camera);
        Assert.True(view.Quads.IsEmpty);
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
    public void AView_HoldsTheCameraAssignedToIt()
    {
        FrameView view = new() { Camera = new CameraView(new Vector2(4, 4), new Vector2(32, 18)) };

        Assert.Equal(new CameraView(new Vector2(4, 4), new Vector2(32, 18)), view.Camera);
    }

    [Fact]
    public void ClearingAView_EmptiesTheQuads()
    {
        FrameView view = new();
        view.AddQuad(new QuadIntent(Vector2.Zero, Vector2.Zero, Vector2.One, ColorRgba.White));

        view.Clear();

        Assert.True(view.Quads.IsEmpty);
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
