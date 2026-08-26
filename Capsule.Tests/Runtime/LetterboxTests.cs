using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Runtime;

/// <summary>
/// The presentation rule the device path is built on and cannot assert for itself: content
/// fitted into a differently-shaped container is scaled uniformly and centred, never
/// stretched. Both stages of that path — camera into surface, surface into back buffer —
/// take their scale from here, so these are the invariants a distorted frame would break.
/// </summary>
public sealed class LetterboxTests
{
    // One scale, both axes: the invariant the rest of the presentation path rests on.
    [Theory]
    [InlineData(320f, 180f, 1920, 1080)]
    [InlineData(320f, 180f, 1000, 1000)]
    [InlineData(4f, 3f, 3440, 1440)]
    [InlineData(16f, 9f, 640, 480)]
    public void Fit_ScalesBothAxesByTheSameFactor(float contentWidth, float contentHeight, int containerWidth, int containerHeight)
    {
        Letterbox fit = Letterbox.Fit(contentWidth, contentHeight, containerWidth, containerHeight);

        // Rounding to whole pixels is the only thing between the two, so they agree to within
        // half a pixel of the ideal on each axis.
        Assert.Equal(contentWidth * fit.Scale, fit.Width, 0.5);
        Assert.Equal(contentHeight * fit.Scale, fit.Height, 0.5);
    }

    // An ultrawide window against a 16:9 camera pillarboxes: height binds and the slack lands
    // on the sides. Getting the binding axis backwards is the classic error here, and it is
    // invisible on a square container.
    [Fact]
    public void Fit_PillarboxesAContainerWiderThanTheContent()
    {
        Letterbox fit = Letterbox.Fit(320f, 180f, 3440, 1440);

        Assert.Equal(new Letterbox(440, 0, 2560, 1440, 8f), fit);
    }

    // The mirror case: a container proportionally taller binds on width, bars top and bottom.
    [Fact]
    public void Fit_LetterboxesAContainerTallerThanTheContent()
    {
        Letterbox fit = Letterbox.Fit(320f, 180f, 1920, 1480);

        Assert.Equal(new Letterbox(0, 200, 1920, 1080, 6f), fit);
    }

    // Centred, not corner-anchored: the two bars differ by at most the odd pixel integer
    // division cannot split.
    [Theory]
    [InlineData(320f, 180f, 1000, 1000)]
    [InlineData(320f, 180f, 1001, 777)]
    [InlineData(1f, 1f, 1920, 1081)]
    public void Fit_LeavesEqualBarsOnEitherSide(float contentWidth, float contentHeight, int containerWidth, int containerHeight)
    {
        Letterbox fit = Letterbox.Fit(contentWidth, contentHeight, containerWidth, containerHeight);

        Assert.InRange(containerWidth - fit.Width - (2 * fit.X), 0, 1);
        Assert.InRange(containerHeight - fit.Height - (2 * fit.Y), 0, 1);
    }

    // Matching aspects must fill exactly, with no bar and no rounding gap, at any multiple.
    [Theory]
    [InlineData(320f, 180f, 1920, 1080)]
    [InlineData(320f, 180f, 1600, 900)]
    [InlineData(320f, 180f, 800, 450)]
    public void Fit_FillsTheContainerExactlyWhenTheAspectsMatch(float contentWidth, float contentHeight, int containerWidth, int containerHeight)
    {
        Letterbox fit = Letterbox.Fit(contentWidth, contentHeight, containerWidth, containerHeight);

        Assert.Equal(new Letterbox(0, 0, containerWidth, containerHeight, containerWidth / contentWidth), fit);
    }

    // A minimised window presents no area, a default CameraView spans nothing, and a NaN
    // extent passes every comparison-based guard. Each must fall out as an empty fit, because
    // the caller's next act is to divide by it and hand it to a viewport.
    [Theory]
    [InlineData(320f, 180f, 0, 1080)]
    [InlineData(320f, 180f, 1920, 0)]
    [InlineData(320f, 180f, -1920, -1080)]
    [InlineData(0f, 0f, 1920, 1080)]
    [InlineData(float.NaN, 180f, 1920, 1080)]
    [InlineData(320f, float.NaN, 1920, 1080)]
    public void Fit_IsEmptyForDegenerateGeometry(float contentWidth, float contentHeight, int containerWidth, int containerHeight)
    {
        Assert.True(Letterbox.Fit(contentWidth, contentHeight, containerWidth, containerHeight).IsEmpty);
    }

    // Content so much wider than the container that the bound axis rounds away entirely: the
    // fit reports empty rather than a zero-height viewport.
    [Fact]
    public void Fit_IsEmptyWhenTheFittedContentRoundsToNothing()
    {
        Assert.True(Letterbox.Fit(1000f, 1f, 10, 10).IsEmpty);
    }
}
