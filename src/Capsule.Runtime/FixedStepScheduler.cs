using Capsule.Input;
using Capsule.Runtime.Input;

namespace Capsule.Runtime;

internal sealed class FixedStepScheduler
{
    private readonly double _stepSeconds;
    private readonly int _maxStepsPerFrame;
    private readonly InputState _input;
    private readonly SnapshotLatch _latch = new();

    // Null unless the run is replaying. A tape supplies one snapshot per fixed step, so what the
    // simulation sees is independent of how many frames the host drew to reach that step.
    private readonly InputTape? _tape;
    private readonly InputRecorder? _recorder;

    private double _accumulatorSeconds;

    internal FixedStepScheduler(
        double stepSeconds,
        int maxStepsPerFrame,
        ActionBindings bindings,
        InputTape? tape = null,
        InputRecorder? recorder = null)
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
        _tape = tape;
        _recorder = recorder;
    }

    internal long Tick { get; private set; }

    internal double AccumulatorSeconds => _accumulatorSeconds;

    internal float InterpolationAlpha => (float)(_accumulatorSeconds / _stepSeconds);

    // Whether a replay has run every step its tape holds, which is where the run ends.
    private bool TapeSpent => _tape is { } tape && Tick >= tape.Count;

    internal bool Advance(double elapsedSeconds, in DeviceSnapshot snapshot, ISimulation simulation)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), elapsedSeconds, "Elapsed time must be finite and non-negative.");
        }

        // Under a tape the sampled device is not input at all: the host still samples it for its
        // own fullscreen chord, and nothing of it reaches the simulation.
        if (_tape is null)
        {
            _latch.Observe(snapshot);
        }
        else if (TapeSpent)
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

            DeviceSnapshot stepped = _tape is { } tape ? tape[(int)Tick] : _latch.ConsumeStepSnapshot();
            _recorder?.Record(stepped);
            _input.Advance(stepped);
            simulation.Step(new StepContext(_stepSeconds, _input, Tick));

            _accumulatorSeconds -= _stepSeconds;
            if (_accumulatorSeconds < 0 && _accumulatorSeconds >= -stepEpsilon)
            {
                _accumulatorSeconds = 0;
            }

            stepsRun++;
            Tick++;

            if (simulation.ExitRequested || TapeSpent)
            {
                return true;
            }
        }

        return false;
    }
}
