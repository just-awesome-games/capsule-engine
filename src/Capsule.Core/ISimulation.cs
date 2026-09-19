using Capsule.Rendering;

namespace Capsule;

// A game, as the engine sees it. It owns all game state, advances it one fixed step at a time, and does
// not draw.
internal interface ISimulation
{
    // Set by the simulation to ask the runtime to shut down. The runtime does not clear it.
    bool ExitRequested { get; }

    // What to draw for the current state. It is read on every draw frame. An implementation returns a
    // held instance it rewrites once per step, not one built per call.
    FrameView View { get; }

    // Advances the simulation by one fixed step.
    void Step(in StepContext context);

    // Advances one fixed step, running `before` once the step has begun and ahead of the simulation's own
    // logic. A host act then lands inside the step, where between ticks the mixer's next step would clear
    // what it asked for. What `before` throws propagates.
    void Step(in StepContext context, Action before);
}
