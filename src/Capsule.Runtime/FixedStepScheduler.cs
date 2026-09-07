using Capsule.Input;
using Capsule.Scenes.Input;

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
    private bool _driverFinished;

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

    internal double AccumulatorSeconds => _accumulatorSeconds;

    internal float InterpolationAlpha => (float)(_accumulatorSeconds / _stepSeconds);

    internal bool Advance(double elapsedSeconds, in DeviceSnapshot snapshot, ISimulation simulation)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), elapsedSeconds, "Elapsed time must be finite and non-negative.");
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

        _accumulatorSeconds += elapsedSeconds;

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
                return false;
            }

            DeviceSnapshot stepped;
            if (_driver is { } driver)
            {
                // Asked once per step, holding the scene the step is about to run.
                if (!driver.TryNext(_scenes!.Scene, Tick, out stepped))
                {
                    _driverFinished = true;

                    return true;
                }
            }
            else
            {
                stepped = _latch.ConsumeStepSnapshot();
            }

            _input.Advance(stepped);
            simulation.Step(new StepContext(_stepSeconds, _input, Tick));

            _accumulatorSeconds -= _stepSeconds;
            if (_accumulatorSeconds < 0 && _accumulatorSeconds >= -stepEpsilon)
            {
                _accumulatorSeconds = 0;
            }

            stepsRun++;
            Tick++;

            if (simulation.ExitRequested)
            {
                return true;
            }
        }

        return false;
    }
}
