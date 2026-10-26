using Capsule.Runtime.Gpu;

namespace Capsule.Tests.Runtime;

public sealed class GpuDeviceTests
{
    // Vulkan opens a device and then refuses the window while every display is off. That is no GPU
    // shortfall. The message names the driver and SDL's cause and points at the display and the
    // driver choice, never at a driver update.
    [Fact]
    public void ClaimRefused_NamesDriverAndCause_AndPointsAtDisplayAndDriverChoice()
    {
        string message = GpuDevice.ClaimRefused("vulkan", "Unhandled VkResult");

        Assert.Contains("vulkan GPU", message, StringComparison.Ordinal);
        Assert.Contains("Unhandled VkResult", message, StringComparison.Ordinal);
        Assert.Contains("display", message, StringComparison.Ordinal);
        Assert.Contains("SDL_GPU_DRIVER", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Update the graphics driver", message, StringComparison.Ordinal);
    }
}
