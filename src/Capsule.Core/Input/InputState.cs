namespace Capsule.Input;

/// <summary>Action-level input derived from consecutive deterministic device snapshots.</summary>
public sealed class InputState(ActionBindings bindings)
{
    private readonly ActionBindings _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));

    private DeviceSnapshot _previous;
    private DeviceSnapshot _current;

    /// <summary>Advances to a snapshot; repeated snapshots produce no repeated edges.</summary>
    public void Advance(in DeviceSnapshot snapshot)
    {
        _previous = _current;
        _current = snapshot;
    }

    /// <summary>Whether anything bound to <paramref name="action"/> is down this step.</summary>
    public bool IsHeld(InputAction action) => _bindings.IsAnyDown(action, _current);

    /// <summary>What <paramref name="action"/> reads this step, in [-1, 1]; 0 when unbound.</summary>
    public float Axis(AxisAction action) => _bindings.AxisValue(action, _current);

    /// <summary>Whether <paramref name="action"/> went down on the edge into this step.</summary>
    public bool WasPressed(InputAction action) =>
        _bindings.IsAnyDown(action, _current) && !_bindings.IsAnyDown(action, _previous);

    /// <summary>Whether <paramref name="action"/> came up on the edge into this step.</summary>
    public bool WasReleased(InputAction action) =>
        !_bindings.IsAnyDown(action, _current) && _bindings.IsAnyDown(action, _previous);
}
