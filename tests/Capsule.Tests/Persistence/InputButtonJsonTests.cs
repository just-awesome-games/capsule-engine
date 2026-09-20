using System.Text.Json;
using Capsule.Input;

namespace Capsule.Tests.Persistence;

public sealed class InputButtonJsonTests
{
    [Theory]
    [InlineData("Key.Space")]
    [InlineData("PadButton.South")]
    [InlineData("MouseButton.Left")]
    [InlineData("StickDirection.LeftStickUp")]
    public void InputButton_RoundTripsThroughASourceGeneratedContext(string form)
    {
        InputButton button = InputButton.Parse(form);
        ButtonHolder holder = new() { Button = button };

        string json = JsonSerializer.Serialize(holder, SaveTestJsonContext.Default.ButtonHolder);
        ButtonHolder roundTripped = JsonSerializer.Deserialize(json, SaveTestJsonContext.Default.ButtonHolder)!;

        Assert.Equal(button, roundTripped.Button);
    }

    [Fact]
    public void ABadString_ReadsAsJsonException()
    {
        string json = """{"Button":"NotAButton"}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, SaveTestJsonContext.Default.ButtonHolder));
    }
}
