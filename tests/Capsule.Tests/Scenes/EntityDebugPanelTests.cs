using System.Numerics;
using Capsule.Animation;
using Capsule.Diagnostics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

public sealed class EntityDebugPanelTests
{
    private static readonly SpriteClip Walk = new(
        [SceneFixtures.Frame(8, 8), SceneFixtures.Frame(8, 16), SceneFixtures.Frame(8, 24)],
        [2, 2, 2],
        loop: true);

    [Fact]
    public void TheWalk_OpensTheEntitySectionWithPositionAndZIndexThenTheHookThenEachComponentUnderItsHeading()
    {
        Scene scene = new();
        Reporter entity = new(new Vector2(3f, 4f)) { ZIndex = 7 };
        entity.Add(new ReportingComponent("Second"));
        entity.Add(new SilentComponent());
        entity.Add(new ReportingComponent("Fourth"));
        scene.Add(entity);
        using SimulationHost host = new(scene);
        DebugPanel panel = new();

        entity.RunDebugPanel(panel);

        Assert.Equal(
            [
                ("[Entity]", null),
                ("Position", "(3, 4)"),
                ("ZIndex", "7"),
                ("ScrollFactor", "(1, 1)"),
                ("Remove", null),
                ("Health", "12"),
                ("[ReportingComponent]", null),
                ("Name", "Second"),
                ("[SilentComponent]", null),
                ("[ReportingComponent]", null),
                ("Name", "Fourth"),
            ],
            Rows(panel));
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
        DebugPanel panel = new();

        entity.RunDebugPanel(panel);

        Assert.Equal(
            [
                ("[Entity]", null),
                ("Position", "(0, 0)"),
                ("ZIndex", "0"),
                ("ScrollFactor", "(1, 1)"),
                ("Remove", null),
                ("[ReportingComponent]", null),
            ],
            Rows(panel));
    }

    // The entity has no override of its own, and the innate rows are still there ahead of the
    // collider's; the collider's Enabled is a toggle that round-trips through the collider.
    [Fact]
    public void ABoxCollider_ReportsItsSharedFieldsThenItsSizeAndItsEnabledToggleRoundTrips()
    {
        Scene scene = new();
        Entity entity = new SceneFixtures.Drifter(new Vector2(9f, 9f));
        BoxCollider2D collider = new(new Vector2(8f, 16f)) { Offset = new Vector2(1f, 2f), Layer = "solid", Enabled = false };
        entity.Add(collider);
        scene.Add(entity);
        using SimulationHost host = new(scene);
        DebugPanel panel = new();

        entity.RunDebugPanel(panel);

        Assert.Equal(
            [
                ("[Entity]", null),
                ("Position", "(9, 9)"),
                ("ZIndex", "0"),
                ("ScrollFactor", "(1, 1)"),
                ("Remove", null),
                ("[BoxCollider2D]", null),
                ("Offset", "(1, 2)"),
                ("Layer", "solid"),
                ("Touching", "0"),
                ("Enabled", null),
                ("ReportsContacts", null),
                ("Size", "(8, 16)"),
            ],
            Rows(panel));

        DebugPanelRow enabled = panel.Rows[9];
        Assert.Equal(DebugPanelRowKind.Toggle, enabled.Kind);
        Assert.False(enabled.On);

        enabled.Activate!();
        Assert.True(collider.Enabled);

        panel.Clear();
        entity.RunDebugPanel(panel);
        Assert.True(panel.Rows[9].On);

        panel.Rows[9].Activate!();
        Assert.False(collider.Enabled);
    }

    [Fact]
    public void AKinematicBodyOnAFloor_ReportsTheFloorAndItsNormal()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        SceneFixtures.Body body = new(new Vector2(24f, 8f), blocksOn: "solid");
        scene.Add(body);
        using SimulationHost host = new(scene);
        body.Mover.Move(new Vector2(0f, 60f));
        DebugPanel panel = new();

        body.RunDebugPanel(panel);

        (string Label, string? Value)[] rows = Rows(panel);
        int heading = Array.FindIndex(rows, static row => row.Label == "[KinematicBody2D]");
        Assert.True(heading >= 0);
        Assert.Equal(
            [
                ("IsOnFloor", "True"),
                ("IsOnWall", "False"),
                ("IsOnCeiling", "False"),
                ("FloorNormal", "(0, -1)"),
                ("WallNormal", "(0, 0)"),
                ("MoveContacts", "1"),
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
        DebugPanel panel = new();

        entity.RunDebugPanel(panel);

        (string Label, string? Value)[] rows = Rows(panel);
        int heading = Array.FindIndex(rows, static row => row.Label == "[SpriteAnimator]");
        Assert.True(heading >= 0);
        Assert.Equal(1, animator.FrameIndex);
        Assert.Equal(
            [
                ("Playing", "True"),
                ("Clip", "3 frames, (0, 0) to (0, 0)"),
                ("Frame", "1 of 3"),
                ("Tick", animator.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("Loop", "True"),
                ("IsFinished", "False"),
                ("Restart", null),
            ],
            rows[(heading + 1)..]);
    }

    private static (string Label, string? Value)[] Rows(DebugPanel panel)
    {
        ReadOnlySpan<DebugPanelRow> rows = panel.Rows;
        (string, string?)[] pairs = new (string, string?)[rows.Length];
        for (int index = 0; index < rows.Length; index++)
        {
            DebugPanelRow row = rows[index];
            pairs[index] = row.IsHeading ? ($"[{row.Label}]", null) : (row.Label, row.Value);
        }

        return pairs;
    }

    private sealed class Reporter(Vector2 position) : Entity(position)
    {
        protected internal override void OnDebugPanel(DebugPanel panel) => panel.Field("Health", 12);
    }

    private sealed class ReportingComponent(string name) : Component
    {
        protected internal override void OnDebugPanel(DebugPanel panel) => panel.Field("Name", name);
    }

    private sealed class SilentComponent : Component;
}
