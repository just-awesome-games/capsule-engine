namespace Capsule.Runtime.Input;

/// <summary>
/// Capsule's own deadzone filtering, applied to raw backend axis readings. Pure and
/// device-free so it is asserted directly rather than through a gamepad. One instance
/// carries the radii a host was configured with.
/// </summary>
internal readonly struct PadFilter(float stickDeadzone, float triggerDeadzone)
{
    /// <summary>Stick radius below which the stick reads as centred, absent a configured one.</summary>
    internal const float DefaultStickDeadzone = 0.25f;

    /// <summary>Trigger pull below which the trigger reads as released, absent a configured one.</summary>
    internal const float DefaultTriggerDeadzone = 0.12f;

    private readonly float _stickDeadzone = stickDeadzone;
    private readonly float _triggerDeadzone = triggerDeadzone;

    /// <summary>
    /// A raw stick reading with the deadzone removed radially: inside the radius the stick
    /// is centred, outside it the magnitude is remapped onto [0, 1] with the direction
    /// preserved, so the result never leaves the unit disk even when the hardware reports a
    /// diagonal past it.
    /// </summary>
    internal (float X, float Y) Stick(float x, float y)
    {
        float magnitude = MathF.Sqrt((x * x) + (y * y));
        if (magnitude <= _stickDeadzone)
        {
            return (0f, 0f);
        }

        float scale = Remap(magnitude, _stickDeadzone) / magnitude;

        return (x * scale, y * scale);
    }

    /// <summary>A raw trigger reading with the deadzone removed, remapped onto [0, 1].</summary>
    internal float Trigger(float value) =>
        value <= _triggerDeadzone ? 0f : Remap(value, _triggerDeadzone);

    private static float Remap(float value, float deadzone) =>
        MathF.Min((value - deadzone) / (1f - deadzone), 1f);
}
