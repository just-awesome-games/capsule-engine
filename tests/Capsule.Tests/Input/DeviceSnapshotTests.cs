using Capsule.Input;

namespace Capsule.Tests.Input;

public sealed class DeviceSnapshotTests
{
    public static TheoryData<InputButton, Func<DeviceSnapshot, DeviceSnapshot>> Releases => new()
    {
        { Key.A, static held => held.Without(Key.A) },
        { PadButton.South, static held => held.Without(PadButton.South) },
        { MouseButton.Left, static held => held.Without(MouseButton.Left) },
    };

    [Fact]
    public void EveryDeviceConstant_FitsItsBitset()
    {
        Assert.All(Enum.GetValues<Key>(), key => Assert.InRange((int)key, 0, DeviceSnapshot.Capacity - 1));
        Assert.All(Enum.GetValues<PadButton>(), button => Assert.InRange((int)button, 0, DeviceSnapshot.PadCapacity - 1));
        Assert.All(Enum.GetValues<MouseButton>(), button => Assert.InRange((int)button, 0, DeviceSnapshot.MouseCapacity - 1));
    }

    // Each device's overload and the InputButton one release that button alone.
    [Theory]
    [MemberData(nameof(Releases))]
    public void Without_ReleasesOnlyThatButton_ByItsDeviceOrAsAnInputButton(InputButton button, Func<DeviceSnapshot, DeviceSnapshot> byDevice)
    {
        InputButton[] down = [Key.A, Key.F12, PadButton.South, MouseButton.Left];
        DeviceSnapshot held = DeviceSnapshot.Of(Key.A, Key.F12).With(PadButton.South).With(MouseButton.Left);

        DeviceSnapshot released = held.Without(button);

        Assert.Equal(byDevice(held), released);
        Assert.All(down, other => Assert.Equal(other != button, other.IsDown(released)));
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

    // The family is recorded input: a replay compares it, and a latched frame takes the newest pad's.
    [Fact]
    public void APadFamilyAlone_DistinguishesTwoSnapshots()
    {
        DeviceSnapshot xbox = DeviceSnapshot.Empty.WithPadFamily(PadFamily.Xbox);

        Assert.Equal(PadFamily.Generic, DeviceSnapshot.Empty.PadFamily);
        Assert.NotEqual(DeviceSnapshot.Empty, xbox);
        Assert.Equal(PadFamily.PlayStation, xbox.LatchedWith(DeviceSnapshot.Empty.WithPadFamily(PadFamily.PlayStation)).PadFamily);
        Assert.Throws<ArgumentOutOfRangeException>(() => DeviceSnapshot.Empty.WithPadFamily((PadFamily)4));
    }

    // A snapshot starts on a full pad. The builder replaces the set rather than adding to it, and the
    // set is recorded input that a latched frame takes from the newest sample.
    [Fact]
    public void ThePadsButtons_StartFullAndAreSetByWithPadLacking()
    {
        DeviceSnapshot noTriggers = DeviceSnapshot.Empty.WithPadLacking(PadButton.LeftTrigger, PadButton.RightTrigger);

        Assert.All(Enum.GetValues<PadButton>(), button => Assert.Equal(button != PadButton.None, DeviceSnapshot.Empty.PadHas(button)));
        Assert.All(Enum.GetValues<PadButton>(), button => Assert.Equal(
            button is not (PadButton.None or PadButton.LeftTrigger or PadButton.RightTrigger),
            noTriggers.PadHas(button)));
        Assert.False(DeviceSnapshot.Empty.PadHas((PadButton)DeviceSnapshot.PadCapacity));

        Assert.NotEqual(DeviceSnapshot.Empty, noTriggers);
        Assert.Equal(DeviceSnapshot.Empty, noTriggers.WithPadLacking());
        Assert.True(noTriggers.WithPadLacking(PadButton.East).PadHas(PadButton.RightTrigger));
        Assert.True(noTriggers.LatchedWith(DeviceSnapshot.Empty).PadHas(PadButton.RightTrigger));
        Assert.Throws<ArgumentOutOfRangeException>(() => DeviceSnapshot.Empty.WithPadLacking((PadButton)DeviceSnapshot.PadCapacity));
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
