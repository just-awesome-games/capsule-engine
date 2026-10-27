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
        Assert.Contains("--gpu-driver", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Update the graphics driver", message, StringComparison.Ordinal);
    }

    // A driver the player named and this machine cannot draw through is the player's choice to undo,
    // not a GPU shortfall. The message names the driver, SDL's cause and the drivers the build holds.
    [Fact]
    public void DriverRefused_NamesTheDriverTheCauseAndTheDriversOnOffer()
    {
        string message = GpuDevice.DriverRefused("metal", "SDL_HINT_GPU_DRIVER metal unsupported!", "direct3d12, vulkan");

        Assert.Contains("'metal'", message, StringComparison.Ordinal);
        Assert.Contains("unsupported", message, StringComparison.Ordinal);
        Assert.Contains("direct3d12, vulkan", message, StringComparison.Ordinal);
        Assert.Contains("leave the flag out", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Update the graphics driver", message, StringComparison.Ordinal);
    }
}
