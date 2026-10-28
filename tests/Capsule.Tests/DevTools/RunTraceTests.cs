using System.Globalization;
using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.DevTools.Recordings;
using Capsule.Scenes;
using static Capsule.Tests.DevTools.RecordingFixtures;

namespace Capsule.Tests.DevTools;

// --trace writes the named entities' panel rows at the named ticks of a headless run, a tick being the
// overlay's: the state after that many steps. Each stretch of consecutive ticks starts in full and then
// writes only what changed.
[Collection(LogSinkCollection.Name)]
public sealed class RunTraceTests : IDisposable
{
    private const int Steps = 10;

    private readonly TempWorkspace _workspace = new("trace");

    // The walker's position after each step, by the overlay's tick. Index 0 is the opening.
    private readonly Vector2[] _positions = new Vector2[Steps + 1];

    public void Dispose() => _workspace.Dispose();

    // The walker carries a second, unnamed walker, which the scene names Walker (1), and which holds two of
    // one component.
    private static Reading Carrying()
    {
        Reading scene = new();
        Walker carried = new();
        carried.Add(new Marker(1));
        carried.Add(new Marker(2));
        scene.Add(carried);
        carried.Parent = scene.Walker;

        return scene;
    }

    // Tick 3 does not follow tick 0 and starts a stretch of its own. The walker moves every step, and the
    // walker it carries moves with it. The walkers' Remove and Nudge commands hold no value.
    [Fact]
    public void EachStretch_NamesItsScene_StartsInFull_ThenWritesOnlyWhatChanged()
    {
        string[] lines = Trace("0,3..4", "Walker");

        Assert.Equal("tick,entity,section,field,value", lines[0]);
        Assert.Equal([0L, 3L, 4L], lines.Skip(1).Select(Tick).Distinct());
        Assert.Equal(["0,,,Scene,Reading", "3,,,Scene,Reading"], lines.Where(static line => line.Contains(",,,Scene,", StringComparison.Ordinal)));
        Assert.Contains(Row(0, "Walker", "Entity", "ZIndex", "0"), lines);
        Assert.Contains(Row(3, "Walker", "Entity", "ZIndex", "0"), lines);
        Assert.Contains(Row(3, "Walker (1)", "Entity", "Visible", "True"), lines);
        Assert.Contains(Row(3, "Walker (1)", "Marker", "Mark", "1"), lines);
        Assert.Contains(Row(3, "Walker (1)", "Marker", "Mark (1)", "11"), lines);
        Assert.Contains(Row(3, "Walker (1)", "Marker (1)", "Mark", "2"), lines);
        Assert.Contains(Row(4, "Walker", "Entity", "Transform", Transform(_positions[4])), lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("4,", StringComparison.Ordinal) && !line.Contains("Transform,", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains(",Remove,", StringComparison.Ordinal) || line.Contains(",Nudge,", StringComparison.Ordinal));
    }

    // Expiring stops writing its field after its second step and removes its walker in the step of tick 4.
    [Fact]
    public void AFieldOrAnEntityGone_WritesAnEmptyRow()
    {
        string[] lines = Trace("0..6", "*", () =>
        {
            Reading scene = new();
            Walker expiring = new();
            expiring.Add(new Expiring());
            scene.Add(expiring);

            return scene;
        });

        Assert.Contains(Row(0, "Walker (1)", "Expiring", "Early", "True"), lines);
        Assert.Contains(Row(2, "Walker (1)", "Expiring", "Early", ""), lines);
        Assert.Contains("5,Walker (1),,,", lines);
        Assert.DoesNotContain(lines.Skip(1), line => Tick(line) == 6 && line.Contains("Walker (1)", StringComparison.Ordinal));
    }

    // A numbered name selects its entity alone, where the type name selects every walker.
    [Fact]
    public void AName_SelectsOneEntityOfItsType()
    {
        string[] lines = Trace("1", "Walker (1)", () =>
        {
            Reading scene = new();
            scene.Add(new Walker { Name = "Walker" });

            return scene;
        });

        string[] rows = [.. lines.Skip(1).Where(static line => !line.Contains(",,,Scene,", StringComparison.Ordinal))];
        Assert.All(rows, line => Assert.Contains(",Walker (1),", line, StringComparison.Ordinal));
        Assert.NotEmpty(rows);
    }

    // A driver plays the run, and the trace writes it from tick 0 to its end as it writes a replay. An open
    // range warns of no end.
    [Fact]
    public void ADrivenRun_IsTraced()
    {
        string csv = _workspace.PathTo("driven.csv");

        CollectingLogSink sink = new();

        int exitCode = Builder(Carrying)
            .WithLogSink(sink)
            .WithInputDriver(new Pointing(Steps))
            .WithCommandLine(["--headless", "--trace", csv, "0..", "Walker"])
            .RunScene<Reading>();

        string[] lines = File.ReadAllLines(csv);
        Assert.Equal(0, exitCode);
        Assert.Equal(0L, lines.Skip(1).Select(Tick).Min());
        Assert.Equal(Steps, lines.Skip(1).Select(Tick).Max());
        Assert.Contains(Row(0, "Walker", "Entity", "ZIndex", "0"), lines);
        Assert.DoesNotContain(sink.Entries, static entry => entry.Level == LogLevel.Warning);
    }

    // A closed selection past the ceiling is counted before the run, which never starts.
    [Fact]
    public void ASelectionPastTheCeiling_IsRefusedBeforeTheRun()
    {
        string csv = _workspace.PathTo("refused.csv");

        CommandLineException refused = Assert.Throws<CommandLineException>(() => Builder(Carrying)
            .WithInputDriver(new Pointing(Steps))
            .WithCommandLine(["--headless", "--trace", csv, "0..3600", "Walker"]));

        Assert.StartsWith("--trace selects 3601 ticks, past its ceiling of 3600.", refused.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(csv));
    }

    // A driver's run has no known end, and its open range halts the run at the first tick past the ceiling,
    // keeping every row before it. --force lets the same run play to its end.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnOpenRangeOverADriver_HaltsAtTheCeiling_UnlessForced(bool force)
    {
        const int steps = RunTrace.Ceiling + 100;
        string csv = _workspace.PathTo("halted.csv");
        EngineBuilder builder = Builder(Carrying)
            .WithInputDriver(new Pointing(steps))
            .WithCommandLine(force ? ["--headless", "--trace", csv, "0..", "Walker", "--force"] : ["--headless", "--trace", csv, "0..", "Walker"]);

        if (force)
        {
            Assert.Equal(0, builder.RunScene<Reading>());
            Assert.Equal(steps, File.ReadAllLines(csv).Skip(1).Select(Tick).Max());
        }
        else
        {
            CommandLineException halted = Assert.Throws<CommandLineException>(() => builder.RunScene<Reading>());
            Assert.StartsWith(string.Create(CultureInfo.InvariantCulture, $"--trace stopped the run at tick {RunTrace.Ceiling}, past its ceiling"), halted.Message, StringComparison.Ordinal);
            Assert.Equal(RunTrace.Ceiling - 1, File.ReadAllLines(csv).Skip(1).Select(Tick).Max());
        }
    }

    // Scene and Camera trace the scene's own panel and its camera's beside the entities.
    [Fact]
    public void SceneAndCamera_TraceTheirPanels()
    {
        string[] lines = Trace("1", "Scene,Camera");

        Assert.Contains(lines, static line => line.StartsWith("1,Scene,Scene,Paused,", StringComparison.Ordinal));
        Assert.Contains(Row(1, "Camera", "Camera", "ViewportSize", "\"(320, 180)\""), lines);
        Assert.DoesNotContain(lines, static line => line.Contains("Walker", StringComparison.Ordinal));
    }

    // The restart the step of tick 3 requests is a new scene by tick 4, which starts a stretch of its own: it names
    // the scene again and writes the walker in full, though its walker shares the name.
    [Fact]
    public void ASceneChange_StartsAStretch()
    {
        string[] lines = Trace("0..6", "Walker", () => new Reading { RestartAt = 3 });

        string[] scenes = [.. lines.Where(static line => line.Contains(",,,Scene,", StringComparison.Ordinal))];
        Assert.Equal(2, scenes.Length);
        long changed = Tick(scenes[1]);
        Assert.Contains(Row(changed, "Walker", "Entity", "ZIndex", "0"), lines);
        Assert.DoesNotContain(lines, static line => line.EndsWith(",,,", StringComparison.Ordinal));
    }

    // A panel command and a G move taken through the overlay each write a row under their subject at the tick their
    // result shows, where the walker's own row shows it moved. A move's point holds a comma, and its cell is quoted.
    [Fact]
    public void AReplayedOverlayAct_WritesARowAtTheTickItsResultShows()
    {
        string recording = _workspace.PathTo("acts.recording");
        using (RecordedRun run = new(Builder()))
        using (OverlayRig rig = new(run.Host, scheduler: run.Scheduler))
        {
            rig.Scheduler.Output = OverlayFixtures.Output;
            rig.Frame();
            rig.OpenEntityPanel("Walker");
            rig.SelectRow("Nudge");
            rig.PlaceWorld();
            rig.Frame(DeviceSnapshot.Of(Key.G).WithPointer(new Vector2(700f, 300f)));
            rig.Frame(DeviceSnapshot.Empty.WithPointer(new Vector2(700f, 300f)));
            rig.Press(Key.Grave);
            rig.Frame();

            using FileStream file = File.Create(recording);
            run.Recorder.Write(file, crashed: false);
        }

        string[] lines = TraceFile(recording, "0..", "Walker", static () => new Reading());

        foreach (string act in (ReadOnlySpan<string>)["Command Nudge", "Move to ("])
        {
            string row = Assert.Single(lines, line => line.Contains(",Walker,,Act,", StringComparison.Ordinal) && line.Contains(act, StringComparison.Ordinal));
            Assert.Contains(lines, line => line.StartsWith($"{Tick(row)},Walker,Entity,Transform,", StringComparison.Ordinal));
        }
    }

    // The overlay's Exit ends the run in its step, which leaves no scene to write. Its act still writes its row.
    [Fact]
    public void AnExitThroughTheOverlay_WritesItsActRow()
    {
        string recording = _workspace.PathTo("exit.recording");
        using (RecordedRun run = new(Builder()))
        using (OverlayRig rig = new(run.Host, scheduler: run.Scheduler))
        {
            rig.Frame();
            rig.Frame();
            rig.Open();
            rig.Press(Key.E);

            using FileStream file = File.Create(recording);
            run.Recorder.Write(file, crashed: false);
        }

        string[] lines = TraceFile(recording, "0..", "Walker", static () => new Reading());

        Assert.Single(lines, static line => line.EndsWith(",,,Act,Exit", StringComparison.Ordinal));
    }

    // The carried walker, Walker (2), has the second walker, Walker (1), as its Parent.
    [Fact]
    public void AFieldNamingAnEntity_HoldsItsName()
    {
        string[] lines = Trace("1", "*", () =>
        {
            Reading scene = new();
            Walker second = new() { Name = "Walker" };
            Walker carried = new();
            scene.Add(second);
            scene.Add(carried);
            carried.Parent = second;

            return scene;
        });

        Assert.Contains(Row(1, "Walker (2)", "Entity", "Parent", "Walker (1)"), lines);
    }

    // The last step exits and tears its scene down. The run ends on the tick before it, and the exit's
    // tick is past the end.
    [Fact]
    public void ANameThatMatchesNothing_AndATickPastTheEnd_AreWarnedOf()
    {
        CollectingLogSink sink = new();

        string[] lines = Trace($"5,{Steps}", "Walker,Wlaker", () => new Reading { ExitAt = Steps - 1 }, sink);

        Assert.Equal([5L], lines.Skip(1).Select(Tick).Distinct());
        Assert.Contains(sink.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("'Wlaker'", StringComparison.Ordinal));
        Assert.Contains(sink.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains(string.Create(CultureInfo.InvariantCulture, $"run ended at tick {Steps - 1}"), StringComparison.Ordinal));
        Assert.Contains(sink.Entries, entry => entry.Message.Contains(string.Create(CultureInfo.InvariantCulture, $"wrote {lines.Length - 1} rows"), StringComparison.Ordinal));
    }

    private static long Tick(string line) => long.Parse(line.AsSpan(0, line.IndexOf(',', StringComparison.Ordinal)), CultureInfo.InvariantCulture);

    private static string Row(long tick, string entity, string section, string field, string value) =>
        string.Create(CultureInfo.InvariantCulture, $"{tick},{entity},{section},{field},{value}");

    // A transform holds commas, and its cell is quoted.
    private static string Transform(Vector2 position) =>
        string.Create(CultureInfo.InvariantCulture, $"\"({position.X}, {position.Y}) r 0 s (1, 1)\"");

    // Records Steps steps of the scene, keeping the walker's positions, then replays the recording headless
    // under --trace and returns the trace's lines.
    private string[] Trace(string ticks, string names, Func<Reading>? scene = null, CollectingLogSink? sink = null)
    {
        scene ??= Carrying;
        string recording = _workspace.PathTo("run.recording");
        using (RecordedRun run = new(Builder(scene)))
        {
            _positions[0] = ((Reading)run.Host.Scene).Walker.Position;
            for (int step = 0; step < Steps; step++)
            {
                run.Step(Varied(run, step));
                _positions[step + 1] = ((Reading)run.Host.Scene).Walker.Position;
            }

            using FileStream file = File.Create(recording);
            run.Recorder.Write(file, crashed: false);
        }

        return TraceFile(recording, ticks, names, scene, sink);
    }

    // Replays the recording headless under --trace over a scene scene builds, and returns the trace's lines.
    private string[] TraceFile(string recording, string ticks, string names, Func<Reading>? scene = null, CollectingLogSink? sink = null)
    {
        string csv = _workspace.PathTo("trace.csv");
        EngineBuilder builder = Builder(scene);
        if (sink is not null)
        {
            builder.WithLogSink(sink);
        }

        int exitCode = builder
            .WithCommandLine(["--replay", recording, "--headless", "--trace", csv, ticks, names])
            .RunScene<Reading>();

        Assert.Equal(0, exitCode);

        return File.ReadAllLines(csv);
    }

    // Moves the pointer every step until steps have run.
    private sealed class Pointing(int steps) : IInputDriver
    {
        public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
        {
            snapshot = DeviceSnapshot.Empty.WithPointer(new Vector2(tick * 3, 0f));

            return tick < steps;
        }
    }

    // Writes its label twice in one section.
    private sealed class Marker(long mark) : Component
    {
        protected internal override void OnDebugPanel(DebugPanel panel)
        {
            panel.Field("Mark", mark);
            panel.Field("Mark", mark + 10);
        }
    }

    private sealed class Expiring : Component
    {
        private int _steps;

        protected internal override void OnStep(in StepContext context)
        {
            _steps++;
            if (context.Tick == 4)
            {
                Entity!.Scene.Remove(Entity);
            }
        }

        protected internal override void OnDebugPanel(DebugPanel panel)
        {
            if (_steps < 2)
            {
                panel.Field("Early", true);
            }
        }
    }
}
