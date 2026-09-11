using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

public sealed class CameraViewTests
{
    private static readonly Vector2 Span = new(320f, 180f);
    private static readonly Vector2 Wider = new(1000f, 250f);
    private static readonly Vector2 Taller = new(500f, 1000f);

    [Fact]
    public void Letterbox_ShowsTheDeclaredSpanWhateverShapeTheOutputIs()
    {
        CameraView view = new(Vector2.Zero, Span);

        Rect expected = new(-160f, -90f, 160f, 90f);

        Assert.Equal(expected, view.Resolve(1f, Wider));
        Assert.Equal(expected, view.Resolve(1f, Taller));
    }

    // The old renderer fitted Size around the interpolated centre and nothing else, so every frame
    // a game already ships must resolve to exactly that rect.
    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void Letterbox_WithNoBounds_IsTheViewportTheCentreAndSpanAlreadyDescribed(float alpha)
    {
        CameraView view = new(new Vector2(100f, 40f), new Vector2(200f, 80f), Span);

        Vector2 center = Vector2.Lerp(view.PreviousCenter, view.Center, alpha);
        Rect resolved = view.Resolve(alpha, Wider);

        Assert.Equal(new Rect(center.X - 160f, center.Y - 90f, center.X + 160f, center.Y + 90f), resolved);
    }

    [Fact]
    public void Expand_RevealsMoreWorldOnTheOutputsSlackAxisAtTheSameScale()
    {
        CameraView view = new(Vector2.Zero, Vector2.Zero, Span, ViewportFit.Expand);

        // 4:1 against a 16:9 span: the height binds and the width grows to 4 * 180.
        Assert.Equal(new Rect(-360f, -90f, 360f, 90f), view.Resolve(1f, Wider));

        // 1:2 against 16:9: the width binds and the height grows to 320 * 2.
        Assert.Equal(new Rect(-160f, -320f, 160f, 320f), view.Resolve(1f, Taller));
    }

    [Fact]
    public void Expand_ShowsTheDeclaredSpanWhenTheOutputMatchesIt()
    {
        CameraView view = new(Vector2.Zero, Vector2.Zero, Span, ViewportFit.Expand);

        Assert.Equal(new Rect(-160f, -90f, 160f, 90f), view.Resolve(1f, new Vector2(1280f, 720f)));
    }

    [Fact]
    public void FixedHeight_HoldsTheVerticalSpanAndLetsTheWidthFollowTheOutput()
    {
        CameraView view = new(Vector2.Zero, Vector2.Zero, Span, ViewportFit.FixedHeight);

        // A wider output shows more width; a taller one shows less. The height never moves.
        Assert.Equal(new Rect(-360f, -90f, 360f, 90f), view.Resolve(1f, Wider));
        Assert.Equal(new Rect(-45f, -90f, 45f, 90f), view.Resolve(1f, Taller));
    }

    [Fact]
    public void AnOutputWithNoArea_ResolvesTheDeclaredSpan()
    {
        CameraView view = new(Vector2.Zero, Vector2.Zero, Span, ViewportFit.Expand);

        Assert.Equal(new Rect(-160f, -90f, 160f, 90f), view.Resolve(1f, Vector2.Zero));
    }

    [Theory]
    [InlineData(0f, 180f)]
    [InlineData(320f, 0f)]
    [InlineData(-320f, 180f)]
    [InlineData(float.NaN, 180f)]
    public void ANonPositiveSpan_ResolvesToNothing(float width, float height)
    {
        CameraView view = new(Vector2.Zero, new Vector2(width, height));

        Assert.True(view.Resolve(1f, Wider).IsEmpty);
    }

    [Fact]
    public void Bounds_ClampTheViewInsideThemOnTheHorizontalAxis()
    {
        Rect room = new(0f, 0f, 1000f, 1000f);
        CameraView left = new(new Vector2(20f, 500f), Span) { Bounds = room };
        CameraView right = new(new Vector2(980f, 500f), Span) { Bounds = room };

        Assert.Equal(new Rect(0f, 410f, 320f, 590f), left.Resolve(1f, Wider));
        Assert.Equal(new Rect(680f, 410f, 1000f, 590f), right.Resolve(1f, Wider));
    }

    [Fact]
    public void Bounds_ClampTheViewInsideThemOnTheVerticalAxis()
    {
        Rect room = new(0f, 0f, 1000f, 1000f);
        CameraView top = new(new Vector2(500f, 10f), Span) { Bounds = room };
        CameraView bottom = new(new Vector2(500f, 990f), Span) { Bounds = room };

        Assert.Equal(new Rect(340f, 0f, 660f, 180f), top.Resolve(1f, Wider));
        Assert.Equal(new Rect(340f, 820f, 660f, 1000f), bottom.Resolve(1f, Wider));
    }

    [Fact]
    public void Bounds_LeaveAViewAlreadyInsideThemUntouched()
    {
        CameraView view = new(new Vector2(500f, 500f), Span) { Bounds = new Rect(0f, 0f, 1000f, 1000f) };

        Assert.Equal(new Rect(340f, 410f, 660f, 590f), view.Resolve(1f, Wider));
    }

    // Clamping an overshooting axis would pin one edge to the bounds and show world past the other.
    [Fact]
    public void Bounds_CentreAnAxisTheViewIsLargerThan()
    {
        CameraView view = new(Vector2.Zero, Span) { Bounds = new Rect(100f, 100f, 300f, 1000f) };

        // 320 wide against a 200-wide room: centred on it, overhanging both edges equally. The
        // vertical axis fits, so it clamps as usual.
        Assert.Equal(new Rect(40f, 100f, 360f, 280f), view.Resolve(1f, Wider));
    }

    // The confinement is the renderer's: the framing target the simulation settled is untouched.
    [Fact]
    public void Bounds_DoNotMoveTheCameraCentre()
    {
        CameraView view = new(new Vector2(-9000f, -9000f), Span) { Bounds = new Rect(0f, 0f, 1000f, 1000f) };

        Assert.Equal(new Vector2(-9000f, -9000f), view.Center);
        Assert.Equal(new Rect(0f, 0f, 320f, 180f), view.Resolve(1f, Wider));
    }

    [Fact]
    public void Bounds_ConfineAFitThatRevealedMoreWorld()
    {
        CameraView view = new(Vector2.Zero, Vector2.Zero, Span, ViewportFit.Expand)
        {
            Bounds = new Rect(0f, 0f, 1000f, 1000f),
        };

        // 720 wide once expanded, still inside the 1000-wide room, so it clamps rather than centres.
        Assert.Equal(new Rect(0f, 0f, 720f, 180f), view.Resolve(1f, Wider));
    }

    // The reviewer's case: the raw sweep around an unclamped centre excludes world the confined
    // view shows, which cost a shipped frame its top row and left column to culling.
    [Fact]
    public void SweptBounds_CoverTheConfinedViewRatherThanTheRawCentres()
    {
        CameraView view = new(new Vector2(20f, 500f), Span) { Bounds = new Rect(0f, 0f, 1000f, 1000f) };

        Assert.Equal(new Rect(0f, 410f, 320f, 590f), view.Resolve(1f, Wider));
        Assert.Equal(new Rect(0f, 410f, 320f, 590f), view.SweptBounds);
    }

    // A camera whose centre the game clamped by hand and one confined by Bounds must cull
    // identically, or the same room draws two different frames across the migration.
    [Theory]
    [InlineData(20f, 500f)]
    [InlineData(980f, 500f)]
    [InlineData(500f, 10f)]
    [InlineData(-9000f, 990f)]
    [InlineData(500f, 500f)]
    public void SweptBounds_UnderLetterbox_MatchAHandClampedCameraWithNoBounds(float x, float y)
    {
        Rect room = new(0f, 0f, 1000f, 1000f);
        CameraView confined = new(new Vector2(x, y), Span) { Bounds = room };

        Vector2 clamped = new(
            Math.Clamp(x, room.Left + 160f, room.Right - 160f),
            Math.Clamp(y, room.Top + 90f, room.Bottom - 90f));

        Assert.Equal(new CameraView(clamped, Span).SweptBounds, confined.SweptBounds);
        Assert.Equal(new CameraView(clamped, Span).Resolve(1f, Wider), confined.Resolve(1f, Wider));
    }

    // Interpolation runs inside the confinement, so the sweep has to cover every alpha between the
    // two settled centres and not just its endpoints.
    [Fact]
    public void SweptBounds_CoverEveryInterpolatedFrameOfTheStep()
    {
        CameraView view = new(new Vector2(-500f, 500f), new Vector2(1500f, 500f), Span)
        {
            Bounds = new Rect(0f, 0f, 1000f, 1000f),
        };

        Rect swept = view.SweptBounds;

        for (int step = 0; step <= 20; step++)
        {
            Rect frame = view.Resolve(step / 20f, Wider);

            Assert.True(swept.Left <= frame.Left && swept.Right >= frame.Right);
            Assert.True(swept.Top <= frame.Top && swept.Bottom >= frame.Bottom);
        }
    }

    // A span the bounds cannot hold overhangs them, and the sweep has to overhang with it.
    [Fact]
    public void SweptBounds_CoverAViewLargerThanItsBounds()
    {
        CameraView view = new(Vector2.Zero, Span) { Bounds = new Rect(100f, 100f, 300f, 1000f) };

        Rect swept = view.SweptBounds;
        Rect frame = view.Resolve(1f, Wider);

        Assert.Equal(frame, swept);
        Assert.Equal(40f, swept.Left);
    }

    [Fact]
    public void SweptBounds_CoverTheWorldAFitThatFollowsTheOutputCanReveal()
    {
        CameraView letterbox = new(Vector2.Zero, Span);
        CameraView expand = new(Vector2.Zero, Vector2.Zero, Span, ViewportFit.Expand);

        Assert.Equal(new Rect(-160f, -90f, 160f, 90f), letterbox.SweptBounds);

        // Anything the expanded view resolves on a plausible output has to survive culling.
        Rect culled = expand.SweptBounds;
        Rect resolved = expand.Resolve(1f, Wider);

        Assert.True(culled.Left <= resolved.Left && culled.Right >= resolved.Right);
        Assert.True(culled.Top <= resolved.Top && culled.Bottom >= resolved.Bottom);
    }
}
