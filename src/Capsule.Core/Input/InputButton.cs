namespace Capsule.Input;

/// <summary>
/// One bindable digital input: a <see cref="Key"/>, a <see cref="PadButton"/> or a
/// <see cref="MouseButton"/>, converting implicitly from any of them. The default is
/// <see cref="None"/>, which no snapshot holds down.
/// </summary>
public readonly struct InputButton : IEquatable<InputButton>
{
    private readonly Key _key;
    private readonly PadButton _padButton;
    private readonly MouseButton _mouseButton;

    private InputButton(Key key, PadButton padButton, MouseButton mouseButton)
    {
        _key = key;
        _padButton = padButton;
        _mouseButton = mouseButton;
    }

    /// <summary>No button; equal to <c>default</c>. Binding rejects it.</summary>
    public static InputButton None => default;

    /// <summary>Names <paramref name="key"/> as this button.</summary>
    public static implicit operator InputButton(Key key) => new(key, PadButton.None, MouseButton.None);

    /// <summary>Names <paramref name="padButton"/> as this button.</summary>
    public static implicit operator InputButton(PadButton padButton) => new(Key.None, padButton, MouseButton.None);

    /// <summary>Names <paramref name="mouseButton"/> as this button.</summary>
    public static implicit operator InputButton(MouseButton mouseButton) => new(Key.None, PadButton.None, mouseButton);

    /// <summary>Whether this names no button at all — the default, or a <c>None</c> device constant.</summary>
    public bool IsNone => _key == Key.None && _padButton == PadButton.None && _mouseButton == MouseButton.None;

    /// <summary>Whether <paramref name="snapshot"/> holds this button down. <see cref="None"/> never is.</summary>
    public bool IsDown(in DeviceSnapshot snapshot) =>
        _key != Key.None ? snapshot.IsDown(_key)
        : _padButton != PadButton.None ? snapshot.IsDown(_padButton)
        : snapshot.IsDown(_mouseButton);

    /// <summary>Whether both name the same device constant.</summary>
    public bool Equals(InputButton other) =>
        _key == other._key && _padButton == other._padButton && _mouseButton == other._mouseButton;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is InputButton other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_key, _padButton, _mouseButton);

    /// <summary>The device constant's own name, or <c>None</c>.</summary>
    public override string ToString() =>
        IsNone ? nameof(None)
        : _key != Key.None ? _key.ToString()
        : _padButton != PadButton.None ? _padButton.ToString()
        : _mouseButton.ToString();

    /// <summary>Whether both name the same device constant.</summary>
    public static bool operator ==(InputButton left, InputButton right) => left.Equals(right);

    /// <summary>Whether the two name different device constants.</summary>
    public static bool operator !=(InputButton left, InputButton right) => !left.Equals(right);
}
