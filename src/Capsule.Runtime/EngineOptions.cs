using Capsule.Input;

namespace Capsule.Runtime;

/// <summary>The settled configuration a builder hands the host; validated on the way in.</summary>
internal sealed record EngineOptions(
    string WindowTitle,
    int WindowWidth,
    int WindowHeight,
    bool Resizable,
    bool Fullscreen,
    (int Width, int Height)? RenderResolution,
    double StepSeconds,
    double MaxFrameSeconds,
    float StickDeadzone,
    float TriggerDeadzone,
    ActionBindings Bindings);
