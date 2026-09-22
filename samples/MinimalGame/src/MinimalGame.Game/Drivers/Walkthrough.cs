using Capsule.Input;
using Capsule.Scenes;

namespace MinimalGame.Game.Drivers;

/// <summary>
/// Plays the room with nobody at the keyboard: walks right along the floor, through the hazard
/// and under the first ledge, jumps up through it and lands on top, walks on a little, fires a
/// bolt, then presses Quit so the run ends by the game's own exit route. Every count is in fixed
/// steps. Run it with <c>--scene room --driver Walkthrough</c>, with or without <c>--headless</c>; it
/// plays the same steps either way and closes itself.
/// </summary>
public sealed class Walkthrough : IInputDriver
{
    private readonly IInputDriver _script = Script();

    /// <inheritdoc/>
    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot) => _script.TryNext(scene, tick, out snapshot);

    // A fixed sequence: the room's geometry is known, so nothing here reads the scene.
    private static IInputDriver Script()
    {
        InputScript script = new();

        // 128 world units at the walk speed, which parks the body fully beneath the first ledge.
        script.Down(Key.D).Wait(96).Up(Key.D);

        // The jump passes through the ledge's underside and lands on its top face inside a second.
        script.Tap(Key.Space).Wait(60);

        // A short walk along the ledge, then a pause to show the landing.
        script.Down(Key.D).Wait(24).Up(Key.D).Wait(30);

        // One bolt from the muzzle, given long enough to cross the frame.
        script.Tap(MouseButton.Left).Wait(60);

        script.Tap(Key.Escape);

        return script.Build();
    }
}
