namespace Capsule.Runtime.Audio;

internal static class SoundPitch
{
    // The backend accepts +-10 octaves and throws outside them, while the mixer accepts any positive
    // finite rate, so the conversion clamps instead of turning a game's arithmetic into a crash.
    private const float MaxOctaves = 10f;

    // The mixer's pitch is a playback-rate multiplier. The backend's is octaves off unit rate.
    internal static float Octaves(float rate) =>
        rate <= 0f ? -MaxOctaves : Math.Clamp(MathF.Log2(rate), -MaxOctaves, MaxOctaves);
}
