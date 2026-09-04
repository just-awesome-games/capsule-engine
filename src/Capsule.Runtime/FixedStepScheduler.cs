using Capsule.Input;

namespace Capsule.Runtime;

internal sealed class FixedStepScheduler
{
    private readonly double _stepSeconds;
    private readonly double _maxFrameSeconds;
    private readonly InputState _input;
    private readonly SnapshotLatch _latch = new();

    private double _accumulatorSeconds;

    internal FixedStepScheduler(double stepSeconds, double maxFrameSeconds, ActionBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        if (!double.IsFinite(stepSeconds) || stepSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepSeconds), stepSeconds, "The fixed step must be finite and greater than zero.");
        }

        if (!double.IsFinite(maxFrameSeconds) || maxFrameSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrameSeconds), maxFrameSeconds, "The frame clamp must be finite and greater than zero.");
        }

        _stepSeconds = stepSeconds;
        _maxFrameSeconds = maxFrameSeconds;
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
        _accumulatorSeconds += Math.Min(elapsedSeconds, _maxFrameSeconds);

        double stepEpsilon = _stepSeconds * 1e-12;
        while (_accumulatorSeconds >= _stepSeconds ||
               Math.Abs(_accumulatorSeconds - _stepSeconds) <= stepEpsilon)
        {
            _input.Advance(_latch.ConsumeStepSnapshot());
            simulation.Step(new StepContext(_stepSeconds, _input, Tick));

            _accumulatorSeconds -= _stepSeconds;
            if (_accumulatorSeconds < 0 && _accumulatorSeconds >= -stepEpsilon)
            {
                _accumulatorSeconds = 0;
            }

            Tick++;

            if (simulation.ExitRequested)
            {
                return true;
            }
        }

        return false;
    }
}
