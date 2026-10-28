using System.Diagnostics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Runtime.DevTools.Recordings;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime;

// The host with no window or media, which plays a driver or a recording through the scenes a windowed
// run would host. Transitions, the seed and the run settings behave as they do in a window. It steps as
// fast as its input allows, or in real-time frames when frame diagnostics are on, unless uncapped. It
// records nothing. It
// takes exactly one of driver and replay, and watch runs after each step with the scene and the tick the
// step ran at. A trace and a capture write the overlay's ticks, one past the step's, from tick 0, the
// opening scene before its first step. An exiting step tears its scene down and leaves no tick to write.
// Either one halts the run at the first tick past its ceiling. A capture draws on its own device, and with
// none the host draws nothing.
internal sealed class HeadlessHost(
    EngineBuilder builder,
    SceneHost scenes,
    IInputDriver? driver,
    Replay? replay,
    FrameDiagnostics? diagnostics,
    Action<Scene, long>? watch = null,
    RunTrace? trace = null,
    RunCapture? capture = null)
{
    private readonly FixedStepScheduler _scheduler =
        new(builder.StepSeconds, builder.MaxStepsPerFrame, builder.Input.Bindings, driver, scenes, builder.Timestamp, replay: replay);

    internal long SimulationTick => _scheduler.Tick;

    // Whether a trace or a capture reached a tick past its ceiling.
    private bool Halted => Development.IsSupported && (trace?.Ticks.HaltedAt is not null || capture?.Ticks.HaltedAt is not null);

    internal HeadlessRunResult Run()
    {
        if (watch is not null || trace is not null || capture is not null)
        {
            _scheduler.StepCompleted = StepCompleted;
        }

        if (Development.IsSupported)
        {
            capture?.Attach(scenes);
            Write(0);
        }

        if (diagnostics is null)
        {
            // One step's worth of time per call. The accumulator drains a single step and
            // neither the step bound nor the step budget can bind.
            while (!_scheduler.Advance(builder.StepSeconds, DeviceSnapshot.Empty, scenes) && !Halted)
            {
                EndFrame();
            }
        }
        else
        {
            PaceFrames(diagnostics);
        }

        // The advance that ends the run may have run a step whose request and writes the loop
        // body never sees.
        EndFrame();

        return new HeadlessRunResult(_scheduler.Tick, scenes.ExitRequested, scenes.View.Metrics, replay?.DivergedAt);
    }

    private void StepCompleted()
    {
        watch?.Invoke(scenes.Scene, _scheduler.Tick);
        if (Development.IsSupported && !scenes.ExitRequested)
        {
            Write(_scheduler.Tick + 1);
        }
    }

    // A capture lays its frame out at the extent the step saw, which a replay holds as recorded. A halt ends
    // the frame at the step it was reached in, and neither writes again.
    private void Write(long tick)
    {
        if (!Halted)
        {
            trace?.Write(scenes, tick);
        }

        if (!Halted)
        {
            capture?.Write(scenes, tick, _scheduler.Output);
        }

        if (Halted)
        {
            _scheduler.EndFrame();
        }
    }

    // Frames as a window presenting once per fixed step runs them. Each frame advances by the wall
    // time since the last one began, and the scheduler decides how much of it to catch up. The view
    // column times the frame's build. Nothing is submitted, and the draw section is empty. Uncapped, each
    // frame runs one step back to back with the last, as a run without diagnostics does, and no frame
    // waits for a period.
    private void PaceFrames(FrameDiagnostics diagnostics)
    {
        Func<long> timestamp = builder.Timestamp;
        long period = builder.Uncapped ? 0 : (long)(builder.StepSeconds * Stopwatch.Frequency);
        long previous = timestamp();
        while (true)
        {
            long start = timestamp();
            diagnostics.BeginUpdate();
            bool exiting;
            if (builder.Uncapped)
            {
                exiting = _scheduler.Advance(builder.StepSeconds, DeviceSnapshot.Empty, scenes);
            }
            else
            {
                _scheduler.TimeScale = scenes.Run.TimeScale;
                exiting = _scheduler.Advance((start - previous) / (double)Stopwatch.Frequency, DeviceSnapshot.Empty, scenes);
            }

            previous = start;
            diagnostics.EndUpdate(_scheduler.StepsThisFrame, _scheduler.Tick);

            diagnostics.BeginView();
            _ = scenes.View;
            diagnostics.BeginDraw();
            if (diagnostics.EndDraw() || exiting || Halted)
            {
                return;
            }

            EndFrame();

            // The present's wait for the next refresh. A frame that overran it starts at once.
            while (timestamp() - start < period)
            {
                Thread.Yield();
            }
        }
    }

    // Nothing is drawn here. A capture request the game made is taken and dropped instead of waiting for a
    // frame that never comes.
    private void EndFrame()
    {
        scenes.TryTakeFrameCapture(out _);
        scenes.FlushSaves();
    }
}
