using Capsule.Input;
using Capsule.Scenes;

namespace Capsule.Tests.Input;

public sealed class InputConfigurationTests
{
    [Fact]
    public void TheDebugMenuToggle_DefaultsToTheGraveKey()
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

    [Fact]
    public void DebugMenu_AfterTheRunHasBooted_Throws()
    {
        using SimulationHost host = new(new Scene());

        Assert.Throws<InvalidOperationException>(() => host.Run.Input.DebugMenu(Key.F1));
    }

    [Theory]
    [InlineData(float.NaN, 0.12f)]
    [InlineData(0.25f, float.NaN)]
    [InlineData(1f, 0.12f)]
    [InlineData(-0.1f, 0.12f)]
    public void GamepadDeadzones_RejectsARadiusOutsideTheUnitInterval(float stick, float trigger)
    {
        InputConfiguration configuration = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => configuration.GamepadDeadzones(stick, trigger));
    }
}
