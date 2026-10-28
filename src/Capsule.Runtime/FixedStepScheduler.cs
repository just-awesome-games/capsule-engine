using System.Diagnostics;
using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Runtime.DevTools.Recordings;
using Capsule.Runtime.Scenes;

namespace Capsule.Runtime;

internal sealed class FixedStepScheduler
{
    private readonly double _stepSeconds;
    private readonly int _maxStepsPerFrame;

    // The real time, in Stopwatch ticks, a frame's steps may take before it stops catching up: one
    // step length.
    private readonly long _budgetTicks;
    private readonly Func<long> _timestamp;
    private readonly InputState _input;
    private readonly SnapshotLatch _latch = new();

    // Null unless the run is driven. A driver supplies one snapshot per fixed step. The
    // simulation sees the same input whatever the frame rate.
    private readonly IInputDriver? _driver;

    // The scene a driver is shown. A transition replaces it under the driver.
    private readonly SceneHost? _scenes;

    private double _accumulatorSeconds;
    private double _timeScale = 1;
    private bool _driverFinished;
    private bool _held;

    internal long Tick { get; private set; }

    internal int StepsThisFrame { get; private set; }

    // Takes every step a windowed run runs, a replayed one included. Null in a headless run, a run the
    // shell does not record and a shipping build, where a step pays a null check.
    internal Recorder? Recorder { get; }

    // Supplies each step's input to the recording's end. Null when the run replays nothing, and once a
    // windowed replay has handed over to live input.
    internal Replay? Replay { get; private set; }

    // The extent in pixels of the output the host draws this run to. Driven runs get it too: a
    // driver scripts input, the host still owns the output. A replay writes the recorded one.
    internal Vector2 Output { get; set; }

    // The shortest seconds between the window's frames, set by a frame cap or vertical sync, 0 when
    // neither holds them. Such a frame covers its whole period. It may run every step the period buys
    // at the run's pace and spend the period stepping, and neither slows the simulation.
    internal double FramePeriodSeconds { get; set; }

    // Raised after each step completes, before the next is scheduled. A step rewrites what the host
    // acts on and a frame may run several, and a per-frame call would lose all but the last. Null
    // unless the host set one.
    internal Action? StepCompleted { get; set; }

    internal FixedStepScheduler(
        double stepSeconds,
        int maxStepsPerFrame,
        ActionBindings bindings,
        IInputDriver? driver = null,
        SceneHost? scenes = null,
        Func<long>? timestamp = null,
        Recorder? recorder = null,
        Replay? replay = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        if ((driver is not null || replay is not null) && scenes is null)
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
        _budgetTicks = (long)(stepSeconds * Stopwatch.Frequency);
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
        _input = new InputState(bindings);
        _driver = driver;
        _scenes = scenes;
        if (Development.IsSupported)
        {
            Recorder = recorder;
            Replay = replay;
            replay?.Seed(_input);
        }
    }

    // The device the last consumed step read as active, for the host's pad-facing output.
    internal InputDevice ActiveDevice => _input.ActiveDevice;

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
            else
            {
                HandOverFinishedReplay();
            }
        }
    }

    // Host pace: the simulation seconds a wall second is worth, finite and greater than zero. It
    // changes only how many steps a frame's elapsed time buys. The step length, the tick and the
    // time a step is handed stay the same. A run at 0.25x is the 1x run played slower, and the
    // simulation cannot read or record it. A frame still stops catching up at the step bound or the
    // step budget, and drops its backlog there instead of running faster.
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

    internal double AccumulatorSeconds => _accumulatorSeconds;

    internal float InterpolationAlpha => _held ? 1f : (float)(_accumulatorSeconds / _stepSeconds);

    // Seeds the run's initial active device. Called before the first step. A replay keeps the recorded one.
    internal void SeedDevice(InputDevice device)
    {
        if (Replay is null)
        {
            _input.Seed(device);
        }
    }

    // Reads key characters from the player's keyboard layout, which the host rewrites in place on a
    // change. A driven run ignores it and reads a US layout's, as a headless run does. A replay reads the
    // recorded layout, and keeps it after handing over to live input.
    internal void UseKeyLayout(string?[] characters)
    {
        if (_driver is null && Replay is null)
        {
            _input.UseKeyLayout(characters);
        }
    }

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

        // A replay standing at its end runs nothing until it is released.
        if (Replay is { Finished: true })
        {
            _accumulatorSeconds = 0;

            return false;
        }

        // Under a driver or a replay the sampled device is not input, and none of it reaches the
        // simulation.
        if (_driver is null && Replay is null)
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
        long started = _timestamp();

        // One step past the steps the period buys, for a frame that woke a little late.
        int maxSteps = Math.Max(_maxStepsPerFrame, (int)Math.Ceiling(FramePeriodSeconds * _timeScale / _stepSeconds) + 1);
        long budgetTicks = Math.Max(_budgetTicks, (long)(FramePeriodSeconds * Stopwatch.Frequency));
        while (_accumulatorSeconds >= _stepSeconds ||
               Math.Abs(_accumulatorSeconds - _stepSeconds) <= stepEpsilon)
        {
            // A frame stops catching up at the step bound, or once its steps have taken a step length
            // of real time, each widened to the frame's period. Steps that cost more than they
            // cover only lengthen the next frame and grow its backlog. The first due step always
            // runs. An overloaded game slows down and keeps its frame rate.
            if (stepsRun == maxSteps || (stepsRun > 0 && _timestamp() - started >= budgetTicks))
            {
                _accumulatorSeconds = 0;
                StepsThisFrame = stepsRun;

                return false;
            }

            if (!TryTakeInput(out DeviceSnapshot stepped, out Action? act))
            {
                StepsThisFrame = stepsRun;

                return InputEnded();
            }

            RunStep(in stepped, simulation, act);

            _accumulatorSeconds -= _stepSeconds;
            if (_accumulatorSeconds < 0 && _accumulatorSeconds >= -stepEpsilon)
            {
                _accumulatorSeconds = 0;
            }

            stepsRun++;
            StepsThisFrame = stepsRun;

            if (simulation.ExitRequested)
            {
                return true;
            }
        }

        return false;
    }

    // One fixed step while held, through the input path a played step takes. The sampled snapshot is
    // latched and consumed, or the driver or the replay is asked for the step's snapshot. The
    // accumulator is untouched, and a held run stays drawn at the settled step. Returns whether the
    // simulation has requested exit. `before` runs inside the step, after it begins and before the
    // simulation's own work.
    internal bool StepOnce(in DeviceSnapshot snapshot, ISimulation simulation, Action? before = null)
    {
        if (!_held)
        {
            throw new InvalidOperationException("The scheduler is not held. Hold it before taking a single step.");
        }

        StepsThisFrame = 0;

        // A step taken at a finished replay's hold is the first live one, the overlay's act in it.
        HandOverFinishedReplay();
        if (_driverFinished)
        {
            return true;
        }

        if (_driver is null && Replay is null)
        {
            _latch.Observe(snapshot);
        }

        if (!TryTakeInput(out DeviceSnapshot stepped, out Action? act))
        {
            return InputEnded();
        }

        RunStep(in stepped, simulation, act + before);
        StepsThisFrame = 1;

        return simulation.ExitRequested;
    }

    // A windowed replay's fast-forward: replayed steps back to back until budgetTicks of real time have
    // passed or the replay ends, with no accumulator. Returns whether the simulation requested exit.
    internal bool FastForward(long budgetTicks, ISimulation simulation)
    {
        StepsThisFrame = 0;
        _accumulatorSeconds = 0;
        if (_held)
        {
            return simulation.ExitRequested;
        }

        long started = _timestamp();
        while (Replay is { Finished: false } && _timestamp() - started < budgetTicks)
        {
            if (!TryTakeInput(out DeviceSnapshot stepped, out Action? act))
            {
                return InputEnded();
            }

            RunStep(in stepped, simulation, act);
            StepsThisFrame++;
            if (simulation.ExitRequested)
            {
                return true;
            }
        }

        return false;
    }

    // A windowed replay at its end hands the run to the devices from this tick, when it is released or
    // when a step is taken from its hold.
    private void HandOverFinishedReplay()
    {
        if (Replay is { Finished: true } replay)
        {
            replay.HandedOver = true;
            Replay = null;
        }
    }

    // The step's input: the replay's, the driver's, or the latched sample. False once the replay or the
    // driver has none left. act is a replayed host act to run inside the step.
    private bool TryTakeInput(out DeviceSnapshot stepped, out Action? act)
    {
        act = null;
        if (Development.IsSupported && Replay is { } replay)
        {
            Vector2 output = Output;
            bool more = replay.TryNext(Tick, ref output, out stepped, out act);
            Output = output;

            return more;
        }

        if (_driver is { } driver)
        {
            // Asked once per step, holding the scene the step is about to run.
            return driver.TryNext(_scenes!.Scene, Tick, out stepped);
        }

        stepped = _latch.Consume();

        return true;
    }

    // A driver's end ends the run. A replay's ends it too unless the replay holds at its end, which
    // leaves the run standing on its last tick until the host releases it.
    private bool InputEnded()
    {
        if (Replay is { HoldsAtEnd: true })
        {
            _accumulatorSeconds = 0;

            return false;
        }

        _driverFinished = true;

        return true;
    }

    private void RunStep(in DeviceSnapshot stepped, ISimulation simulation, Action? before)
    {
        if (Development.IsSupported)
        {
            Recorder?.Step(in stepped, Output, _input);
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

        if (Development.IsSupported)
        {
            Recorder?.Stepped(Tick);
            Replay?.Stepped(Tick);
        }
    }
}
