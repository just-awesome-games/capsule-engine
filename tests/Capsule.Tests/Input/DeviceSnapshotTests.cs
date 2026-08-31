using Capsule.Input;

namespace Capsule.Tests.Input;

public sealed class DeviceSnapshotTests
{
    private const float Tolerance = 1e-6f;

    [Fact]
    public void EveryKey_FitsTheBitset()
    {
        Assert.All(Enum.GetValues<Key>(), key => Assert.InRange((int)key, 0, DeviceSnapshot.Capacity - 1));
    }

    [Fact]
    public void EveryPadButton_FitsTheBitset()
    {
        Assert.All(Enum.GetValues<PadButton>(), button => Assert.InRange((int)button, 0, DeviceSnapshot.PadCapacity - 1));
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
    public void Without_ReleasesOnlyThatKey()
    {
        DeviceSnapshot snapshot = DeviceSnapshot.Of(Key.A, Key.B).Without(Key.A);

        Assert.False(snapshot.IsDown(Key.A));
        Assert.True(snapshot.IsDown(Key.B));
    }

    [Fact]
    public void EveryPadButton_IsItsOwnBit()
    {
        foreach (PadButton button in Enum.GetValues<PadButton>())
        {
            if (button == PadButton.None)
            {
                continue;
            }

            DeviceSnapshot snapshot = DeviceSnapshot.Empty.With(button);

            Assert.All(
                Enum.GetValues<PadButton>(),
                other => Assert.Equal(other == button, snapshot.IsDown(other)));
        }
    }

    [Fact]
    public void KeysAndPadButtons_DoNotShareBits()
    {
        DeviceSnapshot keysOnly = DeviceSnapshot.Of(Key.A, Key.Space);
        DeviceSnapshot padOnly = DeviceSnapshot.Empty.With(PadButton.South);

        Assert.All(Enum.GetValues<PadButton>(), button => Assert.False(keysOnly.IsDown(button)));
        Assert.All(Enum.GetValues<Key>(), key => Assert.False(padOnly.IsDown(key)));
        Assert.NotEqual(keysOnly, padOnly);
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

        Assert.Equal(-0.5f, snapshot.Axis(PadAxis.LeftStickX), Tolerance);
        Assert.Equal(0.25f, snapshot.Axis(PadAxis.RightTrigger), Tolerance);
        Assert.Equal(0f, snapshot.Axis(PadAxis.LeftStickY));
        Assert.Equal(0f, snapshot.Axis(PadAxis.RightStickX));
        Assert.Equal(0f, snapshot.Axis(PadAxis.RightStickY));
        Assert.Equal(0f, snapshot.Axis(PadAxis.LeftTrigger));
    }

    [Fact]
    public void AnAxisAwayFromRest_IsNotEmpty()
    {
        Assert.False(DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickX, 0.5f).IsEmpty);
    }

    [Theory]
    [InlineData(PadAxis.LeftStickX, -1f)]
    [InlineData(PadAxis.LeftStickY, 1f)]
    [InlineData(PadAxis.LeftTrigger, 0f)]
    [InlineData(PadAxis.RightTrigger, 1f)]
    public void WithAxis_AcceptsTheEndsOfTheRange(PadAxis axis, float value)
    {
        Assert.Equal(value, DeviceSnapshot.Empty.WithAxis(axis, value).Axis(axis), Tolerance);
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
    public void TheNoneAxis_NamesNothingToReadOrWrite()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DeviceSnapshot.Empty.Axis(PadAxis.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => DeviceSnapshot.Empty.WithAxis(PadAxis.None, 0f));
    }

    [Fact]
    public void LatchedWith_UnionsTheButtonsOfBothSnapshots()
    {
        DeviceSnapshot older = DeviceSnapshot.Of(Key.A, Key.B).With(PadButton.South);
        DeviceSnapshot newer = DeviceSnapshot.Of(Key.B, Key.C).With(PadButton.North);

        DeviceSnapshot folded = older.LatchedWith(newer);

        Assert.Equal(
            DeviceSnapshot.Of(Key.A, Key.B, Key.C).With(PadButton.South).With(PadButton.North),
            folded);
    }

    [Fact]
    public void LatchedWith_TakesEveryAxisFromTheNewerSnapshot()
    {
        DeviceSnapshot older = DeviceSnapshot.Empty
            .WithAxis(PadAxis.LeftStickX, 1f)
            .WithAxis(PadAxis.LeftTrigger, 1f);
        DeviceSnapshot newer = DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickX, -0.25f);

        DeviceSnapshot folded = older.LatchedWith(newer);

        Assert.Equal(-0.25f, folded.Axis(PadAxis.LeftStickX), Tolerance);
        Assert.Equal(0f, folded.Axis(PadAxis.LeftTrigger));
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

    [Theory]
    [InlineData(-1)]
    [InlineData(DeviceSnapshot.Capacity)]
    public void AnUnrepresentableKey_Throws(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DeviceSnapshot.Empty.With((Key)value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void AnUnrepresentableAxis_Throws(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DeviceSnapshot.Empty.Axis((PadAxis)value));
    }
}
