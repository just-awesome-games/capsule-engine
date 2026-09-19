using System.Numerics;

namespace Capsule.Input;

// Preserves sampled input until a fixed step consumes it, including presses between steps.
internal sealed class SnapshotLatch
{
    private DeviceSnapshot _live;
    private DeviceSnapshot _latched;
    private bool _observedSinceStep;

    // Records one sampled frame. Buttons stay latched until a step consumes them, and wheel notches
    // accumulate until then.
    internal void Observe(in DeviceSnapshot snapshot)
    {
        _latched = _observedSinceStep ? _latched.LatchedWith(snapshot) : snapshot;
        _live = snapshot;
        _observedSinceStep = true;
    }

    // Drops samples waiting for a step but keeps the last live sample. The next observation after a host
    // hold replaces it, and a release during the hold cannot become a stale edge.
    internal void DiscardPending()
    {
        _observedSinceStep = false;
    }

    // Consumes latched buttons and the latest axis values for one fixed step. A second step drained in
    // the same frame sees the same held state and positions, and no scroll. Notches are a delta one step
    // spends, while a held button is a state every step reads.
    internal DeviceSnapshot Consume()
    {
        DeviceSnapshot consumed = _observedSinceStep ? _latched : _live;
        _observedSinceStep = false;
        _live = _live.WithScroll(Vector2.Zero);

        return consumed;
    }
}
