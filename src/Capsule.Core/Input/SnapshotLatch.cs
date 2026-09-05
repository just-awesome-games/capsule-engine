namespace Capsule.Input;

// Preserves sampled input until a fixed step consumes it, including presses between steps.
internal sealed class SnapshotLatch
{
    private DeviceSnapshot _live;
    private DeviceSnapshot _latched;
    private bool _observedSinceStep;

    // Records one sampled frame. Buttons stay latched until a step consumes them.
    public void Observe(in DeviceSnapshot snapshot)
    {
        _latched = _observedSinceStep ? _latched.LatchedWith(snapshot) : snapshot;
        _live = snapshot;
        _observedSinceStep = true;
    }

    // Consumes latched buttons and the latest axis values for one fixed step.
    public DeviceSnapshot ConsumeStepSnapshot()
    {
        DeviceSnapshot consumed = _observedSinceStep ? _latched : _live;
        _observedSinceStep = false;

        return consumed;
    }
}
