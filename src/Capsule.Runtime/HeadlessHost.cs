using System.Diagnostics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Runtime.DevTools.Recordings;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime;

// The host with no window, device or media, which plays a driver or a recording through the scenes a
// windowed run would host. Transitions, the seed and the run settings behave as they do in a window. It
// steps as fast as its input allows, or in real-time frames when frame diagnostics are on. It records
// nothing. It takes exactly one of driver and replay, and watch runs after each step with the scene and
// the tick the step ran at.
internal sealed class HeadlessHost(
    EngineBuilder builder,
    SceneHost scenes,
    IInputDriver? driver,
    Replay? replay,
    FrameDiagnostics? diagnostics,
    Action<Scene, long>? watch = null)
{
    private readonly FixedStepScheduler _scheduler =
        Watched(new(builder.StepSeconds, builder.MaxStepsPerFrame, builder.Input.Bindings, driver, scenes, builder.Timestamp, replay: replay), scenes, watch);

    internal long SimulationTick => _scheduler.Tick;

    internal HeadlessRunResult Run()
    {
        if (diagnostics is null)
        {
            // One step's worth of time per call. The accumulator drains a single step and
            // neither the step bound nor the step budget can bind.
            while (!_scheduler.Advance(builder.StepSeconds, DeviceSnapshot.Empty, scenes))
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

    private static FixedStepScheduler Watched(FixedStepScheduler scheduler, SceneHost scenes, Action<Scene, long>? watch)
    {
        if (watch is not null)
        {
            scheduler.StepCompleted = () => watch(scenes.Scene, scheduler.Tick);
        }

        return scheduler;
    }

    // Frames as a window presenting once per fixed step runs them. Each frame advances by the wall
    // time since the last one began, and the scheduler decides how much of it to catch up. The view
    // column times the frame's build. Nothing is submitted, and the draw section is empty.
    private void PaceFrames(FrameDiagnostics diagnostics)
    {
        Func<long> timestamp = builder.Timestamp;
        long period = (long)(builder.StepSeconds * Stopwatch.Frequency);
        long previous = timestamp();
        while (true)
        {
            long start = timestamp();
            diagnostics.BeginUpdate();
            _scheduler.TimeScale = scenes.Run.TimeScale;
            bool exiting = _scheduler.Advance((start - previous) / (double)Stopwatch.Frequency, DeviceSnapshot.Empty, scenes);
            previous = start;
            diagnostics.EndUpdate(_scheduler.StepsThisFrame);

            diagnostics.BeginView();
            _ = scenes.View;
            diagnostics.BeginDraw();
            if (diagnostics.EndDraw() || exiting)
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

    // Nothing is drawn here. A capture request is taken and dropped instead of waiting for a frame
    // that never comes.
    private void EndFrame()
    {
        scenes.TryTakeFrameCapture(out _);
        scenes.FlushSaves();
    }
}
