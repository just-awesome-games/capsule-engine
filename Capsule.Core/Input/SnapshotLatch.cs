namespace Capsule.Input;

/// <summary>
/// Folds every sampled <see cref="DeviceSnapshot"/> into the one snapshot the next fixed
/// step consumes: observed once per frame, consumed once per step. Without it a frame that
/// drains no step discards its sample, losing a key pressed and released between two steps
/// entirely. A harness that already owns per-tick snapshots may bypass this and drive
/// <see cref="InputState.Advance"/> directly — the determinism seam is
/// <see cref="DeviceSnapshot"/>, not the latch.
/// </summary>
public sealed class SnapshotLatch
{
    private DeviceSnapshot _live;
    private DeviceSnapshot _latched;
    private bool _observedSinceStep;

    /// <summary>Records one sampled frame. Keys stay latched until a step consumes them.</summary>
    public void Observe(in DeviceSnapshot snapshot)
    {
        _latched = _observedSinceStep ? _latched.Union(snapshot) : snapshot;
        _live = snapshot;
        _observedSinceStep = true;
    }

    /// <summary>
    /// The snapshot for one fixed step: every key seen down in any frame observed since
    /// the previous step. When no frame has been observed since — several steps draining
    /// in one frame — the last observed frame stands, so an edge still fires only once.
    /// </summary>
    public DeviceSnapshot ConsumeStepSnapshot()
    {
        DeviceSnapshot consumed = _observedSinceStep ? _latched : _live;
        _observedSinceStep = false;

        return consumed;
    }
}
