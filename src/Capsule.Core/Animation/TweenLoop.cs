namespace Capsule.Animation;

/// <summary>What a <see cref="Tween"/> does with the tick after its last one.</summary>
public enum TweenLoop
{
    /// <summary>Holds the end and finishes.</summary>
    Once,

    /// <summary>Begins the run again from the start, giving a sawtooth that never finishes.</summary>
    Repeat,

    /// <summary>Runs back to the start and out again, giving a triangle that never finishes.</summary>
    PingPong,
}
