using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Runtime;

public sealed class LetterboxTests
{
    [Theory]
    [InlineData(320f, 180f, 1920, 1080)]
    [InlineData(320f, 180f, 1000, 1000)]
    [InlineData(4f, 3f, 3440, 1440)]
    [InlineData(16f, 9f, 640, 480)]
    public void Fit_ScalesBothAxesByTheSameFactor(float contentWidth, float contentHeight, int containerWidth, int containerHeight)
    {
        Letterbox fit = Letterbox.Fit(contentWidth, contentHeight, containerWidth, containerHeight);

        Assert.Equal(contentWidth * fit.Scale, fit.Width, 0.5);
        Assert.Equal(contentHeight * fit.Scale, fit.Height, 0.5);
    }

    [Fact]
    public void Fit_PillarboxesAContainerWiderThanTheContent()
    {
        Letterbox fit = Letterbox.Fit(320f, 180f, 3440, 1440);

        Assert.Equal(new Letterbox(440, 0, 2560, 1440, 8f), fit);
    }

    [Fact]
    public void Fit_LetterboxesAContainerTallerThanTheContent()
    {
        Letterbox fit = Letterbox.Fit(320f, 180f, 1920, 1480);

        Assert.Equal(new Letterbox(0, 200, 1920, 1080, 6f), fit);
    }

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

    [Theory]
    [InlineData(320f, 180f, 1920, 1080)]
    [InlineData(320f, 180f, 800, 450)]
    public void Fit_FillsTheContainerExactlyWhenTheAspectsMatch(float contentWidth, float contentHeight, int containerWidth, int containerHeight)
    {
        Letterbox fit = Letterbox.Fit(contentWidth, contentHeight, containerWidth, containerHeight);

        Assert.Equal(new Letterbox(0, 0, containerWidth, containerHeight, containerWidth / contentWidth), fit);
    }

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

    [Fact]
    public void Fit_IsEmptyWhenTheFittedContentRoundsToNothing()
    {
        Assert.True(Letterbox.Fit(1000f, 1f, 10, 10).IsEmpty);
    }

    [Theory]
    [InlineData(320, 180, 1280, 720, 4f)]
    [InlineData(320, 180, 1279, 719, 3f)]
    [InlineData(320, 180, 1366, 768, 4f)]
    [InlineData(320, 180, 3440, 1440, 8f)]
    [InlineData(320, 180, 640, 359, 1f)]
    public void FitPixels_TakesTheLargestWholeScaleThatFits(
        int contentWidth,
        int contentHeight,
        int containerWidth,
        int containerHeight,
        float expectedScale)
    {
        Letterbox fit = Letterbox.FitPixels(contentWidth, contentHeight, containerWidth, containerHeight);

        Assert.Equal(expectedScale, fit.Scale);
        Assert.Equal((int)(contentWidth * expectedScale), fit.Width);
        Assert.Equal((int)(contentHeight * expectedScale), fit.Height);
    }

    [Theory]
    [InlineData(320, 180, 1366, 768)]
    [InlineData(320, 180, 1001, 777)]
    [InlineData(320, 180, 100, 100)]
    public void FitPixels_LeavesEqualBarsOnEitherSide(int contentWidth, int contentHeight, int containerWidth, int containerHeight)
    {
        Letterbox fit = Letterbox.FitPixels(contentWidth, contentHeight, containerWidth, containerHeight);

        Assert.InRange(containerWidth - fit.Width - (2 * fit.X), 0, 1);
        Assert.InRange(containerHeight - fit.Height - (2 * fit.Y), 0, 1);
    }

    [Theory]
    [InlineData(320, 180, 160, 90)]
    [InlineData(320, 180, 319, 179)]
    public void FitPixels_FallsBackToTheFractionalFitBelowOneWholeScale(
        int contentWidth,
        int contentHeight,
        int containerWidth,
        int containerHeight)
    {
        Letterbox fit = Letterbox.FitPixels(contentWidth, contentHeight, containerWidth, containerHeight);

        Assert.Equal(Letterbox.Fit(contentWidth, contentHeight, containerWidth, containerHeight), fit);
        Assert.InRange(fit.Scale, 0f, 1f);
    }

    [Theory]
    [InlineData(320, 180, 0, 720)]
    [InlineData(320, 180, 1280, 0)]
    [InlineData(0, 180, 1280, 720)]
    [InlineData(320, 0, 1280, 720)]
    [InlineData(320, 180, -1280, -720)]
    public void FitPixels_IsEmptyForDegenerateGeometry(int contentWidth, int contentHeight, int containerWidth, int containerHeight)
    {
        Assert.True(Letterbox.FitPixels(contentWidth, contentHeight, containerWidth, containerHeight).IsEmpty);
    }
}
