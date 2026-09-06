using Capsule.Input;

namespace Capsule.Tests.Input;

public sealed class InputTapeTests
{
    [Fact]
    public void ToText_RunLengthEncodesAndParsesBackToTheSameTape()
    {
        InputTape tape = new InputScript()
            .Wait(600)
            .Down(Key.W)
            .Down(PadButton.South)
            .Axis(PadAxis.LeftStickX, -0.3f)
            .Wait(2)
            .Up(Key.W)
            .Tap(Key.Escape)
            .Build();

        string text = tape.ToText();

        Assert.Equal(
            """
            600
            2 W Pad.South Axis.LeftStickX=-0.3
            1 Escape Pad.South Axis.LeftStickX=-0.3

            """.ReplaceLineEndings("\n"),
            text);
        Assert.Equal(tape, InputTape.Parse(text));
    }

    // Round-trip formatting, not a fixed number of digits: a recorded stick position that the
    // shortest form did not capture would replay as a different run.
    [Theory]
    [InlineData(0.1f)]
    [InlineData(-0.7f)]
    [InlineData(1f)]
    [InlineData(float.Epsilon)]
    [InlineData(0.12345678f)]
    public void ToText_RoundTripsAnAxisValueExactly(float value)
    {
        InputTape tape = new InputScript().Axis(PadAxis.RightStickY, value).Wait(1).Build();

        InputTape parsed = InputTape.Parse(tape.ToText());

        Assert.Equal(value, parsed[0].Axis(PadAxis.RightStickY));
        Assert.Equal(tape, parsed);
    }

    // A snapshot accepts every value below its capacity, named by the enum or not, so the text has
    // to carry the unnamed ones too or a round trip would silently drop them.
    [Fact]
    public void ToText_RoundTripsValuesTheEnumsDoNotName()
    {
        InputTape tape = InputTape.Of(
            DeviceSnapshot.Empty
                .With((Key)(DeviceSnapshot.Capacity - 1))
                .With((PadButton)(DeviceSnapshot.PadCapacity - 1)));

        string text = tape.ToText();

        Assert.Equal("1 Key.127 Pad.31\n", text);
        Assert.Equal(tape, InputTape.Parse(text));
    }

    [Fact]
    public void ToText_OfAnEmptyTape_IsEmptyText()
    {
        Assert.Equal(string.Empty, InputTape.Empty.ToText());
        Assert.Equal(InputTape.Empty, InputTape.Parse(string.Empty));
    }

    [Fact]
    public void Parse_IgnoresBlankLinesAndComments()
    {
        InputTape tape = InputTape.Parse("# a recorded run\n\n  \n2 Space\n");

        Assert.Equal(new InputScript().Down(Key.Space).Wait(2).Build(), tape);
    }

    [Fact]
    public void Parse_AcceptsTokensInAnyOrder()
    {
        InputTape tape = InputTape.Parse("1 Axis.LeftTrigger=0.5 Pad.North A");

        Assert.Equal(
            new InputScript().Down(Key.A).Down(PadButton.North).Axis(PadAxis.LeftTrigger, 0.5f).Wait(1).Build(),
            tape);
    }

    [Theory]
    [InlineData("1 A\nNoCount B\n", 2)]
    [InlineData("1 A\n0 B\n", 2)]
    [InlineData("# lead\n\n1 A\n1 Nonesuch\n", 4)]
    [InlineData("1 Pad.Nonesuch\n", 1)]
    [InlineData("1 Axis.LeftStickX\n", 1)]
    [InlineData("1 Axis.Nonesuch=0.5\n", 1)]
    [InlineData("1 Axis.LeftStickX=up\n", 1)]
    [InlineData("1 Axis.LeftTrigger=-0.5\n", 1)]
    [InlineData("1 A A\n", 1)]
    [InlineData("1 Axis.LeftStickX=0.5 Axis.LeftStickX=0.5\n", 1)]
    [InlineData("1 Axis.LeftStickX=0 Axis.LeftStickX=1\n", 1)]
    [InlineData("1 Key.128\n", 1)]
    [InlineData("1 Pad.32\n", 1)]
    public void Parse_OfAMalformedLine_NamesItsNumber(string text, int line)
    {
        FormatException failure = Assert.Throws<FormatException>(() => InputTape.Parse(text));

        Assert.Contains($"line {line}", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Equality_IsByContent()
    {
        InputTape tape = new InputScript().Down(Key.A).Wait(3).Build();

        Assert.Equal(tape, new InputScript().Down(Key.A).Wait(3).Build());
        Assert.Equal(tape.GetHashCode(), new InputScript().Down(Key.A).Wait(3).Build().GetHashCode());
        Assert.NotEqual(tape, new InputScript().Down(Key.A).Wait(4).Build());
        Assert.NotEqual(tape, new InputScript().Down(Key.B).Wait(3).Build());
    }
}
