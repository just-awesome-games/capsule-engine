namespace Capsule.Input;

/// <summary>
/// Preserves sampled input until a fixed step consumes it, including presses between steps.
/// Per-tick harnesses may drive <see cref="InputState.Advance"/> directly.
/// </summary>
internal sealed class SnapshotLatch
{
    private DeviceSnapshot _live;
    private DeviceSnapshot _latched;
    private bool _observedSinceStep;

    /// <summary>Records one sampled frame. Buttons stay latched until a step consumes them.</summary>
    public void Observe(in DeviceSnapshot snapshot)
    {
        _latched = _observedSinceStep ? _latched.LatchedWith(snapshot) : snapshot;
        _live = snapshot;
        _observedSinceStep = true;
    }

    /// <summary>Consumes latched buttons and the latest axis values for one fixed step.</summary>
    public DeviceSnapshot ConsumeStepSnapshot()
    {
        DeviceSnapshot consumed = _observedSinceStep ? _latched : _live;
        _observedSinceStep = false;

        return consumed;
    }
}
