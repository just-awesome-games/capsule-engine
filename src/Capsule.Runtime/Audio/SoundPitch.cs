namespace Capsule.Runtime.Audio;

internal static class SoundPitch
{
    // The backend accepts +-10 octaves and throws outside them, while the mixer accepts any positive
    // finite rate, so the conversion clamps rather than propagating a game's arithmetic as a crash.
    private const float MaxOctaves = 10f;

    // The mixer's pitch is a playback-rate multiplier; the backend's is octaves off unit rate.
    internal static float Octaves(float rate) =>
        rate <= 0f ? -MaxOctaves : Math.Clamp(MathF.Log2(rate), -MaxOctaves, MaxOctaves);
}
