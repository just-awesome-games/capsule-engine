namespace Capsule;

/// <summary>What a run over a range, such as a <see cref="Animation.Tween"/> or a path follower, does on reaching its end.</summary>
public enum LoopMode
{
    /// <summary>Holds the end and finishes.</summary>
    Once,

    /// <summary>Begins again from the start, giving a sawtooth that never finishes.</summary>
    Repeat,

    /// <summary>Runs back to the start and out again, giving a triangle that never finishes.</summary>
    PingPong,
}
