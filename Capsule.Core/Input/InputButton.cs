namespace Capsule.Input;

/// <summary>
/// One bindable digital input: a <see cref="Key"/> or a <see cref="PadButton"/>. Converts
/// implicitly from either, so a call site names the device constant and nothing else. The
/// default is <see cref="None"/>, which no snapshot ever holds down.
/// </summary>
public readonly struct InputButton : IEquatable<InputButton>
{
    private readonly Key _key;
    private readonly PadButton _padButton;

    private InputButton(Key key, PadButton padButton)
    {
        _key = key;
        _padButton = padButton;
    }

    /// <summary>No button; equal to <c>default</c>. Binding rejects it.</summary>
    public static InputButton None => default;

    public static implicit operator InputButton(Key key) => new(key, PadButton.None);

    public static implicit operator InputButton(PadButton padButton) => new(Key.None, padButton);

    /// <summary>Whether this names no button at all — the default, or a <c>None</c> device constant.</summary>
    public bool IsNone => _key == Key.None && _padButton == PadButton.None;

    /// <summary>Whether <paramref name="snapshot"/> holds this button down. <see cref="None"/> never is.</summary>
    public bool IsDown(in DeviceSnapshot snapshot) =>
        _key != Key.None ? snapshot.IsDown(_key) : snapshot.IsDown(_padButton);

    public bool Equals(InputButton other) => _key == other._key && _padButton == other._padButton;

    public override bool Equals(object? obj) => obj is InputButton other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_key, _padButton);

    public override string ToString() => IsNone ? nameof(None) : _key != Key.None ? _key.ToString() : _padButton.ToString();

    public static bool operator ==(InputButton left, InputButton right) => left.Equals(right);

    public static bool operator !=(InputButton left, InputButton right) => !left.Equals(right);
}
