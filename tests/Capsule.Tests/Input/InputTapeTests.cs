using Capsule.Input;

namespace Capsule.Tests.Input;

public sealed class InputTapeTests
{
    [Fact]
    public void Indexer_OutsideTheTape_Throws()
    {
        InputTape tape = new InputScript().Wait(2).Build();

        Assert.Throws<ArgumentOutOfRangeException>(() => tape[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => tape[2]);
    }

    [Fact]
    public void Equality_IsByContent()
    {
        InputTape tape = new InputScript().Down(Key.A).Wait(3).Build();

        Assert.Equal(tape, new InputScript().Down(Key.A).Wait(3).Build());
        Assert.Equal(tape.GetHashCode(), new InputScript().Down(Key.A).Wait(3).Build().GetHashCode());
        Assert.NotEqual(tape, new InputScript().Down(Key.B).Wait(3).Build());
        Assert.NotEqual(tape, new InputScript().Down(Key.A).Wait(4).Build());
    }
}
