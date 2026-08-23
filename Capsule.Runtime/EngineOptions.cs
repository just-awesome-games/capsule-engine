using Capsule.Input;
using Capsule.Rendering;

namespace Capsule.Runtime;

/// <summary>The settled configuration <see cref="EngineBuilder"/> hands the host; validated on the way in.</summary>
internal sealed record EngineOptions(
    string WindowTitle,
    int WindowWidth,
    int WindowHeight,
    bool Resizable,
    double StepSeconds,
    ColorRgba ClearColor,
    ActionBindings Bindings);
