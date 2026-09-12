using Capsule.Input;
using Capsule.Scenes.Input;

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
    InputConfiguration Input,
    // Null unless the run is driven in code instead of sampling the devices.
    IInputDriver? Driver)
{
    // The canvas rule: the declared render resolution, and the configured window where there is none.
    internal static (int Width, int Height) CanvasOf((int Width, int Height)? renderResolution, int windowWidth, int windowHeight) =>
        renderResolution ?? (windowWidth, windowHeight);
}
