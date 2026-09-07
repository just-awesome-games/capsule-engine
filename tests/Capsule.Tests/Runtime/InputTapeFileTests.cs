using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.Input;

namespace Capsule.Tests.Runtime;

public sealed class InputTapeFileTests
{
    private const int HeaderBytes = 6;
    private const int DownBytes = 16;

    // The layout of docs/headless-play.md, written out by hand so a matching reader and writer
    // mistake cannot pass: magic, version, then one record of held keys, held buttons, and six
    // axes, every field little-endian. FixtureTape is the same step in engine terms.
    private static readonly byte[] FixtureBytes =
    [
        0x43, 0x54, 0x41, 0x50,                         // "CTAP"
        0x01, 0x00,                                     // version 1
        0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, // keys: A (bit 1) and F12 (bit 63)
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x02, 0x00, 0x01, 0x00,                         // pad: DPadUp (bit 1) and Select (bit 16)
        0x00, 0x00, 0x80, 0xBF,                         // LeftStickX -1
        0x00, 0x00, 0x00, 0x3F,                         // LeftStickY 0.5
        0x00, 0x00, 0x80, 0x3F,                         // RightStickX 1
        0x00, 0x00, 0x80, 0xBE,                         // RightStickY -0.25
        0x00, 0x00, 0x40, 0x3F,                         // LeftTrigger 0.75
        0x00, 0x00, 0x00, 0x3E,                         // RightTrigger 0.125
    ];

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

    [Fact]
    public void Write_OfTheFixtureTape_IsTheFixtureBytes()
    {
        Assert.Equal(FixtureBytes, Written(FixtureTape()));
    }

    [Fact]
    public void Read_OfTheFixtureBytes_IsTheFixtureTape()
    {
        using MemoryStream stream = new(FixtureBytes);

        Assert.Equal(FixtureTape(), InputTapeFile.Read(stream));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(1.5f)]
    [InlineData(-1.5f)]
    public void Read_OfAStickOutsideItsRange_Throws(float value)
    {
        Assert.Throws<InputTapeFormatException>(() => ReadWithAxis(PadAxis.LeftStickX, value));
    }

    [Fact]
    public void Read_OfANegativeTrigger_Throws()
    {
        Assert.Throws<InputTapeFormatException>(() => ReadWithAxis(PadAxis.RightTrigger, -0.5f));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(DownBytes)]
    public void Read_OfABitNoMemberNames_Throws(int fieldOffset)
    {
        byte[] bytes = Written(new InputScript().Wait(1).Build());
        bytes[HeaderBytes + fieldOffset] |= 1;

        using MemoryStream stream = new(bytes);

        Assert.Throws<InputTapeFormatException>(() => InputTapeFile.Read(stream));
    }

    // A one-step tape whose step reads value on axis, encoded past the codec so that the reader,
    // not the writer, is what refuses it.
    private static InputTape ReadWithAxis(PadAxis axis, float value)
    {
        byte[] bytes = Written(new InputScript().Wait(1).Build());
        int offset = HeaderBytes + DownBytes + sizeof(uint) + (((int)axis - 1) * sizeof(float));
        BitConverter.TryWriteBytes(bytes.AsSpan(offset), value);

        using MemoryStream stream = new(bytes);

        return InputTapeFile.Read(stream);
    }

    private static InputTape FixtureTape() =>
        InputTape.Of(DeviceSnapshot.Empty
            .With(Key.A)
            .With(Key.F12)
            .With(PadButton.DPadUp)
            .With(PadButton.Select)
            .WithAxis(PadAxis.LeftStickX, -1f)
            .WithAxis(PadAxis.LeftStickY, 0.5f)
            .WithAxis(PadAxis.RightStickX, 1f)
            .WithAxis(PadAxis.RightStickY, -0.25f)
            .WithAxis(PadAxis.LeftTrigger, 0.75f)
            .WithAxis(PadAxis.RightTrigger, 0.125f));

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
