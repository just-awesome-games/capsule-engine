using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;
using MinimalGame.Game.Entities;

namespace MinimalGame.Game.Drivers;

/// <summary>
/// Plays the room with nobody at the keyboard: walks right along the floor, through the hazard and
/// under the first ledge, jumps up through it and lands on top, walks on a little, fires a bolt at
/// the pointer, then picks Quit from the pause menu so the run ends by the game's own exit route.
/// Every count is in fixed steps. Run it with <c>--scene room --driver Walkthrough</c>, with or
/// without <c>--headless</c>; it plays the same steps either way and closes itself.
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

        // 128 world units at the walk speed, which parks the body fully beneath the first ledge. The
        // hazard on the way freezes the room for a few steps, and the walk is held that much longer.
        script.Down(Key.D).Wait(96 + PlayerTuning.Default.HurtFreezeTicks).Up(Key.D);

        // The jump passes through the ledge's underside and lands on its top face inside a second.
        script.Tap(Key.Space).Wait(60);

        // A short walk along the ledge, then a pause to show the landing.
        script.Down(Key.D).Wait(24).Up(Key.D).Wait(30);

        // One bolt from the muzzle at a point up and to the right of the player, given long enough
        // to cross the frame.
        script.MoveTo(new Vector2(300f, 40f)).Tap(MouseButton.Left).Wait(60);

        // Pause, move the focus from Resume down to Quit, and confirm it.
        script.Tap(Key.Escape).Wait(30);
        script.Tap(Key.Down).Wait(15);
        script.Tap(Key.Enter);

        return script.Build();
    }
}
