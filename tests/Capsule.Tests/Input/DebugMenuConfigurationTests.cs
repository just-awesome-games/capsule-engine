using Capsule.Input;

namespace Capsule.Tests.Input;

public sealed class DebugMenuConfigurationTests
{
    [Fact]
    public void DefaultsToTheGraveKey()
    {
        InputConfiguration configuration = new();

        Assert.Equal((InputButton)Key.Grave, configuration.DebugMenuButton);
    }

    [Fact]
    public void DebugMenu_IsFluentAndAcceptsNoneOrAnyButton()
    {
        InputConfiguration configuration = new();

        Assert.Same(configuration, configuration.DebugMenu(PadButton.Start));
        Assert.Equal((InputButton)PadButton.Start, configuration.DebugMenuButton);

        configuration.DebugMenu(InputButton.None);

        Assert.Equal(InputButton.None, configuration.DebugMenuButton);
    }
}
