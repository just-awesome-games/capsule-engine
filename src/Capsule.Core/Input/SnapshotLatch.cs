using System.Numerics;

namespace Capsule.Input;

// Preserves sampled input until a fixed step consumes it, including presses between steps.
internal sealed class SnapshotLatch
{
    private DeviceSnapshot _live;
    private DeviceSnapshot _latched;
    private bool _observedSinceStep;

    // Records one sampled frame. Buttons stay latched until a step consumes them, and the wheel's
    // notches accumulate until one does.
    public void Observe(in DeviceSnapshot snapshot)
    {
        _latched = _observedSinceStep ? _latched.LatchedWith(snapshot) : snapshot;
        _live = snapshot;
        _observedSinceStep = true;
    }

    // Drops samples waiting for a step while retaining the last live sample. The next observation
    // after a host hold replaces it, so a release during the hold cannot become a stale edge.
    internal void DiscardPending()
    {
        _observedSinceStep = false;
    }

    // Consumes latched buttons and the latest axis values for one fixed step. A second step drained
    // in the same frame sees the same held state and positions, and no scroll: notches are a delta
    // one step spends, where a held button is a state every step reads.
    public DeviceSnapshot ConsumeStepSnapshot()
    {
        DeviceSnapshot consumed = _observedSinceStep ? _latched : _live;
        _observedSinceStep = false;
        _live = _live.WithScroll(Vector2.Zero);

        return consumed;
    }
}
