using System.Numerics;

namespace Capsule.Audio;

// Something in the world a positional voice follows. The mixer reads it once a step, at the settle.
// A position that is not finite silences the voice.
internal interface IAudioAnchor
{
    Vector2 Position { get; }
}
