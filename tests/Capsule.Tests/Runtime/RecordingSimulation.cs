using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Runtime;

// What one step's input said about one action: its two edges and whether it was held.
internal readonly record struct ActionRead(bool Pressed, bool Released, bool Held);

// One step as the simulation saw it: the context it ran on, and each action the simulation was
// given, in the order they were given.
internal readonly record struct RecordedStep(
    long Tick,
    float DeltaSeconds,
    double TotalSeconds,
    ActionRead First,
    ActionRead Second,
    ActionRead Third);

// The simulation the scheduler and overlay specs step. It always counts its steps; it records them
// only for a spec that named the actions it reads, so a spec measuring allocation steps one that
// records nothing and still counts.
internal sealed class RecordingSimulation(params InputAction[] actions) : ISimulation
{
    internal List<RecordedStep> Recorded { get; } = [];

    // Steps run, counted whether or not they were recorded.
    internal int Steps { get; private set; }

    // The tick on which the simulation asks the run to end; null never asks.
    internal long? ExitOnTick { get; init; }

    public bool ExitRequested { get; private set; }

    public FrameView View { get; } = new();

    public void Step(in StepContext context)
    {
        Steps++;
        ExitRequested = context.Tick == ExitOnTick;

        if (actions.Length == 0)
        {
            return;
        }

        Recorded.Add(new RecordedStep(
            context.Tick,
            context.DeltaSeconds,
            context.TotalSeconds,
            Read(in context, 0),
            Read(in context, 1),
            Read(in context, 2)));
    }

    private ActionRead Read(in StepContext context, int index) =>
        index < actions.Length
            ? new ActionRead(
                context.Input.WasPressed(actions[index]),
                context.Input.WasReleased(actions[index]),
                context.Input.IsHeld(actions[index]))
            : default;
}
