using Capsule.Input;

namespace Capsule.Scenes.Input;

/// <summary>
/// A run's input in code: one <see cref="DeviceSnapshot"/> per fixed step, in step order. A driver
/// replaces the keyboard and gamepad entirely, so the run it drives is reproducible from its
/// initial state, its fixed step and this class alone.
/// </summary>
/// <remarks>
/// A driver is asked exactly once per fixed step whatever the frame rate, before that step runs, so
/// what it reads of the scene is the world the previous step left. A game's shell reaches a driver
/// by name through <c>--driver</c>, which needs a public parameterless constructor;
/// <see cref="InputScript"/> builds one from a fixed sequence instead.
/// </remarks>
public interface IInputDriver
{
    /// <summary>The snapshot the step at <paramref name="tick"/> is driven by.</summary>
    /// <param name="scene">
    /// The scene about to be stepped, which is the scene a transition switched to from the step
    /// after it. Read it to decide what to press; changing it from here is driving the game from
    /// outside its own logic.
    /// </param>
    /// <param name="tick">The fixed step about to run, counting from 0 over the whole run.</param>
    /// <param name="snapshot">What the devices would have reported for that step.</param>
    /// <returns>
    /// False once the run is over, which ends it as the last press of a play session would; the
    /// step it declined never runs. A driver that ends the run conditionally instead calls the
    /// game's own exit route.
    /// </returns>
    bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot);
}
