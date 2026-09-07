using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.Input;

namespace Capsule.Tests.Runtime;

public sealed class InputTapeFileTests
{
    [Fact]
    public void Read_OfWhatWriteWrote_IsTheSameTape()
    {
        InputTape tape = new InputScript()
            .Wait(3)
            .Down(Key.W)
            .Down(PadButton.South)
            .Axis(PadAxis.LeftStickX, -0.3f)
            .Wait(2)
            .Up(Key.W)
            .Tap(Key.Escape)
            .Build();

        Assert.Equal(tape, RoundTrip(tape));
    }

    [Fact]
    public void Read_OfAnEmptyTape_IsTheEmptyTape()
    {
        Assert.Equal(InputTape.Empty, RoundTrip(InputTape.Empty));
    }

    [Fact]
    public void Read_OfBytesThatAreNoTape_Throws()
    {
        using MemoryStream stream = new("not a tape at all"u8.ToArray());

        Assert.Throws<InputTapeFormatException>(() => InputTapeFile.Read(stream));
    }

    [Fact]
    public void Read_OfAnUnsupportedVersion_Throws()
    {
        byte[] bytes = Written(InputTape.Empty);
        bytes[4]++;

        using MemoryStream stream = new(bytes);

        Assert.Throws<InputTapeFormatException>(() => InputTapeFile.Read(stream));
    }

    [Fact]
    public void Read_OfABodyEndingMidStep_Throws()
    {
        byte[] bytes = Written(new InputScript().Wait(2).Build());

        using MemoryStream stream = new(bytes[..^1]);

        Assert.Throws<InputTapeFormatException>(() => InputTapeFile.Read(stream));
    }

    private static byte[] Written(InputTape tape)
    {
        using MemoryStream stream = new();
        InputTapeFile.Write(stream, tape);

        return stream.ToArray();
    }

    private static InputTape RoundTrip(InputTape tape)
    {
        using MemoryStream stream = new(Written(tape));

        return InputTapeFile.Read(stream);
    }
}
