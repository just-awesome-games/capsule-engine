using Capsule.Input;

namespace Capsule.Tests.Input;

public sealed class DeviceSnapshotTests
{
    [Fact]
    public void EveryDeviceConstant_FitsItsBitset()
    {
        Assert.All(Enum.GetValues<Key>(), key => Assert.InRange((int)key, 0, DeviceSnapshot.Capacity - 1));
        Assert.All(Enum.GetValues<PadButton>(), button => Assert.InRange((int)button, 0, DeviceSnapshot.PadCapacity - 1));
        Assert.All(Enum.GetValues<MouseButton>(), button => Assert.InRange((int)button, 0, DeviceSnapshot.MouseCapacity - 1));
    }

    [Fact]
    public void Without_ReleasesOnlyThatKey()
    {
        DeviceSnapshot snapshot = DeviceSnapshot.Of(Key.A, Key.B, Key.F12).Without(Key.A);

        Assert.False(snapshot.IsDown(Key.A));
        Assert.True(snapshot.IsDown(Key.B));
        Assert.True(snapshot.IsDown(Key.F12));
    }

    [Fact]
    public void EveryPadButton_IsItsOwnBitAndSharesNoneWithAKey()
    {
        DeviceSnapshot keysOnly = DeviceSnapshot.Of(Key.A, Key.Space);

        Assert.All(Enum.GetValues<PadButton>(), button => Assert.False(keysOnly.IsDown(button)));

        foreach (PadButton button in Enum.GetValues<PadButton>())
        {
            if (button == PadButton.None)
            {
                continue;
            }

            DeviceSnapshot padOnly = DeviceSnapshot.Empty.With(button);

            Assert.All(Enum.GetValues<PadButton>(), other => Assert.Equal(other == button, padOnly.IsDown(other)));
            Assert.All(Enum.GetValues<Key>(), key => Assert.False(padOnly.IsDown(key)));
            Assert.NotEqual(keysOnly, padOnly);
        }
    }

    [Fact]
    public void None_IsNeverAMember()
    {
        DeviceSnapshot keys = DeviceSnapshot.Of(Key.None);
        DeviceSnapshot pad = DeviceSnapshot.Empty.With(PadButton.None);

        Assert.True(keys.IsEmpty);
        Assert.False(keys.IsDown(Key.None));
        Assert.True(pad.IsEmpty);
        Assert.False(pad.IsDown(PadButton.None));
    }

    [Fact]
    public void WithAxis_SetsThatAxisAndLeavesTheOthers()
    {
        DeviceSnapshot snapshot = DeviceSnapshot.Empty
            .WithAxis(PadAxis.LeftStickX, -0.5f)
            .WithAxis(PadAxis.RightTrigger, 0.25f);

        Assert.Equal(-0.5f, snapshot.Axis(PadAxis.LeftStickX), InputFixtures.Tolerance);
        Assert.Equal(0.25f, snapshot.Axis(PadAxis.RightTrigger), InputFixtures.Tolerance);
        Assert.Equal(0f, snapshot.Axis(PadAxis.LeftStickY));
        Assert.Equal(0f, snapshot.Axis(PadAxis.RightStickX));
        Assert.Equal(0f, snapshot.Axis(PadAxis.RightStickY));
        Assert.Equal(0f, snapshot.Axis(PadAxis.LeftTrigger));
        Assert.False(snapshot.IsEmpty);
    }

    [Theory]
    [InlineData(PadAxis.LeftStickX, -1f)]
    [InlineData(PadAxis.LeftStickY, 1f)]
    [InlineData(PadAxis.LeftTrigger, 0f)]
    [InlineData(PadAxis.RightTrigger, 1f)]
    public void WithAxis_AcceptsTheEndsOfTheRange(PadAxis axis, float value)
    {
        Assert.Equal(value, DeviceSnapshot.Empty.WithAxis(axis, value).Axis(axis), InputFixtures.Tolerance);
    }

    [Theory]
    [InlineData(PadAxis.LeftStickX, -1.0001f)]
    [InlineData(PadAxis.LeftStickY, 1.0001f)]
    [InlineData(PadAxis.LeftTrigger, -0.0001f)]
    [InlineData(PadAxis.RightTrigger, 1.0001f)]
    [InlineData(PadAxis.RightStickX, float.NaN)]
    public void WithAxis_RejectsAValueOutsideTheAxisRange(PadAxis axis, float value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DeviceSnapshot.Empty.WithAxis(axis, value));
    }

    [Fact]
    public void AnAxisAlone_DistinguishesTwoSnapshots()
    {
        DeviceSnapshot held = DeviceSnapshot.Empty.WithAxis(PadAxis.RightStickY, 0.5f);

        Assert.NotEqual(DeviceSnapshot.Empty, held);
        Assert.NotEqual(DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickY, 0.5f), held);
        Assert.Equal(DeviceSnapshot.Empty.WithAxis(PadAxis.RightStickY, 0.5f), held);
        Assert.Equal(DeviceSnapshot.Empty.WithAxis(PadAxis.RightStickY, 0.5f).GetHashCode(), held.GetHashCode());
    }

    [Fact]
    public void AButtonPastItsDeviceCapacity_ThrowsOnAWriteAndReadsUp()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DeviceSnapshot.Empty.With((Key)DeviceSnapshot.Capacity));
        Assert.Throws<ArgumentOutOfRangeException>(() => DeviceSnapshot.Empty.With((PadButton)DeviceSnapshot.PadCapacity));
        Assert.Throws<ArgumentOutOfRangeException>(() => DeviceSnapshot.Empty.With((MouseButton)DeviceSnapshot.MouseCapacity));
        Assert.False(DeviceSnapshot.Empty.IsDown((Key)(-1)));
    }

    // A read is on the step path and answers rather than throws. Only the write refuses the axis.
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(99)]
    public void AnUnrepresentableAxis_Throws(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DeviceSnapshot.Empty.WithAxis((PadAxis)value, 0f));
        Assert.Equal(0f, DeviceSnapshot.Empty.Axis((PadAxis)value));
    }
}
