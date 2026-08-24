namespace Capsule.Input;

/// <summary>
/// Action-level input for the current fixed step. Every edge is a diff of two consecutive
/// <see cref="DeviceSnapshot"/>s, never an OS callback, so a replayed snapshot sequence
/// reproduces a run exactly. The runtime owns <see cref="Advance"/>; a simulation only reads.
/// </summary>
public sealed class InputState(ActionBindings bindings)
{
    private readonly ActionBindings _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));

    private DeviceSnapshot _previous;
    private DeviceSnapshot _current;

    /// <summary>
    /// Makes <paramref name="snapshot"/> the current instant and the former current
    /// the previous one. Advancing twice against the same snapshot reports no edges
    /// the second time, so a frame that drains several steps fires each edge once.
    /// </summary>
    public void Advance(in DeviceSnapshot snapshot)
    {
        _previous = _current;
        _current = snapshot;
    }

    public bool IsHeld(InputAction action) => _bindings.IsAnyDown(action, _current);

    /// <summary>What <paramref name="action"/> reads this step, in [-1, 1]; 0 when unbound.</summary>
    public float Axis(AxisAction action) => _bindings.AxisValue(action, _current);

    public bool WasPressed(InputAction action) =>
        _bindings.IsAnyDown(action, _current) && !_bindings.IsAnyDown(action, _previous);

    public bool WasReleased(InputAction action) =>
        !_bindings.IsAnyDown(action, _current) && _bindings.IsAnyDown(action, _previous);
}
