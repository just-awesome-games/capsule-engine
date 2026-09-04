namespace Capsule.Input;

/// <summary>
/// One bindable digital input: a <see cref="Key"/> or a <see cref="PadButton"/>, converting
/// implicitly from either. The default is <see cref="None"/>, which no snapshot holds down.
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

    /// <summary>Names <paramref name="key"/> as this button.</summary>
    public static implicit operator InputButton(Key key) => new(key, PadButton.None);

    /// <summary>Names <paramref name="padButton"/> as this button.</summary>
    public static implicit operator InputButton(PadButton padButton) => new(Key.None, padButton);

    /// <summary>Whether this names no button at all — the default, or a <c>None</c> device constant.</summary>
    public bool IsNone => _key == Key.None && _padButton == PadButton.None;

    /// <summary>Whether <paramref name="snapshot"/> holds this button down. <see cref="None"/> never is.</summary>
    public bool IsDown(in DeviceSnapshot snapshot) =>
        _key != Key.None ? snapshot.IsDown(_key) : snapshot.IsDown(_padButton);

    /// <summary>Whether both name the same device constant.</summary>
    public bool Equals(InputButton other) => _key == other._key && _padButton == other._padButton;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is InputButton other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_key, _padButton);

    /// <summary>The device constant's own name, or <c>None</c>.</summary>
    public override string ToString() => IsNone ? nameof(None) : _key != Key.None ? _key.ToString() : _padButton.ToString();

    /// <summary>Whether both name the same device constant.</summary>
    public static bool operator ==(InputButton left, InputButton right) => left.Equals(right);

    /// <summary>Whether the two name different device constants.</summary>
    public static bool operator !=(InputButton left, InputButton right) => !left.Equals(right);
}
