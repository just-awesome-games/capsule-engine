using Capsule.Input;

namespace Capsule.Runtime;

// The settled configuration a builder hands the host; validated on the way in.
internal sealed record EngineOptions(
    string WindowTitle,
    int WindowWidth,
    int WindowHeight,
    bool Resizable,
    bool Fullscreen,
    (int Width, int Height)? RenderResolution,
    double StepSeconds,
    int MaxStepsPerFrame,
    float StickDeadzone,
    float TriggerDeadzone,
    ActionBindings Bindings);
