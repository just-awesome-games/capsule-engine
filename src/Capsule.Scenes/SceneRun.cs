using Capsule.Input;
using Capsule.Scenes.Input;

namespace Capsule.Scenes;

/// <summary>
/// Plays the host's role over one <see cref="SceneSimulation"/>: it owns the step tick and the
/// run's <see cref="InputState"/>, so a test drives a scene the way the engine's own loop does with
/// no window, no graphics device and no host. Substrate-free, like the simulation it wraps.
/// </summary>
/// <remarks>
/// The tick counts from 0 across every call on the instance and is never reset, and one
/// <see cref="InputState"/> carries held and pressed for the run's whole life. Both are contracts a
/// scene observes rather than bookkeeping: a <see cref="Animation.SpriteAnimator"/> positions a
/// clip against the step it was played on, so a run that restarted the count would read a play from
/// the step before as this step's and hold that frame an extra tick, and a fresh input each step
/// would report everything held as newly pressed.
/// </remarks>
public sealed class SceneRun : IDisposable
{
    /// <summary>
    /// Starts <paramref name="scene"/> under the default scene settings and runs it from tick 0:
    /// the common case, where nothing about the composition is under test. A run that needs an
    /// entry payload, its own <see cref="SceneDefaults"/> or a seeded <see cref="RandomSource"/>
    /// builds the <see cref="SceneSimulation"/> itself and uses the other constructor.
    /// </summary>
    /// <param name="scene">The scene to start and advance; disposing the run stops it.</param>
    /// <param name="input">
    /// The action-level input every step advances, held for the run's life; omitted, a state over
    /// empty <see cref="ActionBindings"/>, which reads every action as unbound.
    /// </param>
    /// <param name="stepHertz">Simulation steps per second of simulated time; positive, 60 by default.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The step rate is not positive.</exception>
    /// <exception cref="InvalidOperationException">The scene has already been started.</exception>
    /// <exception cref="AggregateException">Starting the scene failed and stopping it then failed too; both are inner exceptions.</exception>
    public SceneRun(Scene scene, InputState? input = null, int stepHertz = StepContext.DefaultStepHertz)
        : this(new SceneSimulation(Rated(scene, stepHertz)), input, stepHertz)
    {
    }

    /// <summary>
    /// Runs <paramref name="simulation"/> from tick 0, and takes over disposing it: for a run whose
    /// scene was composed with an entry payload, scene defaults or a seeded random source.
    /// </summary>
    /// <param name="simulation">
    /// The simulation to advance. This run does not compose the scene, but it does end it:
    /// <see cref="Dispose"/> disposes the simulation it was handed.
    /// </param>
    /// <param name="input">
    /// The action-level input every step advances, held for the run's life; omitted, a state over
    /// empty <see cref="ActionBindings"/>, which reads every action as unbound.
    /// </param>
    /// <param name="stepHertz">Simulation steps per second of simulated time; positive, 60 by default.</param>
    /// <exception cref="ArgumentNullException"><paramref name="simulation"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The step rate is not positive.</exception>
    public SceneRun(SceneSimulation simulation, InputState? input = null, int stepHertz = StepContext.DefaultStepHertz)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepHertz);

        Simulation = simulation;
        Input = input ?? new InputState(new ActionBindings());
        StepSeconds = 1.0 / stepHertz;
    }

    /// <summary>The simulation being advanced, for the lifetime of this run.</summary>
    public SceneSimulation Simulation { get; }

    /// <summary>
    /// Simulated seconds one step of this run represents, constant for its life: the reciprocal of
    /// the rate it was built with. What a distance covered over a run is measured against, so a
    /// caller multiplies by this rather than restating the rate it passed.
    /// </summary>
    public double StepSeconds { get; }

    /// <summary>The scene the simulation is running.</summary>
    public Scene Scene => Simulation.Scene;

    /// <summary>The one input state every step of this run advances.</summary>
    public InputState Input { get; }

    /// <summary>
    /// The tick the next step will run at; 0 at construction, and never reset. A step whose scene
    /// throws has still spent its tick.
    /// </summary>
    public long Tick { get; private set; }

    /// <summary>
    /// Advances <see cref="Input"/> to <paramref name="snapshot"/> and steps the simulation once at
    /// <see cref="Tick"/>, which then moves on.
    /// </summary>
    /// <param name="snapshot">The device state for this step; omitted, nothing is held.</param>
    /// <exception cref="ObjectDisposedException">The simulation has been disposed.</exception>
    public void Step(in DeviceSnapshot snapshot = default)
    {
        Input.Advance(in snapshot);

        // The tick is spent by the attempt, not by the return: a step whose scene throws is the
        // tick that was run, and the next one is the tick after it, as it is for the host.
        StepContext context = new(StepSeconds, Input, Tick++);
        Simulation.Step(in context);
    }

    /// <summary>Steps <paramref name="steps"/> times over one unchanging snapshot.</summary>
    /// <param name="steps">How many steps to run; 0 runs none.</param>
    /// <param name="snapshot">The device state every one of them sees; omitted, nothing is held.</param>
    /// <exception cref="ArgumentOutOfRangeException">The count is negative.</exception>
    /// <exception cref="ObjectDisposedException">The simulation has been disposed.</exception>
    public void Run(int steps, in DeviceSnapshot snapshot = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(steps);

        for (int step = 0; step < steps; step++)
        {
            Step(in snapshot);
        }
    }

    /// <summary>
    /// Drives the run from <paramref name="driver"/>, one snapshot per step, until the driver
    /// declines to supply one or the scene has requested exit, exactly as a windowed or headless run
    /// ends. The driver is asked for the tick the step will run at, so a run already under way is
    /// offered to a driver at the tick it stands on rather than at 0.
    /// </summary>
    /// <remarks>
    /// The exit is read after the step that asked for it, never before one, which is the host's own
    /// order: a scene that requests exit from its start still takes the driver's first step, and a
    /// snapshot the driver has already supplied is never discarded unplayed.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="driver"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The simulation has been disposed.</exception>
    public void Play(IInputDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        while (driver.TryNext(Scene, Tick, out DeviceSnapshot snapshot))
        {
            Step(in snapshot);

            if (Simulation.ExitRequested)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Steps until <paramref name="condition"/> holds, checking it after each step and never
    /// running more than <paramref name="maxSteps"/> of them.
    /// </summary>
    /// <param name="condition">Read after every step; it is never read before the first one, so a budget of 0 always fails.</param>
    /// <param name="maxSteps">The most steps to run; the run stops on the step the condition first holds after.</param>
    /// <param name="snapshot">The device state every step sees; omitted, nothing is held.</param>
    /// <returns>Whether the condition held within the budget.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="condition"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The budget is negative.</exception>
    /// <exception cref="ObjectDisposedException">The simulation has been disposed.</exception>
    public bool RunUntil(Func<bool> condition, int maxSteps, in DeviceSnapshot snapshot = default)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSteps);

        for (int step = 0; step < maxSteps; step++)
        {
            Step(in snapshot);

            if (condition())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Stops the simulation, which runs every stop hook and releases every entity the scene holds.
    /// A run whose test asserts nothing about teardown may skip it: the scene holds no unmanaged
    /// resource and no host handle, and nothing outlives the test.
    /// </summary>
    /// <exception cref="Exception">One stop hook failed; teardown still completed for every entity.</exception>
    /// <exception cref="AggregateException">More than one stop hook failed; each is an inner exception.</exception>
    public void Dispose() => Simulation.Dispose();

    // Constructing the simulation starts the scene, and a rate the chained constructor then rejects
    // would leave it started with nothing holding it to stop. Checked on the way in, a rejected run
    // never starts the scene, so the caller can build another over the same one.
    private static Scene Rated(Scene scene, int stepHertz)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepHertz);

        return scene;
    }
}
