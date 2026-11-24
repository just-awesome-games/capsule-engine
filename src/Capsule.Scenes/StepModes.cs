namespace Capsule.Scenes;

internal static class StepModes
{
    // Whether a step the scene paused or froze holds an owner of this mode. Inherit holds as a root's does.
    internal static bool Holds(this StepMode mode, bool paused, bool frozen) => mode switch
    {
        StepMode.Inherit or StepMode.Pausable => paused || frozen,
        StepMode.UnlessPaused => paused,
        StepMode.WhenPaused => !paused,
        StepMode.Never => true,
        _ => false,
    };
}
