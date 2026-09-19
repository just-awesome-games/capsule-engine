namespace Capsule.Input;

/// <summary>How a timed <see cref="RumblePulse"/> changes over its duration.</summary>
public enum RumbleFade
{
    /// <summary>A linear envelope from 1 at the start of the pulse to 0 at its end. The default.</summary>
    Decay,

    /// <summary>Full amplitude for the whole pulse, then a cut.</summary>
    None,
}
