using System.Numerics;
using Capsule.Input;
using Capsule.Runtime.Scenes;

namespace Capsule.Runtime;

internal sealed class FixedStepScheduler
{
    private readonly double _stepSeconds;
    private readonly int _maxStepsPerFrame;
    private readonly InputState _input;
    private readonly SnapshotLatch _latch = new();

    // Null unless the run is driven. A driver supplies one snapshot per fixed step, so the
    // simulation sees the same input whatever the frame rate.
    private readonly IInputDriver? _driver;

    // The scene a driver is shown. A transition replaces it under the driver.
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
            throw new ArgumentNullException(nameof(scenes), "An input driver needs the scene it drives. Pass a scene host for a driven run.");
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

    // The device the last consumed step read as active, for the host's pad-facing output.
    internal InputDevice ActiveDevice => _input.ActiveDevice;

    // Seeds the run's initial active device. Called before the first step.
    internal void SeedDevice(InputDevice device) => _input.Seed(device);

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

    // Host pace: the simulation seconds a wall second is worth, finite and greater than zero. It
    // changes only how many steps a frame's elapsed time buys. The step length, the tick and the
    // time a step is handed stay the same. A run at 0.25x is the 1x run played slower, and the
    // simulation cannot read or record it. The frame's step bound still binds, so past it a frame
    // drops its backlog instead of running faster.
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

    // The extent in pixels of the output the host draws this run to. Driven runs get it too: a
    // driver scripts input, the host still owns the output.
    internal Vector2 Output { get; set; }

    // Raised after each step completes, before the next is scheduled. A step rewrites what the host
    // acts on and a frame may run several, and a per-frame call would lose all but the last. Null
    // unless the host set one.
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

        // A single step taken during the hold may have exhausted the driver, which ends the run as
        // a played step would.
        if (_held)
        {
            _accumulatorSeconds = 0;

            return _driverFinished || simulation.ExitRequested;
        }

        // Under a driver the sampled device is not input. The host samples it for the fullscreen
        // chord, and none of it reaches the simulation.
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
            // frame, then three, and the frame time spirals.
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
                stepped = _latch.Consume();
            }

            _input.Advance(stepped);
            simulation.Step(new StepContext(_stepSeconds, _input, Tick, Output));
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

    // One fixed step while held, through the input path a played step takes. The sampled snapshot is
    // latched and consumed, or the driver is asked for the step's snapshot. The accumulator is
    // untouched, and a held run stays drawn at the settled step. Returns whether the simulation has
    // requested exit. `before` runs inside the step, after it begins and before the simulation's own
    // work.
    internal bool StepOnce(in DeviceSnapshot snapshot, ISimulation simulation, Action? before = null)
    {
        if (!_held)
        {
            throw new InvalidOperationException("The scheduler is not held. Hold it before taking a single step.");
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
            stepped = _latch.Consume();
        }

        _input.Advance(stepped);
        StepContext context = new(_stepSeconds, _input, Tick, Output);
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
