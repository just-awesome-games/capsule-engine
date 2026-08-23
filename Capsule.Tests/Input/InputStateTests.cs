using Capsule.Input;

namespace Capsule.Tests.Input;

public sealed class InputStateTests
{
    private static readonly InputAction Jump = new("Jump");
    private static readonly InputAction Unbound = new("Unbound");

    private static InputState Bound(params ReadOnlySpan<Key> keys) =>
        new(new ActionBindings().Bind(Jump, keys));

    [Fact]
    public void ANewState_ReportsNothing()
    {
        InputState input = Bound(Key.Space);

        Assert.False(input.IsHeld(Jump));
        Assert.False(input.WasPressed(Jump));
        Assert.False(input.WasReleased(Jump));
    }

    [Fact]
    public void TheFirstAdvanceWithTheKeyDown_IsAPress()
    {
        InputState input = Bound(Key.Space);

        input.Advance(DeviceSnapshot.Of(Key.Space));

        Assert.True(input.IsHeld(Jump));
        Assert.True(input.WasPressed(Jump));
        Assert.False(input.WasReleased(Jump));
    }

    [Fact]
    public void HoldingAcrossSteps_IsAPressThenHeldOnly()
    {
        InputState input = Bound(Key.Space);
        DeviceSnapshot down = DeviceSnapshot.Of(Key.Space);

        input.Advance(down);
        input.Advance(down);

        Assert.True(input.IsHeld(Jump));
        Assert.False(input.WasPressed(Jump));
        Assert.False(input.WasReleased(Jump));
    }

    [Fact]
    public void ReleasingTheKey_IsAReleaseOnTheNextStepOnly()
    {
        InputState input = Bound(Key.Space);

        input.Advance(DeviceSnapshot.Of(Key.Space));
        input.Advance(DeviceSnapshot.Empty);

        Assert.False(input.IsHeld(Jump));
        Assert.False(input.WasPressed(Jump));
        Assert.True(input.WasReleased(Jump));

        input.Advance(DeviceSnapshot.Empty);

        Assert.False(input.WasReleased(Jump));
    }

    [Fact]
    public void Tapping_PressesAndReleasesOnceEach()
    {
        InputState input = Bound(Key.Space);
        int presses = 0;
        int releases = 0;

        DeviceSnapshot down = DeviceSnapshot.Of(Key.Space);
        DeviceSnapshot[] frames = [DeviceSnapshot.Empty, down, down, DeviceSnapshot.Empty, DeviceSnapshot.Empty];

        foreach (DeviceSnapshot snapshot in frames)
        {
            input.Advance(snapshot);
            presses += input.WasPressed(Jump) ? 1 : 0;
            releases += input.WasReleased(Jump) ? 1 : 0;
        }

        Assert.Equal(1, presses);
        Assert.Equal(1, releases);
    }

    [Fact]
    public void AdvancingTwiceOnOneSnapshot_FiresTheEdgeOnTheFirstStepOnly()
    {
        // The runtime samples once per frame and drains several steps against that
        // sample; an edge must not repeat across them.
        InputState input = Bound(Key.Space);
        DeviceSnapshot frame = DeviceSnapshot.Of(Key.Space);
        int presses = 0;

        for (int step = 0; step < 4; step++)
        {
            input.Advance(frame);
            presses += input.WasPressed(Jump) ? 1 : 0;
        }

        Assert.Equal(1, presses);
        Assert.True(input.IsHeld(Jump));
    }

    [Fact]
    public void AnyBoundKey_SatisfiesTheAction()
    {
        InputState input = Bound(Key.Space, Key.W);

        input.Advance(DeviceSnapshot.Of(Key.W));

        Assert.True(input.WasPressed(Jump));
        Assert.True(input.IsHeld(Jump));
    }

    [Fact]
    public void SwappingBetweenBoundKeys_IsNotAnEdge()
    {
        InputState input = Bound(Key.Space, Key.W);

        input.Advance(DeviceSnapshot.Of(Key.Space));
        input.Advance(DeviceSnapshot.Of(Key.Space, Key.W));
        input.Advance(DeviceSnapshot.Of(Key.W));

        Assert.True(input.IsHeld(Jump));
        Assert.False(input.WasPressed(Jump));
        Assert.False(input.WasReleased(Jump));
    }

    [Fact]
    public void MultiKeyRelease_WaitsForTheLastKey()
    {
        InputState input = Bound(Key.Space, Key.W);

        input.Advance(DeviceSnapshot.Of(Key.Space, Key.W));
        input.Advance(DeviceSnapshot.Of(Key.W));

        Assert.False(input.WasReleased(Jump));

        input.Advance(DeviceSnapshot.Empty);

        Assert.True(input.WasReleased(Jump));
    }

    [Fact]
    public void AnUnboundAction_NeverFires()
    {
        InputState input = Bound(Key.Space);

        input.Advance(DeviceSnapshot.Of(Key.Space, Key.Escape));

        Assert.False(input.IsHeld(Unbound));
        Assert.False(input.WasPressed(Unbound));
        Assert.False(input.WasReleased(Unbound));
    }

    [Fact]
    public void AnUnrelatedKey_DoesNotFireTheAction()
    {
        InputState input = Bound(Key.Space);

        input.Advance(DeviceSnapshot.Of(Key.Escape));

        Assert.False(input.WasPressed(Jump));
        Assert.False(input.IsHeld(Jump));
    }

    [Fact]
    public void Construction_RequiresBindings()
    {
        Assert.Throws<ArgumentNullException>(() => new InputState(null!));
    }
}
