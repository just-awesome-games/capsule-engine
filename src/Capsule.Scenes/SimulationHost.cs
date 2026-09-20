using Capsule.Input;
using Capsule.Rendering;

namespace Capsule.Scenes;

/// <summary>
/// Hosts one <see cref="SceneSimulation"/> with no platform behind it, owning its tick, input state and
/// disposal. The tick starts at zero and advances across calls. One input state holds buttons and detects
/// press edges for the whole run.
/// </summary>
public sealed class SimulationHost : IDisposable
{
    /// <summary>
    /// Starts <paramref name="scene"/> under a default or supplied run and advances it from tick 0. A host
    /// whose scene needs an entry payload builds the <see cref="SceneSimulation"/> itself and uses the other
    /// constructor.
    /// </summary>
    /// <param name="scene">The scene to start and advance, under <see cref="SceneSimulation"/>'s rules. Disposing the host stops it.</param>
    /// <param name="stepHertz">Simulation steps per second of simulated time. Positive, 60 by default.</param>
    /// <param name="run">The run to install on the scene. Omit it for a new run with default settings.</param>
    public SimulationHost(
        Scene scene,
        int stepHertz = StepContext.DefaultStepHertz,
        Run? run = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepHertz);

        // Check every argument before building the simulation, because building it starts the scene and a
        // rejected host would leave a started scene with nothing to stop it.
        StepSeconds = 1.0 / stepHertz;
        Simulation = new SceneSimulation(scene, run: run);
        Input = new InputState(Simulation.Run.Input.Bindings);
    }

    /// <summary>Runs <paramref name="simulation"/> from tick 0 and takes over disposing it.</summary>
    /// <param name="simulation">The simulation to advance. <see cref="Dispose"/> disposes it.</param>
    /// <param name="stepHertz">Simulation steps per second of simulated time. Positive, 60 by default.</param>
    public SimulationHost(SceneSimulation simulation, int stepHertz = StepContext.DefaultStepHertz)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepHertz);

        Simulation = simulation;
        StepSeconds = 1.0 / stepHertz;
        Input = new InputState(simulation.Run.Input.Bindings);
    }

    /// <summary>The simulation being advanced, for the lifetime of this host.</summary>
    public SceneSimulation Simulation { get; }

    /// <summary>The run shared by the simulation's scenes.</summary>
    public Run Run => Simulation.Run;

    /// <summary>Simulated seconds per step, the reciprocal of the host's step rate and constant for its life.</summary>
    public double StepSeconds { get; }

    /// <summary>The scene the simulation is running.</summary>
    public Scene Scene => Simulation.Scene;

    /// <summary>The input state every step of this host advances.</summary>
    public InputState Input { get; }

    /// <summary>
    /// The tick the next step will run at. It starts at 0 and is never reset. A step whose scene throws has
    /// still spent its tick.
    /// </summary>
    public long Tick { get; private set; }

    /// <summary>
    /// Advances <see cref="Input"/> to <paramref name="snapshot"/> and steps the simulation once at
    /// <see cref="Tick"/>, then moves the tick on.
    /// </summary>
    /// <param name="snapshot">The device state for this step. Omit it to hold nothing.</param>
    public void Step(in DeviceSnapshot snapshot = default)
    {
        Input.Advance(in snapshot);

        // The attempt spends the tick, not the successful return. A step whose scene throws still
        // counts, and the next step runs at the following tick.
        StepContext context = new(StepSeconds, Input, Tick++);
        Simulation.Step(in context);
    }

    /// <summary>Steps <paramref name="steps"/> times over one unchanging snapshot.</summary>
    /// <param name="steps">How many steps to run. Zero runs none.</param>
    /// <param name="snapshot">The device state every step sees. Omit it to hold nothing.</param>
    public void Step(int steps, in DeviceSnapshot snapshot = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(steps);

        for (int step = 0; step < steps; step++)
        {
            Step(in snapshot);
        }
    }

    /// <summary>
    /// Plays one snapshot per step until <paramref name="driver"/> finishes or the run requests exit.
    /// The driver receives the run's current tick, and exit is checked after each step. An exit
    /// requested during scene startup therefore still lets the first supplied snapshot play.
    /// </summary>
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
    /// Steps until <paramref name="condition"/> holds, checking it after each step and never running
    /// more than <paramref name="maxSteps"/> of them.
    /// </summary>
    /// <param name="condition">Read after every step and never before the first, which makes a budget of 0 always fail.</param>
    /// <param name="maxSteps">The most steps to run.</param>
    /// <param name="snapshot">The device state every step sees. Omit it to hold nothing.</param>
    /// <returns>Whether the condition held within the budget.</returns>
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
    /// Stops the simulation and releases all entities, completing cleanup even when a hook throws.
    /// Repeated calls do nothing.
    /// </summary>
    /// <exception cref="AggregateException">More than one stop hook failed. Each failure is an inner exception.</exception>
    public void Dispose() => Simulation.Dispose();
}
