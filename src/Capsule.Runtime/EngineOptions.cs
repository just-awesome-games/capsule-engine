using Capsule.Input;
using Capsule.Scenes;

namespace Capsule.Runtime;

// The settled configuration a builder hands the host; validated on the way in.
internal sealed record EngineOptions(
    string WindowTitle,
    int WindowWidth,
    int WindowHeight,
    bool Resizable,
    bool Fullscreen,
    (int Width, int Height)? RenderResolution,
    (int Width, int Height)? Canvas,
    double StepSeconds,
    int MaxStepsPerFrame,
    InputConfiguration Input,
    // Null unless the run is driven in code instead of sampling the devices.
    IInputDriver? Driver,
    // The registered scenes, which the development overlay lists.
    SceneRegistry Scenes)
{
    // The canvas rule: the declared canvas; else the declared render resolution; else the window
    // the run was configured to open at.
    internal static (int Width, int Height) CanvasOf(
        (int Width, int Height)? canvas,
        (int Width, int Height)? renderResolution,
        int windowWidth,
        int windowHeight) =>
        canvas ?? renderResolution ?? (windowWidth, windowHeight);
}
