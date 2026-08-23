namespace Capsule.Input;

/// <summary>
/// Folds every sampled <see cref="DeviceSnapshot"/> into the one snapshot the next fixed
/// step will consume. A frame that drains no step would otherwise throw its sample away,
/// so above the step rate a key pressed and released between two steps would produce no
/// edge at all; latching makes it one held tick and then a release.
/// The runtime observes once per frame and consumes once per step. A harness that already
/// owns per-tick snapshots may hand them straight to <see cref="InputState.Advance"/> and
/// bypass this, or drive it exactly as the runtime does — either way the determinism seam
/// stays at <see cref="DeviceSnapshot"/>.
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
