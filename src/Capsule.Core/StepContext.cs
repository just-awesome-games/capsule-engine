using System.Numerics;
using Capsule.Input;

namespace Capsule;

/// <summary>Everything the runtime hands a simulation for one fixed step.</summary>
public readonly struct StepContext
{
    /// <summary>The fixed step rate a run uses unless the host configures another, at 60 steps per second.</summary>
    public const int DefaultStepHertz = 60;

    internal StepContext(double stepSeconds, InputState input, long tick, Vector2 output = default)
    {
        StepSeconds = stepSeconds;
        Input = input;
        Tick = tick;
        Output = output;
    }

    // The step length at the precision the host configured. The engine's clocks derive from this, not
    // from the rounded DeltaSeconds a game reads.
    internal double StepSeconds { get; }

    /// <summary>Simulated seconds this step represents. Constant for a given engine configuration.</summary>
    public float DeltaSeconds => (float)StepSeconds;

    /// <summary>Action-level input for this step. It is the same instance across every step of a run.</summary>
    public InputState Input { get; }

    /// <summary>Index of this step. The first step ever delivered is 0.</summary>
    public long Tick { get; }

    // The extent in pixels of the output this step's frame draws to. Only its aspect is read, by the
    // camera settling the region that frame shows; zero headless, where every fit is the declared span.
    internal Vector2 Output { get; }

    /// <summary>Simulated seconds at the start of this step, not wall clock.</summary>
    /// <remarks>
    /// The value is <see cref="Tick"/> times the double-precision step length, not a running sum. It does
    /// not drift over a long run and carries none of the rounding in <see cref="DeltaSeconds"/>.
    /// </remarks>
    public double TotalSeconds => Tick * StepSeconds;
}
