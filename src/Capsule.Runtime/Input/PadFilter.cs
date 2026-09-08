namespace Capsule.Runtime.Input;

// Capsule's own deadzone filtering, applied to raw backend axis readings. One instance carries the
// radii a host was configured with.
internal readonly struct PadFilter(float stickDeadzone, float triggerDeadzone)
{
    private readonly float _stickDeadzone = stickDeadzone;
    private readonly float _triggerDeadzone = triggerDeadzone;

    // A raw stick reading with the deadzone removed radially: inside the radius it reads centred,
    // outside it the magnitude is remapped onto [0, 1] with the direction preserved. The result
    // never leaves the unit disk, even for a hardware diagonal past it.
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

    // A raw trigger reading with the deadzone removed, remapped onto [0, 1].
    internal float Trigger(float value) =>
        value <= _triggerDeadzone ? 0f : Remap(value, _triggerDeadzone);

    // Whether a pull already through Trigger counts as a button press: the deadzone is the press
    // threshold, so anything the filter did not zero is a press.
    internal static bool TriggerHeld(float filteredPull) => filteredPull > 0f;

    private static float Remap(float value, float deadzone) =>
        MathF.Min((value - deadzone) / (1f - deadzone), 1f);
}
