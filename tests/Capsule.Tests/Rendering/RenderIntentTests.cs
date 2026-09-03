using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

public sealed class RenderIntentTests
{
    private static readonly TextureHandle Atlas = new("atlas", ".png");

    [Fact]
    public void AView_ReadsBackTheSpritesAddedInOrder()
    {
        FrameView view = new();
        SpriteIntent first = Unit(Vector2.Zero, Vector2.Zero);
        SpriteIntent second = Unit(Vector2.One, Vector2.One) with { Color = ColorRgba.Black };

        view.Add(first);
        view.Add(second);

        Assert.Equal(2, view.Sprites.Length);
        Assert.Equal(first, view.Sprites[0]);
        Assert.Equal(second, view.Sprites[1]);
    }

    // The stream is what fixes draw order across kinds, so a culled submission must leave no gap
    // in it and the survivors must still address their own pool.
    [Fact]
    public void TheCommandStream_HoldsWhatSurvivedInOrder_IndexingItsPool()
    {
        FrameView view = Looking();
        SpriteIntent kept = Unit(new Vector2(2, 2), new Vector2(2, 2));
        SpriteIntent culled = Unit(new Vector2(40, 40), new Vector2(40, 40));
        SpriteIntent last = Unit(new Vector2(3, 3), new Vector2(3, 3)) with { Color = ColorRgba.Black };

        view.Add(kept);
        view.Add(culled);
        view.Add(last);

        Assert.Equal([0, 1], view.Commands.ToArray().Select(static command => command.Index));
        Assert.All(view.Commands.ToArray(), command => Assert.Equal(RenderKind.Sprite, command.Kind));
        Assert.Equal(kept, view.Sprites[view.Commands[0].Index]);
        Assert.Equal(last, view.Sprites[view.Commands[1].Index]);
        Assert.Equal(new RenderMetrics(Submitted: 3, Visible: 2), view.Metrics);
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

        view.Add(Unit(Vector2.Zero, Vector2.Zero));
        view.Clear();

        Assert.Equal(camera, view.Camera);
        Assert.Equal(clearColor, view.ClearColor);
        Assert.Equal(TextureSampling.Point, view.Sampling);
        Assert.Equal(default, view.Metrics);
        Assert.Empty(view.Commands.ToArray());
        Assert.Empty(view.Sprites.ToArray());
    }

    [Fact]
    public void RewritingAView_YieldsOnlyTheNewSprites()
    {
        FrameView view = new();
        view.Add(Unit(Vector2.Zero, Vector2.Zero));
        view.Add(Unit(Vector2.One, Vector2.One));

        SpriteIntent rewritten = Unit(Vector2.One, new Vector2(2, 2));
        view.Clear();
        view.Add(rewritten);

        Assert.Equal(1, view.Sprites.Length);
        Assert.Equal(rewritten, view.Sprites[0]);
    }

    [Fact]
    public void AView_RejectsSpritesThatStayOutsideTheCamera()
    {
        FrameView view = Looking();

        view.Add(Unit(new Vector2(-2, 2), new Vector2(-1, 2)));
        view.Add(Unit(new Vector2(10, 2), new Vector2(10, 2)));
        view.Add(Unit(new Vector2(2, 2), new Vector2(3, 2)));

        Assert.Single(view.Sprites.ToArray());
        Assert.Equal(new RenderMetrics(Submitted: 3, Visible: 1), view.Metrics);
        Assert.Equal(2, view.Metrics.Culled);
    }

    [Fact]
    public void AView_KeepsASpriteWhoseMovementCrossesTheCamera()
    {
        FrameView view = Looking();
        SpriteIntent crossing = Unit(new Vector2(-4, 2), new Vector2(11, 2));

        view.Add(crossing);

        Assert.Equal(crossing, Assert.Single(view.Sprites.ToArray()));
    }

    [Fact]
    public void AView_KeepsASpriteTheCameraSweepsPast()
    {
        FrameView view = new()
        {
            Camera = new CameraView(new Vector2(5, 5), new Vector2(25, 5), new Vector2(10, 10)),
        };
        SpriteIntent passed = Unit(new Vector2(14, 2), new Vector2(14, 2));

        view.Add(passed);

        Assert.Equal(passed, Assert.Single(view.Sprites.ToArray()));
    }

    [Fact]
    public void AView_RejectsASpriteWithANonPositiveExtent_MovingOrStill()
    {
        FrameView view = Looking();

        view.Add(Unit(new Vector2(0, 2), new Vector2(10, 2)) with { Size = new Vector2(-1, 1) });
        view.Add(Unit(new Vector2(2, 0), new Vector2(2, 10)) with { Size = new Vector2(1, -1) });
        view.Add(Unit(new Vector2(2, 2), new Vector2(2, 2)) with { Size = Vector2.Zero });

        Assert.Empty(view.Sprites.ToArray());
        Assert.Equal(new RenderMetrics(Submitted: 3, Visible: 0), view.Metrics);
    }

    // A region with no texels has no scale to place the pivot by, so the rect it would occupy is
    // not a rect at all.
    [Fact]
    public void AView_RejectsASpriteWhoseRegionHasNoTexels()
    {
        FrameView view = Looking();

        view.Add(Unit(new Vector2(2, 2), new Vector2(2, 2)) with
        {
            Sprite = new Sprite(Atlas, new TextureRegion(0, 0, 0, 8)),
        });

        Assert.Empty(view.Sprites.ToArray());
    }

    // The pivot, not the position, is what the sprite hangs from: an 8x8 frame anchored at its
    // centre and positioned two units past the camera's right edge still covers two columns
    // inside it, where the same frame anchored at its corner is wholly outside.
    [Fact]
    public void AView_CullsAgainstTheRectThePivotPlaces()
    {
        FrameView view = Looking();
        SpriteIntent centred = Centred(new Vector2(12, 5));
        SpriteIntent cornered = centred with { Sprite = centred.Sprite with { Pivot = Vector2.Zero } };

        view.Add(centred);
        view.Add(cornered);

        Assert.Equal(centred, Assert.Single(view.Sprites.ToArray()));
    }

    // Flipping mirrors the region about the pivot, so the drawn rect moves to the other side of
    // it: a frame anchored at its left edge just off the camera's right edge is only visible once
    // it flips back over the camera.
    [Fact]
    public void AView_CullsAgainstTheSideTheFlipPutsTheRegionOn()
    {
        FrameView view = Looking();
        SpriteIntent unflipped = Unit(new Vector2(11, 5), new Vector2(11, 5)) with
        {
            Sprite = new Sprite(Atlas, new TextureRegion(0, 0, 8, 8)),
            Size = new Vector2(8, 8),
        };

        view.Add(unflipped);
        view.Add(unflipped with { FlipX = true });

        Assert.True(Assert.Single(view.Sprites.ToArray()).FlipX);
    }

    // The anchor moves both ends of the sweep, not one: a frame anchored 4 texels in and travelling
    // to a stop 5 units short of the camera hangs entirely outside it, where the same travel
    // anchored at the corner still reaches in.
    [Fact]
    public void AView_AppliesThePivotToBothEndsOfASweep()
    {
        FrameView view = Looking();
        SpriteIntent anchored = Swept(new Vector2(-10, 5), new Vector2(-5, 5), new Vector2(4, 0));
        SpriteIntent cornered = anchored with { Sprite = anchored.Sprite with { Pivot = Vector2.Zero } };

        view.Add(anchored);
        view.Add(cornered);

        Assert.Equal(cornered, Assert.Single(view.Sprites.ToArray()));
    }

    // And the flip moves both ends too: mirroring a frame anchored 2 texels in swings 6 texels of
    // it back over the camera along the whole sweep, which the unflipped travel never touches.
    [Fact]
    public void AView_AppliesTheFlipToBothEndsOfASweep()
    {
        FrameView view = Looking();
        SpriteIntent travelling = Swept(new Vector2(13, 5), new Vector2(16, 5), new Vector2(2, 0));

        view.Add(travelling);
        view.Add(travelling with { FlipX = true });

        Assert.True(Assert.Single(view.Sprites.ToArray()).FlipX);
    }

    // Y is the same rule on the other axis: the vertical flip swings a frame anchored 2 texels
    // down back up over the camera for the whole of a downward sweep.
    [Fact]
    public void AView_AppliesTheVerticalFlipToBothEndsOfASweep()
    {
        FrameView view = Looking();
        SpriteIntent falling = Swept(new Vector2(5, 13), new Vector2(5, 16), new Vector2(0, 2));

        view.Add(falling);
        view.Add(falling with { FlipY = true });

        Assert.True(Assert.Single(view.Sprites.ToArray()).FlipY);
    }

    [Fact]
    public void AView_RejectsAnUnknownSamplingMode()
    {
        FrameView view = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => view.Sampling = (TextureSampling)99);
    }

    private static FrameView Looking() =>
        new() { Camera = new CameraView(new Vector2(5, 5), new Vector2(10, 10)) };

    private static SpriteIntent Unit(Vector2 previous, Vector2 position) =>
        new(
            new Sprite(Atlas, new TextureRegion(0, 0, 1, 1)),
            previous,
            position,
            Vector2.One,
            FlipX: false,
            FlipY: false,
            ColorRgba.White);

    private static SpriteIntent Swept(Vector2 previous, Vector2 position, Vector2 pivot) =>
        new(
            new Sprite(Atlas, new TextureRegion(0, 0, 8, 8), pivot),
            previous,
            position,
            new Vector2(8, 8),
            FlipX: false,
            FlipY: false,
            ColorRgba.White);

    private static SpriteIntent Centred(Vector2 position) =>
        new(
            new Sprite(Atlas, new TextureRegion(0, 0, 8, 8), new Vector2(4, 4)),
            position,
            position,
            new Vector2(8, 8),
            FlipX: false,
            FlipY: false,
            ColorRgba.White);
}
