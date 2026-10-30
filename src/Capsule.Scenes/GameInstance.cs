namespace Capsule.Scenes;

/// <summary>
/// The game's one run-scoped object, which lives from before the first scene starts to after the last
/// one stops. Every scene reads it as <see cref="Run.State{TGame}"/>.
/// </summary>
/// <remarks>
/// A shell passes its factory to <c>EngineBuilder.WithGame</c>, and a test that builds its run by hand
/// passes it to <see cref="Run.StartGame{TGame}"/>. Hold every run-wide object as a member of it, and
/// override <see cref="OnStart"/>, <see cref="OnStep"/> and <see cref="OnEnd"/> to give the run behaviour
/// no scene owns.
/// </remarks>
/// <example>
/// <code>
/// public sealed class MyGameInstance(Run run) : GameInstance(run)
/// {
///     protected override void OnStart() => MyGameInput.Configure(Run.Input);
/// }
///
/// CapsuleBoot.Configure("My Game").WithGame(static run => new MyGameInstance(run));
/// </code>
/// </example>
public abstract class GameInstance
{
    /// <summary>The run this object belongs to, readable from the constructor onward.</summary>
    protected Run Run { get; }

    /// <summary>Creates the game object of <paramref name="run"/>, the run whose factory builds it.</summary>
    /// <param name="run">The run this object belongs to, the one its factory was handed.</param>
    protected GameInstance(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);

        Run = run;
    }

    /// <summary>
    /// Runs once as the run starts, after its saves are restored and before its first scene is built.
    /// </summary>
    /// <remarks>
    /// A game binds its input and applies its saved settings here. The run's starting language is
    /// already set, and a language set here wins over it. <c>--language</c> is applied after it.
    /// </remarks>
    protected virtual void OnStart()
    {
    }

    /// <summary>
    /// Runs once at the start of every step, before the scene's own step, with the context the scene
    /// steps with.
    /// </summary>
    /// <remarks>
    /// It runs while the scene is paused or frozen. A step that takes a scene transition runs it once,
    /// before the outgoing scene steps, and the incoming scene starts after that step. A transition
    /// requested here is taken after the same step.
    /// </remarks>
    protected virtual void OnStep(in StepContext context)
    {
    }

    /// <summary>
    /// Runs once as a run that did not crash ends, after its last step and its last scene's stop, and
    /// before the host persists the saves for the last time.
    /// </summary>
    /// <remarks>
    /// A game writes what it saves at quit here, and the host's last save flush writes it out. A run ends
    /// after the step that calls <see cref="Run.RequestExit"/>, when the window closes or the system quits
    /// the game, and when a headless driver or recording finishes. A replay ends the same way, its saves
    /// kept in memory. A crash or a killed process skips it. <see cref="Run.Tick"/> and
    /// <see cref="Run.TotalSeconds"/> read the run's whole length.
    /// </remarks>
    protected virtual void OnEnd()
    {
    }

    internal void Start() => OnStart();

    internal void Step(in StepContext context) => OnStep(in context);

    internal void End() => OnEnd();
}
