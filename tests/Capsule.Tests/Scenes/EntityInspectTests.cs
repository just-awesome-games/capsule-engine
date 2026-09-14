using System.Numerics;
using Capsule.Animation;
using Capsule.Diagnostics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

public sealed class EntityInspectTests
{
    private static readonly SpriteClip Walk = new(
        [SceneFixtures.Frame(8, 8), SceneFixtures.Frame(8, 16), SceneFixtures.Frame(8, 24)],
        [2, 2, 2],
        loop: true);

    [Fact]
    public void TheWalk_WritesPositionAndZIndexBeforeTheHookThenEachComponentUnderItsHeadingInAttachmentOrder()
    {
        Scene scene = new();
        Reporter entity = new(new Vector2(3f, 4f)) { ZIndex = 7 };
        entity.Add(new ReportingComponent("Second"));
        entity.Add(new SilentComponent());
        entity.Add(new ReportingComponent("Fourth"));
        scene.Add(entity);
        using SimulationHost host = new(scene);
        Inspector inspector = new();

        entity.RunInspect(inspector);

        Assert.Equal(
            [
                ("Position", "(3, 4)"),
                ("ZIndex", "7"),
                ("Health", "12"),
                ("[ReportingComponent]", null),
                ("Name", "Second"),
                ("[SilentComponent]", null),
                ("[ReportingComponent]", null),
                ("Name", "Fourth"),
            ],
            Rows(inspector));
    }

    // Time has not begun for an entity in a scene that has not started, so neither hook runs; the
    // innate rows and the headings are the engine's and are written regardless.
    [Fact]
    public void AnUnstartedEntity_ReportsOnlyTheInnateRowsAndItsComponentHeadings()
    {
        Scene scene = new();
        Reporter entity = new(Vector2.Zero);
        entity.Add(new ReportingComponent("Never"));
        scene.Add(entity);
        Inspector inspector = new();

        entity.RunInspect(inspector);

        Assert.Equal(
            [
                ("Position", "(0, 0)"),
                ("ZIndex", "0"),
                ("[ReportingComponent]", null),
            ],
            Rows(inspector));
    }

    // The entity has no override of its own, and the innate rows are still there ahead of the
    // collider's.
    [Fact]
    public void ABoxCollider_ReportsItsSharedFieldsThenItsSize()
    {
        Scene scene = new();
        Entity entity = new SceneFixtures.Drifter(new Vector2(9f, 9f));
        entity.Add(new BoxCollider2D(new Vector2(8f, 16f)) { Offset = new Vector2(1f, 2f), Layer = "solid", Enabled = false });
        scene.Add(entity);
        using SimulationHost host = new(scene);
        Inspector inspector = new();

        entity.RunInspect(inspector);

        Assert.Equal(
            [
                ("Position", "(9, 9)"),
                ("ZIndex", "0"),
                ("[BoxCollider2D]", null),
                ("Enabled", "False"),
                ("Offset", "(1, 2)"),
                ("Layer", "solid"),
                ("Size", "(8, 16)"),
            ],
            Rows(inspector));
    }

    [Fact]
    public void AKinematicBodyOnAFloor_ReportsTheFloorAndItsNormal()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        SceneFixtures.Body body = new(new Vector2(24f, 8f), blocksOn: "solid");
        scene.Add(body);
        using SimulationHost host = new(scene);
        body.Mover.Move(new Vector2(0f, 60f));
        Inspector inspector = new();

        body.RunInspect(inspector);

        (string Label, string? Value)[] rows = Rows(inspector);
        int heading = Array.FindIndex(rows, static row => row.Label == "[KinematicBody2D]");
        Assert.True(heading >= 0);
        Assert.Equal(
            [
                ("IsOnFloor", "True"),
                ("IsOnWall", "False"),
                ("IsOnCeiling", "False"),
                ("FloorNormal", "(0, -1)"),
                ("WallNormal", "(0, 0)"),
            ],
            rows[(heading + 1)..]);
    }

    [Fact]
    public void ASpriteAnimatorMidClip_ReportsWhereItIs()
    {
        Scene scene = new();
        Entity entity = new SceneFixtures.Drifter(Vector2.Zero);
        SpriteAnimator animator = new(new SpriteRenderer(SceneFixtures.Frame(8, 8)));
        entity.Add(animator);
        scene.Add(entity);
        using SimulationHost host = new(scene);
        animator.Play(Walk);
        host.Step(3);
        Inspector inspector = new();

        entity.RunInspect(inspector);

        (string Label, string? Value)[] rows = Rows(inspector);
        int heading = Array.FindIndex(rows, static row => row.Label == "[SpriteAnimator]");
        Assert.True(heading >= 0);
        Assert.Equal(1, animator.FrameIndex);
        Assert.Equal(
            [
                ("Playing", "True"),
                ("Frame", "1 of 3"),
                ("Tick", animator.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("Loop", "True"),
                ("IsFinished", "False"),
            ],
            rows[(heading + 1)..]);
    }

    private static (string Label, string? Value)[] Rows(Inspector inspector)
    {
        ReadOnlySpan<InspectorRow> rows = inspector.Rows;
        (string, string?)[] pairs = new (string, string?)[rows.Length];
        for (int index = 0; index < rows.Length; index++)
        {
            InspectorRow row = rows[index];
            pairs[index] = row.IsHeading ? ($"[{row.Label}]", null) : (row.Label, row.Value);
        }

        return pairs;
    }

    private sealed class Reporter(Vector2 position) : Entity(position)
    {
        protected internal override void OnInspect(Inspector inspector) => inspector.Field("Health", 12);
    }

    private sealed class ReportingComponent(string name) : Component
    {
        protected internal override void OnInspect(Inspector inspector) => inspector.Field("Name", name);
    }

    private sealed class SilentComponent : Component;
}
