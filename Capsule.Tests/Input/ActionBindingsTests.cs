using Capsule.Input;

namespace Capsule.Tests.Input;

public sealed class ActionBindingsTests
{
    private static readonly InputAction Jump = new("Jump");
    private static readonly InputAction Fire = new("Fire");

    [Fact]
    public void AnUnboundAction_HasNoKeysAndIsNeverDown()
    {
        ActionBindings bindings = new();

        Assert.True(bindings.KeysFor(Jump).IsEmpty);
        Assert.False(bindings.IsAnyDown(Jump, DeviceSnapshot.Of(Key.Space)));
    }

    [Fact]
    public void Bind_MakesEveryBoundKeyStandForTheAction()
    {
        ActionBindings bindings = new ActionBindings().Bind(Jump, Key.Space, Key.W);

        Assert.True(bindings.IsAnyDown(Jump, DeviceSnapshot.Of(Key.Space)));
        Assert.True(bindings.IsAnyDown(Jump, DeviceSnapshot.Of(Key.W)));
        Assert.False(bindings.IsAnyDown(Jump, DeviceSnapshot.Of(Key.A)));
    }

    [Fact]
    public void Bind_ReturnsItselfSoRegistrationsChain()
    {
        ActionBindings bindings = new ActionBindings()
            .Bind(Jump, Key.Space)
            .Bind(Fire, Key.LeftControl);

        Assert.Equal([Key.Space], bindings.KeysFor(Jump).ToArray());
        Assert.Equal([Key.LeftControl], bindings.KeysFor(Fire).ToArray());
    }

    [Fact]
    public void BindingAnActionTwice_UnionsTheKeys()
    {
        ActionBindings bindings = new ActionBindings()
            .Bind(Jump, Key.Space)
            .Bind(Jump, Key.W, Key.Space);

        Assert.Equal([Key.Space, Key.W], bindings.KeysFor(Jump).ToArray());
    }

    [Fact]
    public void Bind_RejectsAnUnnamedAction()
    {
        Assert.Throws<ArgumentException>(() => new ActionBindings().Bind(default, Key.Space));
        Assert.Throws<ArgumentException>(() => new ActionBindings().Bind(new InputAction("  "), Key.Space));
    }

    [Fact]
    public void Bind_RejectsNoKeys()
    {
        Assert.Throws<ArgumentException>(() => new ActionBindings().Bind(Jump));
    }

    [Fact]
    public void Bind_RejectsTheNoneKey()
    {
        Assert.Throws<ArgumentException>(() => new ActionBindings().Bind(Jump, Key.Space, Key.None));
    }

    [Fact]
    public void Actions_AreEqualByName()
    {
        Assert.Equal(Jump, new InputAction("Jump"));
        Assert.NotEqual(Jump, Fire);

        ActionBindings bindings = new ActionBindings().Bind(Jump, Key.Space);

        Assert.True(bindings.IsAnyDown(new InputAction("Jump"), DeviceSnapshot.Of(Key.Space)));
    }
}
