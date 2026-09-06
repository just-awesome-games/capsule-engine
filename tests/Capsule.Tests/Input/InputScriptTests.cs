using Capsule.Input;

namespace Capsule.Tests.Input;

public sealed class InputScriptTests
{
    [Fact]
    public void Tap_HoldsForExactlyOneStepOnTopOfWhatIsAlreadyHeld()
    {
        InputTape tape = new InputScript()
            .Down(Key.LeftShift)
            .Wait(1)
            .Tap(Key.Space)
            .Wait(1)
            .Build();

        Assert.Equal(3, tape.Count);
        Assert.False(tape[0].IsDown(Key.Space));
        Assert.True(tape[1].IsDown(Key.Space));
        Assert.False(tape[2].IsDown(Key.Space));
        Assert.All(tape, snapshot => Assert.True(snapshot.IsDown(Key.LeftShift)));
    }

    [Fact]
    public void Wait_EmitsTheHeldStateAndEditsEmitNothingOfTheirOwn()
    {
        InputTape tape = new InputScript()
            .Down(Key.A)
            .Down(PadButton.LeftShoulder)
            .Axis(PadAxis.LeftStickY, 0.5f)
            .Up(Key.A)
            .Wait(2)
            .Build();

        Assert.Equal(2, tape.Count);
        Assert.All(
            tape,
            snapshot =>
            {
                Assert.False(snapshot.IsDown(Key.A));
                Assert.True(snapshot.IsDown(PadButton.LeftShoulder));
                Assert.Equal(0.5f, snapshot.Axis(PadAxis.LeftStickY));
            });
    }

    [Fact]
    public void Tap_OfSomethingAlreadyHeld_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => new InputScript().Down(Key.Space).Tap(Key.Space));
        Assert.Throws<InvalidOperationException>(() => new InputScript().Down(PadButton.South).Tap(PadButton.South));
    }

    [Fact]
    public void Wait_RejectsANegativeStepCountAndEmitsNothingForZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InputScript().Wait(-1));
        Assert.Equal(InputTape.Empty, new InputScript().Down(Key.A).Wait(0).Build());
    }
}
