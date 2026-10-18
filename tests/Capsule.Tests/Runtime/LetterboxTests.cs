using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Tests.Runtime;

public sealed class LetterboxTests
{
    // A container the content's aspect does not match gets bars on the axis with room to spare:
    // pillars on a wider container, a letterbox on a taller one.
    [Theory]
    [InlineData(3440, 1440, 440, 0, 2560, 1440, 8f)]
    [InlineData(1920, 1480, 0, 200, 1920, 1080, 6f)]
    public void Fit_BarsTheAxisTheContentDoesNotFill(
        int containerWidth,
        int containerHeight,
        int x,
        int y,
        int width,
        int height,
        float scale) =>
        Assert.Equal(
            new Letterbox(x, y, width, height, scale),
            Letterbox.Fit(320f, 180f, containerWidth, containerHeight));

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
