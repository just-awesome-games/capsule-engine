using Capsule.Input;

namespace Capsule.Runtime;

internal sealed class FixedStepScheduler
{
    private readonly double _stepSeconds;
    private readonly int _maxStepsPerFrame;
    private readonly InputState _input;
    private readonly SnapshotLatch _latch = new();

    private double _accumulatorSeconds;

    internal FixedStepScheduler(double stepSeconds, int maxStepsPerFrame, ActionBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        if (!double.IsFinite(stepSeconds) || stepSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepSeconds), stepSeconds, "The fixed step must be finite and greater than zero.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStepsPerFrame);

        _stepSeconds = stepSeconds;
        _maxStepsPerFrame = maxStepsPerFrame;
        _input = new InputState(bindings);
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

        _latch.Observe(snapshot);
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

            _input.Advance(_latch.ConsumeStepSnapshot());
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
