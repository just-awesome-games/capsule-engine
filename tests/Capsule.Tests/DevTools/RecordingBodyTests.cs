using Capsule.Input;
using Capsule.Runtime.DevTools.Recordings;

namespace Capsule.Tests.DevTools;

public sealed class RecordingBodyTests
{
    // A step whose pad buttons did not change writes no set, as every recording made before the set was
    // recorded, and reads back a full pad. A changed set is written and held until it changes again.
    [Fact]
    public void ThePadsButtons_RoundTripAndAStepWithoutThemReadsAFullPad()
    {
        DeviceSnapshot pressed = DeviceSnapshot.Empty.With(PadButton.South);
        DeviceSnapshot noTriggers = pressed.WithPadLacking(PadButton.LeftTrigger, PadButton.RightTrigger);
        DeviceSnapshot[] steps = [pressed, noTriggers, noTriggers.Without(PadButton.South)];

        ChunkedBuffer writer = new(256);
        DeviceSnapshot previous = default;
        foreach (DeviceSnapshot step in steps)
        {
            writer.WriteByte(RecordingFormat.Step);
            RecordingBody.WriteSnapshot(writer, RecordingBody.ChangeMask(in step, in previous), in step, in previous);
            previous = step;
        }

        using MemoryStream bytes = new();
        writer.CopyTo(bytes);
        RecordingBody body = new(new RecordingReader(bytes.ToArray(), 0, "test"));

        Assert.All(steps, step =>
        {
            Assert.True(body.Next(out RecordingEvent read));
            Assert.Equal(step, read.Snapshot);
        });
    }

    // A set of pad buttons, mouse buttons or lacking pad inputs is 32 bits. A wider one is damage, never
    // a set cut to its low bits.
    [Theory]
    [InlineData(RecordingFormat.PadBit)]
    [InlineData(RecordingFormat.MouseBit)]
    [InlineData(RecordingFormat.LackingBit)]
    public void AButtonSetWiderThan32Bits_IsRefusedAsDamage(int field)
    {
        ChunkedBuffer writer = new(32);
        writer.WriteByte(RecordingFormat.Step);
        writer.WriteVarint((ulong)field);
        writer.WriteVarint((1UL << 32) | 1);
        using MemoryStream bytes = new();
        writer.CopyTo(bytes);
        RecordingBody body = new(new RecordingReader(bytes.ToArray(), 0, "test"));

        InvalidDataException refused = Assert.Throws<InvalidDataException>(() => body.Next(out _));

        Assert.Contains("4294967297", refused.Message, StringComparison.Ordinal);
    }
}
