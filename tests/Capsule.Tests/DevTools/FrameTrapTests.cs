using Capsule.Runtime.DevTools;

namespace Capsule.Tests.DevTools;

// The trap's recording and dump draw on a GPU, which the suite does not assume. Its ring's length needs none.
public sealed class FrameTrapTests
{
    // Two seconds at the display's rate, 60 where it reports none, within 1.5 GB and never under 16 frames.
    [Theory]
    [InlineData(165.0, 256, 224, 330)]
    [InlineData(0.0, 256, 224, 120)]
    [InlineData(165.0, 2560, 1440, 109)]
    [InlineData(60.0, 7680, 4320, 16)]
    public void RingLength_HoldsTwoSecondsWithinItsBudget_AndNeverUnderSixteen(double rate, int width, int height, int expected)
    {
        Assert.Equal(expected, FrameTrap.RingLength(rate, width, height));
    }
}
