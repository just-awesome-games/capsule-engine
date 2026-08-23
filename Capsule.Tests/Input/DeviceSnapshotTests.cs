using Capsule.Input;

namespace Capsule.Tests.Input;

public sealed class DeviceSnapshotTests
{
    [Fact]
    public void EveryKey_FitsTheBitset()
    {
        Assert.All(Enum.GetValues<Key>(), key => Assert.InRange((int)key, 0, DeviceSnapshot.Capacity - 1));
    }

    [Fact]
    public void ADefaultSnapshot_IsEmpty()
    {
        DeviceSnapshot snapshot = default;

        Assert.True(snapshot.IsEmpty);
        Assert.Equal(DeviceSnapshot.Empty, snapshot);
    }

    [Fact]
    public void Of_HoldsEveryKeyItWasGiven()
    {
        DeviceSnapshot snapshot = DeviceSnapshot.Of(Key.Escape, Key.LeftShift, Key.F12);

        Assert.True(snapshot.IsDown(Key.Escape));
        Assert.True(snapshot.IsDown(Key.LeftShift));
        Assert.True(snapshot.IsDown(Key.F12));
        Assert.False(snapshot.IsDown(Key.A));
        Assert.False(snapshot.IsEmpty);
    }

    [Fact]
    public void Of_NoKeys_IsEmpty()
    {
        Assert.True(DeviceSnapshot.Of().IsEmpty);
    }

    [Fact]
    public void With_AddsAKeyWithoutDisturbingTheRest()
    {
        DeviceSnapshot snapshot = DeviceSnapshot.Of(Key.A).With(Key.B);

        Assert.True(snapshot.IsDown(Key.A));
        Assert.True(snapshot.IsDown(Key.B));
    }

    [Fact]
    public void With_IsIdempotent()
    {
        Assert.Equal(DeviceSnapshot.Of(Key.A), DeviceSnapshot.Of(Key.A).With(Key.A));
    }

    [Fact]
    public void Without_ReleasesOnlyThatKey()
    {
        DeviceSnapshot snapshot = DeviceSnapshot.Of(Key.A, Key.B).Without(Key.A);

        Assert.False(snapshot.IsDown(Key.A));
        Assert.True(snapshot.IsDown(Key.B));
    }

    [Fact]
    public void Without_AKeyThatIsUp_ChangesNothing()
    {
        Assert.Equal(DeviceSnapshot.Of(Key.B), DeviceSnapshot.Of(Key.B).Without(Key.A));
    }

    [Fact]
    public void Union_HoldsTheKeysOfBothSnapshots()
    {
        DeviceSnapshot snapshot = DeviceSnapshot.Of(Key.A, Key.B).Union(DeviceSnapshot.Of(Key.B, Key.C));

        Assert.Equal(DeviceSnapshot.Of(Key.A, Key.B, Key.C), snapshot);
    }

    [Fact]
    public void Union_WithAnEmptySnapshot_ChangesNothing()
    {
        Assert.Equal(DeviceSnapshot.Of(Key.A), DeviceSnapshot.Of(Key.A).Union(DeviceSnapshot.Empty));
        Assert.Equal(DeviceSnapshot.Of(Key.A), DeviceSnapshot.Empty.Union(DeviceSnapshot.Of(Key.A)));
    }

    [Fact]
    public void None_IsNeverAMember()
    {
        DeviceSnapshot snapshot = DeviceSnapshot.Of(Key.None);

        Assert.True(snapshot.IsEmpty);
        Assert.False(snapshot.IsDown(Key.None));
    }

    [Fact]
    public void KeyOrder_DoesNotAffectTheValue()
    {
        Assert.Equal(DeviceSnapshot.Of(Key.A, Key.Z), DeviceSnapshot.Of(Key.Z, Key.A));
    }

    [Fact]
    public void Snapshots_CompareByTheKeysHeld()
    {
        DeviceSnapshot held = DeviceSnapshot.Of(Key.A);
        DeviceSnapshot other = DeviceSnapshot.Of(Key.B);

        Assert.True(held == DeviceSnapshot.Of(Key.A));
        Assert.True(held != other);
        Assert.True(held.Equals((object)DeviceSnapshot.Of(Key.A)));
        Assert.False(held.Equals("not a snapshot"));
        Assert.Equal(DeviceSnapshot.Of(Key.A).GetHashCode(), held.GetHashCode());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(DeviceSnapshot.Capacity)]
    public void AnUnrepresentableKey_Throws(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DeviceSnapshot.Empty.With((Key)value));
    }
}
