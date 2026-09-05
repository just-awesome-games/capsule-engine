using Capsule.Rendering;

namespace Capsule;

// A game, as the engine sees it: it owns all game state, advances it one fixed step at a time, and
// never draws — what it wants on screen is exposed through View.
internal interface ISimulation
{
    // Advances the simulation by exactly one fixed step.
    void Step(in StepContext context);

    // Set by the simulation to ask the runtime to shut down; the runtime never clears it.
    bool ExitRequested { get; }

    // What to draw for the current state. Read on every draw frame, so an implementation returns a
    // held instance it rewrites once per step, never one built per call.
    FrameView View { get; }
}
