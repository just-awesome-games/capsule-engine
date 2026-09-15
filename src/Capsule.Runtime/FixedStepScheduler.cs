using Capsule.Input;
using Capsule.Runtime.Scenes;

namespace Capsule.Runtime;

internal sealed class FixedStepScheduler
{
    private readonly double _stepSeconds;
    private readonly int _maxStepsPerFrame;
    private readonly InputState _input;
    private readonly SnapshotLatch _latch = new();

    // Null unless the run is driven. A driver supplies one snapshot per fixed step, so what the
    // simulation sees is independent of how many frames the host drew to reach that step.
    private readonly IInputDriver? _driver;

    // The scene a driver is shown, which a transition replaces under it.
    private readonly SceneHost? _scenes;

    private double _accumulatorSeconds;
    private double _timeScale = 1;
    private bool _driverFinished;
    private bool _held;

    internal FixedStepScheduler(
        double stepSeconds,
        int maxStepsPerFrame,
        ActionBindings bindings,
        IInputDriver? driver = null,
        SceneHost? scenes = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        if (driver is not null && scenes is null)
        {
            throw new ArgumentNullException(nameof(scenes), "An input driver is shown the scene it drives, so a driven run is a run of scenes.");
        }

        if (!double.IsFinite(stepSeconds) || stepSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepSeconds), stepSeconds, "The fixed step must be finite and greater than zero.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStepsPerFrame);

        _stepSeconds = stepSeconds;
        _maxStepsPerFrame = maxStepsPerFrame;
        _input = new InputState(bindings);
        _driver = driver;
        _scenes = scenes;
    }

    internal long Tick { get; private set; }

    internal bool Held
    {
        get => _held;
        set
        {
            if (_held == value)
            {
                return;
            }

            _held = value;
            if (value)
            {
                _accumulatorSeconds = 0;
                _latch.DiscardPending();
            }
        }
    }

    internal int StepsThisFrame { get; private set; }

    // Host pace: the simulation seconds a wall second is worth, greater than zero and finite.
    // It changes only how many steps a frame's elapsed time buys. The step length, the tick and
    // the time a step is handed are untouched, so a run at 0.25x is the same run as at 1x — one
    // played slower — and nothing of the simulation can read or record it. A single step is one
    // step at any scale, and the frame's step bound still binds, so past it a frame drops its
    // backlog rather than running faster.
    internal double TimeScale
    {
        get => _timeScale;
        set
        {
            if (!double.IsFinite(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The time scale must be finite and greater than zero.");
            }

            _timeScale = value;
        }
    }

    // Raised after each step completes, before the next one is scheduled. A frame may run several
    // steps and a step rewrites what the host has to act on, so once a frame would lose all but the
    // last. Null unless the host set one.
    internal Action? StepCompleted { get; set; }

    internal double AccumulatorSeconds => _accumulatorSeconds;

    internal float InterpolationAlpha => _held ? 1f : (float)(_accumulatorSeconds / _stepSeconds);

    internal bool Advance(double elapsedSeconds, in DeviceSnapshot snapshot, ISimulation simulation)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), elapsedSeconds, "Elapsed time must be finite and non-negative.");
        }

        StepsThisFrame = 0;

        // A single step taken during the hold may have exhausted the driver, which ends the run
        // the same way it does when a played step exhausts it.
        if (_held)
        {
            _accumulatorSeconds = 0;

            return _driverFinished || simulation.ExitRequested;
        }

        // Under a driver the sampled device is not input at all: the host still samples it for its
        // own fullscreen chord, and nothing of it reaches the simulation.
        if (_driver is null)
        {
            _latch.Observe(snapshot);
        }
        else if (_driverFinished)
        {
            return true;
        }

        _accumulatorSeconds += elapsedSeconds * _timeScale;

        double stepEpsilon = _stepSeconds * 1e-12;
        int stepsRun = 0;
        while (_accumulatorSeconds >= _stepSeconds ||
               Math.Abs(_accumulatorSeconds - _stepSeconds) <= stepEpsilon)
        {
            // Without this bound a step costing more than the step length schedules two steps next
            // frame, then three: the frame time spirals until the whole backlog runs every frame.
            if (stepsRun == _maxStepsPerFrame)
            {
                _accumulatorSeconds = 0;
                StepsThisFrame = stepsRun;

                return false;
            }

            DeviceSnapshot stepped;
            if (_driver is { } driver)
            {
                // Asked once per step, holding the scene the step is about to run.
                if (!driver.TryNext(_scenes!.Scene, Tick, out stepped))
                {
                    _driverFinished = true;
                    StepsThisFrame = stepsRun;

                    return true;
                }
            }
            else
            {
                stepped = _latch.ConsumeStepSnapshot();
            }

            _input.Advance(stepped);
            simulation.Step(new StepContext(_stepSeconds, _input, Tick));
            StepCompleted?.Invoke();

            _accumulatorSeconds -= _stepSeconds;
            if (_accumulatorSeconds < 0 && _accumulatorSeconds >= -stepEpsilon)
            {
                _accumulatorSeconds = 0;
            }

            stepsRun++;
            Tick++;
            StepsThisFrame = stepsRun;

            if (simulation.ExitRequested)
            {
                return true;
            }
        }

        return false;
    }

    // One fixed step while held, through the same input path a played step takes: the sampled
    // snapshot is latched and consumed, or the driver is asked for the step's snapshot. The
    // accumulator is untouched, so a held run stays drawn at the settled step. Returns whether the
    // simulation has requested exit.
    // `before` runs inside the step, once it has begun and before the simulation's own work: a
    // host act that is part of the tick it forces.
    internal bool StepOnce(in DeviceSnapshot snapshot, ISimulation simulation, Action? before = null)
    {
        if (!_held)
        {
            throw new InvalidOperationException("A single step is taken only while the scheduler is held.");
        }

        StepsThisFrame = 0;

        DeviceSnapshot stepped;
        if (_driver is { } driver)
        {
            if (_driverFinished || !driver.TryNext(_scenes!.Scene, Tick, out stepped))
            {
                _driverFinished = true;

                return true;
            }
        }
        else
        {
            _latch.Observe(snapshot);
            stepped = _latch.ConsumeStepSnapshot();
        }

        _input.Advance(stepped);
        StepContext context = new(_stepSeconds, _input, Tick);
        if (before is null)
        {
            simulation.Step(in context);
        }
        else
        {
            simulation.Step(in context, before);
        }

        StepCompleted?.Invoke();

        Tick++;
        StepsThisFrame = 1;

        return simulation.ExitRequested;
    }
}
