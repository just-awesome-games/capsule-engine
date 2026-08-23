using Capsule.Rendering;

namespace Capsule;

/// <summary>
/// A game, as the engine sees it. The runtime owns the clock, the window and the
/// device; a simulation owns all game state, advances it one fixed step at a time,
/// and never draws — it exposes render intent through <see cref="View"/>.
/// </summary>
public interface ISimulation
{
    /// <summary>Advances the simulation by exactly one fixed step.</summary>
    void Step(in StepContext context);

    /// <summary>Set by the simulation to ask the runtime to shut down; the runtime never clears it.</summary>
    bool ExitRequested { get; }

    /// <summary>
    /// What to draw for the current state. Read every frame, so an implementation
    /// returns a held instance rather than building one per call.
    /// </summary>
    FrameView View { get; }
}
