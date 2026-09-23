using Capsule.Scenes;

namespace Capsule.Input;

/// <summary>
/// Supplies a run's input from code, one <see cref="DeviceSnapshot"/> per fixed step in step order.
/// </summary>
/// <remarks>
/// A driver replaces the keyboard, mouse and gamepad completely. The run it drives is reproducible
/// from its initial state, its fixed step and the driver alone.
/// <para>
/// The engine asks a driver exactly once per fixed step, whatever the frame rate, before that step
/// runs. The scene it reads is the world the previous step left. A game's shell reaches a driver by
/// name through <c>--driver</c>, which requires a public parameterless constructor.
/// <see cref="InputScript"/> builds one from a fixed sequence instead.
/// </para>
/// </remarks>
///
public interface IInputDriver
{
    /// <summary>Supplies the snapshot that drives the step at <paramref name="tick"/>.</summary>
    /// <param name="scene">
    /// The scene about to be stepped, which after a transition is the scene the run switched to. Read it to
    /// decide what to press. Changing it here drives the game from outside its own logic.
    /// </param>
    /// <param name="tick">The fixed step about to run, counted from 0 across the whole run.</param>
    /// <param name="snapshot">What the devices would have reported for that step.</param>
    /// <returns>
    /// False once the run is over, which ends it the way a player quitting would, and the declined step never
    /// runs. A driver that ends the run conditionally calls the game's own exit route instead.
    /// </returns>
    bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot);
}
