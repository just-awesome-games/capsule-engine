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
}
