using System.Text.Json.Serialization;

namespace Capsule.Input;

/// <summary>
/// One bindable digital input: a <see cref="Key"/>, a <see cref="PadButton"/>, a
/// <see cref="MouseButton"/> or a <see cref="StickDirection"/>. It converts implicitly from any of
/// them. The default is <see cref="None"/>, which no snapshot holds down.
/// </summary>
[JsonConverter(typeof(InputButtonJsonConverter))]
public readonly struct InputButton : IEquatable<InputButton>, IParsable<InputButton>
{
    /// <summary>
    /// How far a stick must be pushed, in [0, 1] along the direction's axis, for a
    /// <see cref="StickDirection"/> to read as held. It applies to the axis position the snapshot
    /// carries, which is already past the run's stick deadzone.
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

    /// <summary>No button, equal to <c>default</c>. Binding rejects it.</summary>
    public static InputButton None => default;

    /// <summary>Names <paramref name="key"/> as this button.</summary>
    public static implicit operator InputButton(Key key) => new(key, PadButton.None, MouseButton.None, StickDirection.None);

    /// <summary>Names <paramref name="padButton"/> as this button.</summary>
    public static implicit operator InputButton(PadButton padButton) => new(Key.None, padButton, MouseButton.None, StickDirection.None);

    /// <summary>Names <paramref name="mouseButton"/> as this button.</summary>
    public static implicit operator InputButton(MouseButton mouseButton) => new(Key.None, PadButton.None, mouseButton, StickDirection.None);

    /// <summary>Names <paramref name="stickDirection"/> as this button.</summary>
    public static implicit operator InputButton(StickDirection stickDirection) => new(Key.None, PadButton.None, MouseButton.None, stickDirection);

    /// <summary>Whether this names no button, which covers the default and any <c>None</c> device constant.</summary>
    public bool IsNone =>
        _key == Key.None && _padButton == PadButton.None && _mouseButton == MouseButton.None &&
        _stickDirection == StickDirection.None;

    // Whether a snapshot can hold this button. Binding checks it, and a read does not.
    internal bool IsRepresentable =>
        (uint)_key < DeviceSnapshot.Capacity &&
        (uint)_padButton < DeviceSnapshot.PadCapacity &&
        (uint)_mouseButton < DeviceSnapshot.MouseCapacity;

    /// <summary>
    /// Whether <paramref name="snapshot"/> holds this button down. A stick direction is down while its
    /// axis is at or past <see cref="StickPressPoint"/> in that direction. <see cref="None"/> is never
    /// down.
    /// </summary>
    public bool IsDown(in DeviceSnapshot snapshot) =>
        _key != Key.None ? snapshot.IsDown(_key)
        : _padButton != PadButton.None ? snapshot.IsDown(_padButton)
        : _stickDirection != StickDirection.None ? IsPushed(snapshot)
        : snapshot.IsDown(_mouseButton);

    /// <summary>The device this button is on: keyboard and mouse together, or the gamepad.</summary>
    /// <exception cref="InvalidOperationException"><see cref="None"/> names no device.</exception>
    public InputDevice Device =>
        IsNone ? throw new InvalidOperationException($"{nameof(InputButton)}.{nameof(None)} names no device. Test {nameof(IsNone)} first.")
        : _key != Key.None || _mouseButton != MouseButton.None ? InputDevice.KeyboardMouse
        : InputDevice.Gamepad;

    /// <summary>The device constant's bare name, what a caption shows, or <c>None</c> for none.</summary>
    public string Name => IsNone ? nameof(None) : Qualified.Name;

    /// <summary>Whether both name the same device constant.</summary>
    public bool Equals(InputButton other) =>
        _key == other._key && _padButton == other._padButton && _mouseButton == other._mouseButton &&
        _stickDirection == other._stickDirection;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is InputButton other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_key, _padButton, _mouseButton, _stickDirection);

    /// <summary>The qualified, parseable form, such as <c>Key.Space</c>, or <c>None</c>.</summary>
    public override string ToString() => IsNone ? nameof(None) : $"{Qualified.Device}.{Qualified.Name}";

    // The device constant's own type name and bare name, shared by Name and ToString. Not called
    // when IsNone; callers guard that first.
    private (string Device, string Name) Qualified =>
        _key != Key.None ? (nameof(Key), _key.ToString())
        : _padButton != PadButton.None ? (nameof(PadButton), _padButton.ToString())
        : _stickDirection != StickDirection.None ? (nameof(StickDirection), _stickDirection.ToString())
        : (nameof(MouseButton), _mouseButton.ToString());

    // The accepted-shape text, shared by Parse and InputButtonJsonConverter so it exists once.
    internal const string ExpectedShape =
        "Expected 'None' or '<Device>.<Name>', such as 'Key.Space', 'PadButton.South', " +
        "'MouseButton.Left' or 'StickDirection.LeftStickUp'.";

    /// <summary>Parses the form <see cref="ToString"/> writes, such as <c>Key.Space</c> or <c>None</c>.</summary>
    /// <exception cref="FormatException"><paramref name="s"/> is not that shape.</exception>
    public static InputButton Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);

        if (TryParse(s, out InputButton result))
        {
            return result;
        }

        throw new FormatException($"'{s}' is not a valid {nameof(InputButton)}. {ExpectedShape}");
    }

    static InputButton IParsable<InputButton>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>Tries to parse the form <see cref="ToString"/> writes. Ordinal and exact, no numeric enum strings.</summary>
    public static bool TryParse(string? s, out InputButton result)
    {
        result = None;

        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        if (s == nameof(None))
        {
            return true;
        }

        int dot = s.IndexOf('.');
        if (dot < 0)
        {
            return false;
        }

        string device = s[..dot];
        string name = s[(dot + 1)..];

        result = device switch
        {
            nameof(Key) when TryParseDefined(name, out Key key) => key,
            nameof(PadButton) when TryParseDefined(name, out PadButton padButton) => padButton,
            nameof(MouseButton) when TryParseDefined(name, out MouseButton mouseButton) => mouseButton,
            nameof(StickDirection) when TryParseDefined(name, out StickDirection stickDirection) => stickDirection,
            _ => None,
        };

        return !result.IsNone;
    }

    static bool IParsable<InputButton>.TryParse(string? s, IFormatProvider? provider, out InputButton result) =>
        TryParse(s, out result);

    // Ordinal and exact: a numeric string Enum.TryParse accepts but IsDefined does not is rejected.
    private static bool TryParseDefined<T>(string name, out T value)
        where T : struct, Enum =>
        Enum.TryParse(name, ignoreCase: false, out value) && Enum.IsDefined(value);

    /// <summary>Whether both name the same device constant.</summary>
    public static bool operator ==(InputButton left, InputButton right) => left.Equals(right);

    /// <summary>Whether the two name different device constants.</summary>
    public static bool operator !=(InputButton left, InputButton right) => !left.Equals(right);

    private bool IsPushed(in DeviceSnapshot snapshot)
    {
        float position = snapshot.Axis(Axis(_stickDirection));

        return Positive(_stickDirection) ? position >= StickPressPoint : position <= -StickPressPoint;
    }

    internal DeviceSnapshot RemoveFrom(in DeviceSnapshot snapshot) =>
        _key != Key.None ? snapshot.Without(_key)
        : _padButton != PadButton.None ? snapshot.Without(_padButton)
        : _stickDirection != StickDirection.None ? WithoutStickDirection(snapshot)
        : snapshot.Without(_mouseButton);

    private DeviceSnapshot WithoutStickDirection(in DeviceSnapshot snapshot)
    {
        PadAxis axis = Axis(_stickDirection);
        float position = snapshot.Axis(axis);
        float inside = Positive(_stickDirection)
            ? MathF.BitDecrement(StickPressPoint)
            : MathF.BitIncrement(-StickPressPoint);
        float released = Positive(_stickDirection) ? MathF.Min(position, inside) : MathF.Max(position, inside);

        return snapshot.WithAxis(axis, released);
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
