using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

public sealed class DisplayTests
{
    [Theory]
    [InlineData(1920f, 1080f, 1280, 1024, 1f, 1280, 720)]
    [InlineData(1080f, 1920f, 1920, 1080, 1f, 608, 1080)]
    [InlineData(1280.4f, 719.6f, 1920, 1080, 1f, 1280, 720)]
    [InlineData(1280f, 720f, 2560, 1440, 1.5f, 1920, 1080)]
    [InlineData(1024f, 896f, 1920, 1080, 1.5f, 1234, 1080)]
    public void WindowPixels_ScalesThenRoundsAndShrinksAWindowLargerThanTheDisplayKeepingItsAspect(
        float requestedWidth, float requestedHeight, int displayWidth, int displayHeight, float displayScale, int width, int height)
    {
        Display display = new() { WindowSize = new Vector2(requestedWidth, requestedHeight) };

        Assert.Equal((width, height), display.WindowPixels(displayWidth, displayHeight, displayScale));
    }
}
