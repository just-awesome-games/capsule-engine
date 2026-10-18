using System.Text.Json.Serialization;

namespace Capsule.Input;

/// <summary>
/// One bindable digital input: a <see cref="Key"/>, a <see cref="PadButton"/>, a
/// <see cref="MouseButton"/> or a <see cref="StickDirection"/>.
/// </summary>
/// <remarks>
/// It converts implicitly from any of them. The default is <see cref="None"/>, which no snapshot
/// holds down.
/// </remarks>
[JsonConverter(typeof(InputButtonJsonConverter))]
public readonly struct InputButton : IEquatable<InputButton>, IParsable<InputButton>
{
    /// <summary>
    /// How far a stick must be pushed, in [0, 1] along the direction's axis, for a
    /// <see cref="StickDirection"/> to read as held. The threshold applies to the snapshot's axis
    /// position, after the stick deadzone.
    /// </summary>
    public const float StickPressPoint = 0.5f;

    // The accepted-shape text Parse and InputButtonJsonConverter share.
    internal const string ExpectedShape =
        "Expected 'None' or '<Device>.<Name>', such as 'Key.Space', 'PadButton.South', " +
        "'MouseButton.Left' or 'StickDirection.LeftStickUp'.";

    // Which device enum _value belongs to. Every None constant stores as Source.None with value 0,
    // which keeps it equal to the default.
    private readonly Source _source;
    private readonly int _value;

    private InputButton(Source source, int value)
    {
        _source = value == 0 ? Source.None : source;
        _value = value;
    }

    /// <summary>No button, equal to <c>default</c>. Binding rejects it.</summary>
    public static InputButton None => default;

    /// <summary>Names <paramref name="key"/> as this button.</summary>
    public static implicit operator InputButton(Key key) => new(Source.Key, (int)key);

    /// <summary>Names <paramref name="padButton"/> as this button.</summary>
    public static implicit operator InputButton(PadButton padButton) => new(Source.PadButton, (int)padButton);

    /// <summary>Names <paramref name="mouseButton"/> as this button.</summary>
    public static implicit operator InputButton(MouseButton mouseButton) => new(Source.MouseButton, (int)mouseButton);

    /// <summary>Names <paramref name="stickDirection"/> as this button.</summary>
    public static implicit operator InputButton(StickDirection stickDirection) => new(Source.StickDirection, (int)stickDirection);

    /// <summary>Whether this names no button, which covers the default and any <c>None</c> device constant.</summary>
    public bool IsNone => _source == Source.None;

    // Whether a snapshot can hold this button. Binding checks it, and a read does not.
    internal bool IsRepresentable => _source switch
    {
        Source.Key => (uint)_value < DeviceSnapshot.Capacity,
        Source.PadButton => (uint)_value < DeviceSnapshot.PadCapacity,
        Source.MouseButton => (uint)_value < DeviceSnapshot.MouseCapacity,
        _ => true,
    };

    internal bool IsMouseButton => _source == Source.MouseButton;

    /// <summary>
    /// Whether <paramref name="snapshot"/> holds this button down. A stick direction is down while
    /// its axis is at or past <see cref="StickPressPoint"/> in that direction.
    /// </summary>
    /// <remarks><see cref="None"/> is never down.</remarks>
    public bool IsDown(in DeviceSnapshot snapshot) => _source switch
    {
        Source.Key => snapshot.IsDown((Key)_value),
        Source.PadButton => snapshot.IsDown((PadButton)_value),
        Source.MouseButton => snapshot.IsDown((MouseButton)_value),
        Source.StickDirection => IsPushed(snapshot),
        _ => false,
    };

    /// <summary>The device this button is on: keyboard and mouse together, or the gamepad.</summary>
    /// <exception cref="InvalidOperationException"><see cref="None"/> names no device.</exception>
    public InputDevice Device =>
        IsNone ? throw new InvalidOperationException($"{nameof(InputButton)}.{nameof(None)} names no device. Test {nameof(IsNone)} first.")
        : _source is Source.Key or Source.MouseButton ? InputDevice.KeyboardMouse
        : InputDevice.Gamepad;

    /// <summary>The device constant's bare name for a caption, such as <c>Space</c>, or <c>None</c>.</summary>
    public string Name => IsNone ? nameof(None) : Qualified.Name;

    /// <summary>Whether both name the same device constant.</summary>
    public bool Equals(InputButton other) => _source == other._source && _value == other._value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is InputButton other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_source, _value);

    /// <summary>The qualified, parseable form, such as <c>Key.Space</c>, or <c>None</c>.</summary>
    public override string ToString() => IsNone ? nameof(None) : $"{Qualified.Device}.{Qualified.Name}";

    // The device constant's type name and bare name. Callers rule out None first.
    private (string Device, string Name) Qualified => _source switch
    {
        Source.Key => (nameof(Key), ((Key)_value).ToString()),
        Source.PadButton => (nameof(PadButton), ((PadButton)_value).ToString()),
        Source.MouseButton => (nameof(MouseButton), ((MouseButton)_value).ToString()),
        _ => (nameof(StickDirection), Direction.ToString()),
    };

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

    /// <summary>Tries to parse the form <see cref="ToString"/> writes.</summary>
    /// <remarks>
    /// Matching is ordinal and case-sensitive. Numeric enum strings, null and empty all fail.
    /// </remarks>
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

    private StickDirection Direction => (StickDirection)_value;

    private bool IsPushed(in DeviceSnapshot snapshot)
    {
        float position = snapshot.Axis(Axis(Direction));

        return Positive(Direction) ? position >= StickPressPoint : position <= -StickPressPoint;
    }

    internal DeviceSnapshot AddTo(in DeviceSnapshot snapshot) => _source switch
    {
        Source.Key => snapshot.With((Key)_value),
        Source.PadButton => snapshot.With((PadButton)_value),
        Source.MouseButton => snapshot.With((MouseButton)_value),
        Source.StickDirection => snapshot.WithAxis(Axis(Direction), Positive(Direction) ? 1f : -1f),
        _ => snapshot,
    };

    internal DeviceSnapshot RemoveFrom(in DeviceSnapshot snapshot) => _source switch
    {
        Source.Key => snapshot.Without((Key)_value),
        Source.PadButton => snapshot.Without((PadButton)_value),
        Source.MouseButton => snapshot.Without((MouseButton)_value),
        Source.StickDirection => WithoutStickDirection(snapshot),
        _ => snapshot,
    };

    private DeviceSnapshot WithoutStickDirection(in DeviceSnapshot snapshot)
    {
        PadAxis axis = Axis(Direction);
        float position = snapshot.Axis(axis);
        float inside = Positive(Direction)
            ? MathF.BitDecrement(StickPressPoint)
            : MathF.BitIncrement(-StickPressPoint);
        float released = Positive(Direction) ? MathF.Min(position, inside) : MathF.Max(position, inside);

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

    private enum Source : byte
    {
        None,
        Key,
        PadButton,
        MouseButton,
        StickDirection,
    }
}
