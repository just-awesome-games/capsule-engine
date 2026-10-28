using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Persistence;
using Capsule.Runtime;
using Capsule.Runtime.DevTools.Recordings;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Tests.DevTools;

// A run recorded through the scheduler as a windowed run records it, over a scene whose state reads the
// input, the output extent, the refresh rate and the random source, and the builder that replays it.
internal static class RecordingFixtures
{
    internal const int Hertz = 10;

    internal static readonly Vector2 FirstOutput = new(640f, 360f);

    // A builder whose one scene class is Reading, at Hertz steps a second. scene builds the scene each run
    // boots, a plain Reading unless given.
    internal static EngineBuilder Builder(Func<Reading>? scene = null) =>
        CapsuleEngine.Configure(
                "Recording Game",
                new SceneRegistry(new EntityRegistry([]), [SceneRegistration.Plain(typeof(Reading), _ => scene?.Invoke() ?? new Reading())]))
            .WithFixedStep(Hertz)
            .WithoutCrashLog()
            .WithoutLogging();

    // The pointer wanders, focus is lost for a few steps, a key and the wheel are used, and the output
    // extent and the refresh rate change once each.
    internal static DeviceSnapshot Varied(RecordedRun run, int step)
    {
        if (step == 35)
        {
            run.Scheduler.Output = new Vector2(800f, 600f);
            run.Host.Run.Display.RefreshRate = 144;
        }

        DeviceSnapshot snapshot = DeviceSnapshot.Empty.WithPointer(new Vector2(step * 3 % 200, step % 50));

        return step switch
        {
            >= 20 and < 25 => snapshot.WithWindowFocus(false),
            30 => snapshot.With(Key.Space),
            40 => snapshot.WithScroll(new Vector2(0f, 1f)),
            _ => snapshot,
        };
    }

    // A host and recorder made as a windowed run makes them, restoring one save, and a scheduler stepping
    // it one step a call. A run given a recording replays it from that recording's boot state, holding at
    // its end as a windowed replay does.
    internal sealed class RecordedRun : IDisposable
    {
        internal SceneHost Host { get; }

        internal Recorder Recorder { get; }

        internal FixedStepScheduler Scheduler { get; }

        internal RecordedRun(EngineBuilder builder, Recording? replaying = null)
        {
            RecordedSaveStorage medium = new(replaying?.Header.Saves ?? [Save]);
            ulong seed = replaying?.Header.Seed ?? 7;
            SceneTransition opening = SceneTransition.ToScene(typeof(Reading), null);
            Recorder = builder.NewRecorder(in opening, seed, languages: null, language: null)!;
            Host = builder.CreateHost(in opening, medium, seed, preferredLanguages: null, language: null, Recorder);
            Replay? replay = replaying is null ? null : new Replay(replaying, Host, builder.Scenes, Recorder, holdsAtEnd: true);
            Scheduler = new FixedStepScheduler(builder.StepSeconds, 8, builder.Input.Bindings, scenes: Host, recorder: Recorder, replay: replay) { Output = FirstOutput };
        }

        private static RecordedSave Save => new("settings", "{\"volume\":3}", new SaveMetadata(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        internal void Step(in DeviceSnapshot snapshot) => Scheduler.Advance(1.0 / Hertz, snapshot, Host);

        // The run so far as a file would hold it.
        internal Recording Saved()
        {
            using MemoryStream file = new();
            Recorder.Write(file, crashed: false);

            return Recording.Read(file.ToArray(), "test run");
        }

        public void Dispose() => Host.Dispose();
    }

    // Moves its walker by what the step reads. ExtraDrawAt draws once more from the random source at that
    // tick, which a replay against it diverges on, ExitAt requests exit at that tick, and RestartAt a restart. ThrowsOnStart
    // fails the scene's start.
    internal sealed class Reading : Scene
    {
        internal Walker Walker { get; } = new() { Name = "Walker" };

        internal long? ExtraDrawAt { get; init; }

        internal long? ExitAt { get; init; }

        internal long? RestartAt { get; init; }

        internal bool ThrowsOnStart { get; init; }

        internal Reading()
        {
            Camera.ViewportSize = new Vector2(320f, 180f);
            Add(Walker);
        }

        protected override void OnStart()
        {
            if (ThrowsOnStart)
            {
                throw new InvalidOperationException("Reading was told to fail its start.");
            }
        }

        protected override void OnStep(in StepContext context)
        {
            if (context.Tick == ExitAt)
            {
                Run.RequestExit();
            }

            if (context.Tick == RestartAt)
            {
                Run.RequestRestart();
            }

            InputState input = context.Input;
            if (input.PointerMoved)
            {
                Walker.Position += input.PointerDelta * 0.25f;
            }

            if (!input.HasWindowFocus || input.WasAnyPressed(out _) || context.Tick == ExtraDrawAt)
            {
                Walker.Position += new Vector2(Run.Random.Range(0, 4), 0f);
            }

            Walker.Position += new Vector2(context.Output.X * 0.001f, (float)Run.Display.RefreshRate * 0.001f) + input.Scroll;
        }
    }

    internal sealed class Walker : Entity
    {
        protected internal override void OnDebugPanel(DebugPanel panel) =>
            panel.Command("Nudge", () => Position += new Vector2(0f, 5f));
    }
}
