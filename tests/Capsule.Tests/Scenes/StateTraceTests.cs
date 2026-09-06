using System.Globalization;
using System.Numerics;
using Capsule.Animation;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Animation;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Rendering;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Scenes;

public sealed class StateTraceTests
{
    private static readonly TextureHandle Sheet = new("player", ".png");

    private static readonly SpriteClip Walk = new(
        "walk",
        [Frame(0), Frame(1)],
        [1, 1],
        loop: true);

    [Fact]
    public void ATracedStep_RecordsEveryEntityTheAnimatorsColumnsAndTheCamera()
    {
        StateTrace trace = new();
        SceneFixtures.HookScene scene = new(start: world =>
        {
            world.Add(new Animated(new Vector2(4f, 5f)));
            world.Add(new SceneFixtures.Drifter(new Vector2(1f, 2f)));
            SceneFixtures.Open(world, new Vector2(10f, 20f), SceneFixtures.Viewport);
        });

        using SceneSimulation simulation = new(scene, trace: trace);
        simulation.Step(SceneFixtures.Step());

        List<Row> rows = Rows(trace);

        Assert.Equal("4", Value(rows, 0, "e0", "x"));
        Assert.Equal("5", Value(rows, 0, "e0", "y"));
        Assert.Equal(nameof(Animated), Value(rows, 0, "e0", "type"));
        Assert.Equal("walk", Value(rows, 0, "e0", "clip"));
        Assert.Equal("0", Value(rows, 0, "e0", "frame"));

        // The drifter moved one unit this step, which is what the row holds.
        Assert.Equal("2", Value(rows, 0, "e1", "x"));
        Assert.Equal("2", Value(rows, 0, "e1", "y"));
        Assert.Equal(nameof(SceneFixtures.Drifter), Value(rows, 0, "e1", "type"));

        Assert.Equal("10", Value(rows, 0, StateTrace.CameraSubject, "x"));
        Assert.Equal("20", Value(rows, 0, StateTrace.CameraSubject, "y"));
    }

    [Fact]
    public void ASubject_HoldsItsNameAcrossStepsAndAfterAPeerLeaves()
    {
        StateTrace trace = new();
        SceneFixtures.Drifter first = new(Vector2.Zero);
        SceneFixtures.HookScene scene = new(start: world =>
        {
            world.Add(first);
            world.Add(new SceneFixtures.Drifter(Vector2.Zero));
        });

        using SceneSimulation simulation = new(scene, trace: trace);
        simulation.Step(SceneFixtures.Step());
        scene.Remove(first);
        simulation.Step(SceneFixtures.Step(1));

        List<Row> rows = Rows(trace);

        // The survivor keeps the name it was given on the first step; the departed one is simply
        // absent from the second, neither renamed nor replaced by its peer.
        Assert.Equal(["e0", "e1", StateTrace.CameraSubject], Subjects(rows, 0));
        Assert.Equal(["e1", StateTrace.CameraSubject], Subjects(rows, 1));
        Assert.Equal("2", Value(rows, 1, "e1", "x"));
    }

    [Fact]
    public void AComposedEntity_IsNamedByItsDocumentPlacementId()
    {
        StateTrace trace = new();
        EntityRegistry entities = SceneFixtures.Registry(("drifter", static _ => new SceneFixtures.Drifter()));
        Scene scene = new(SceneFixtures.Content(
            SceneFixtures.RoomWithoutTerrain(new EntityPlacement(7, "drifter", 3f, 4f)),
            entities));

        using SceneSimulation simulation = new(scene, trace: trace);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(nameof(SceneFixtures.Drifter), Value(Rows(trace), 0, "#7", "type"));
    }

    [Fact]
    public void Write_QuotesAFieldHoldingASeparator()
    {
        StateTrace trace = new();
        SceneFixtures.HookScene scene = new(start: world => world.Add(new Quoting()));

        using SceneSimulation simulation = new(scene, trace: trace);
        simulation.Step(SceneFixtures.Step());

        Assert.Contains("0,e0,note,\"a, \"\"b\"\"\"\n", trace.ToString(), StringComparison.Ordinal);
    }

    private static Sprite Frame(int index) => new(Sheet, new TextureRegion(index * 8, 0, 8, 8));

    private static List<Row> Rows(StateTrace trace)
    {
        string[] lines = trace.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("tick,subject,column,value", lines[0]);

        List<Row> rows = new(lines.Length - 1);
        for (int index = 1; index < lines.Length; index++)
        {
            string[] fields = lines[index].Split(',', 4);
            rows.Add(new Row(
                long.Parse(fields[0], CultureInfo.InvariantCulture),
                fields[1],
                fields[2],
                fields[3]));
        }

        return rows;
    }

    private static string Value(List<Row> rows, long tick, string subject, string column) =>
        Assert.Single(rows, row => row.Tick == tick && row.Subject == subject && row.Column == column).Value;

    private static List<string> Subjects(List<Row> rows, long tick) =>
        [.. rows.Where(row => row.Tick == tick).Select(static row => row.Subject).Distinct()];

    private readonly record struct Row(long Tick, string Subject, string Column, string Value);

    private sealed class Animated : Entity
    {
        private readonly SpriteAnimator _animator;

        internal Animated(Vector2 position)
            : base(position)
        {
            SpriteRenderer renderer = new(Frame(0));
            _animator = new SpriteAnimator(renderer);
            Add(renderer);
            Add(_animator);
        }

        protected internal override void OnStart() => _animator.Play(Walk);
    }

    // Writes a value the CSV must quote rather than let split a row.
    private sealed class Quoting() : Entity(Vector2.Zero), ITraceSource
    {
        public void WriteTrace(TraceWriter writer) => writer.Write("note", "a, \"b\"");
    }
}
