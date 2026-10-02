namespace Capsule.UI;

// Repeats a held input after delay reads, then every interval reads. Zero interval never repeats.
internal struct HoldRepeat
{
    // Reads since the press. A press or a release restarts it.
    private int _held;

    internal bool Next(bool held, bool pressed, int delay, int interval)
    {
        _held = held && !pressed ? _held + 1 : 0;

        return interval > 0 && _held >= delay && (_held - delay) % interval == 0;
    }
}
