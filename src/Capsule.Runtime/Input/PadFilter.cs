namespace Capsule.Runtime.Input;

// Capsule's own deadzone filtering, applied to raw backend axis readings. One instance carries the
// radii a host was configured with.
internal readonly struct PadFilter(float stickDeadzone, float triggerDeadzone)
{
    // A raw stick reading with the deadzone removed radially. Inside the radius it reads centred.
    // Outside, the magnitude is remapped onto [0, 1] with the direction preserved. The result stays in
    // the unit disk, even for a hardware diagonal past it.
    internal (float X, float Y) Stick(float x, float y)
    {
        float magnitude = MathF.Sqrt((x * x) + (y * y));
        if (magnitude <= stickDeadzone)
        {
            return (0f, 0f);
        }

        float scale = Remap(magnitude, stickDeadzone) / magnitude;

        return (x * scale, y * scale);
    }

    // A raw trigger reading with the deadzone removed, remapped onto [0, 1].
    internal float Trigger(float value) =>
        value <= triggerDeadzone ? 0f : Remap(value, triggerDeadzone);

    private static float Remap(float value, float deadzone) =>
        MathF.Min((value - deadzone) / (1f - deadzone), 1f);
}
