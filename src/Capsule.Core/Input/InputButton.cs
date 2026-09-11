namespace Capsule.Input;

/// <summary>
/// One bindable digital input: a <see cref="Key"/>, a <see cref="PadButton"/>, a
/// <see cref="MouseButton"/> or a <see cref="StickDirection"/>, converting implicitly from any of
/// them. The default is <see cref="None"/>, which no snapshot holds down.
/// </summary>
public readonly struct InputButton : IEquatable<InputButton>
{
    /// <summary>
    /// How far a stick must be pushed, in [0, 1] along the direction's axis, for a
    /// <see cref="StickDirection"/> to read as held. Unity's default press point; applied to the
    /// axis position the snapshot carries, which is already past the run's stick deadzone.
    /// </summary>
    public const float StickPressPoint = 0.5f;

    private readonly Key _key;
    private readonly PadButton _padButton;
    private readonly MouseButton _mouseButton;
    private readonly StickDirection _stickDirection;

    private InputButton(Key key, PadButton padButton, MouseButton mouseButton, StickDirection stickDirection)
    {
        _key = key;
        _padButton = padButton;
        _mouseButton = mouseButton;
        _stickDirection = stickDirection;
    }

    /// <summary>No button; equal to <c>default</c>. Binding rejects it.</summary>
    public static InputButton None => default;

    /// <summary>Names <paramref name="key"/> as this button.</summary>
    public static implicit operator InputButton(Key key) => new(key, PadButton.None, MouseButton.None, StickDirection.None);

    /// <summary>Names <paramref name="padButton"/> as this button.</summary>
    public static implicit operator InputButton(PadButton padButton) => new(Key.None, padButton, MouseButton.None, StickDirection.None);

    /// <summary>Names <paramref name="mouseButton"/> as this button.</summary>
    public static implicit operator InputButton(MouseButton mouseButton) => new(Key.None, PadButton.None, mouseButton, StickDirection.None);

    /// <summary>Names <paramref name="stickDirection"/> as this button.</summary>
    public static implicit operator InputButton(StickDirection stickDirection) => new(Key.None, PadButton.None, MouseButton.None, stickDirection);

    /// <summary>Whether this names no button at all — the default, or a <c>None</c> device constant.</summary>
    public bool IsNone =>
        _key == Key.None && _padButton == PadButton.None && _mouseButton == MouseButton.None &&
        _stickDirection == StickDirection.None;

    /// <summary>
    /// Whether <paramref name="snapshot"/> holds this button down; a stick direction is down while
    /// its axis is at or past <see cref="StickPressPoint"/> that way. <see cref="None"/> never is.
    /// </summary>
    public bool IsDown(in DeviceSnapshot snapshot) =>
        _key != Key.None ? snapshot.IsDown(_key)
        : _padButton != PadButton.None ? snapshot.IsDown(_padButton)
        : _stickDirection != StickDirection.None ? IsPushed(snapshot)
        : snapshot.IsDown(_mouseButton);

    /// <summary>Whether both name the same device constant.</summary>
    public bool Equals(InputButton other) =>
        _key == other._key && _padButton == other._padButton && _mouseButton == other._mouseButton &&
        _stickDirection == other._stickDirection;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is InputButton other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_key, _padButton, _mouseButton, _stickDirection);

    /// <summary>The device constant's own name, or <c>None</c>.</summary>
    public override string ToString() =>
        IsNone ? nameof(None)
        : _key != Key.None ? _key.ToString()
        : _padButton != PadButton.None ? _padButton.ToString()
        : _stickDirection != StickDirection.None ? _stickDirection.ToString()
        : _mouseButton.ToString();

    /// <summary>Whether both name the same device constant.</summary>
    public static bool operator ==(InputButton left, InputButton right) => left.Equals(right);

    /// <summary>Whether the two name different device constants.</summary>
    public static bool operator !=(InputButton left, InputButton right) => !left.Equals(right);

    private bool IsPushed(in DeviceSnapshot snapshot)
    {
        float position = snapshot.Axis(Axis(_stickDirection));

        return Positive(_stickDirection) ? position >= StickPressPoint : position <= -StickPressPoint;
    }

    private static PadAxis Axis(StickDirection direction) => direction switch
    {
        StickDirection.LeftStickUp or StickDirection.LeftStickDown => PadAxis.LeftStickY,
        StickDirection.LeftStickLeft or StickDirection.LeftStickRight => PadAxis.LeftStickX,
        StickDirection.RightStickUp or StickDirection.RightStickDown => PadAxis.RightStickY,
        _ => PadAxis.RightStickX,
    };

    private static bool Positive(StickDirection direction) =>
        direction is StickDirection.LeftStickUp or StickDirection.LeftStickRight
            or StickDirection.RightStickUp or StickDirection.RightStickRight;
}
