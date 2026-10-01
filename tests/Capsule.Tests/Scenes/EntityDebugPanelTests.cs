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
                .. EntitySection("(3, 4) r 0 s (1, 1)", "7"),
                ("Health", "12"),
                ("[ReportingComponent]", null),
                ("Name", "Second"),
                ("[SilentComponent]", null),
                ("[ReportingComponent]", null),
                ("Name", "Fourth"),
            ],
            Rows(panel));
    }

    // Neither hook runs before the scene starts. The innate rows and the headings are the engine's.
    [Fact]
    public void AnUnstartedEntity_ReportsOnlyTheInnateRowsAndItsComponentHeadings()
    {
        Scene scene = new();
        Reporter entity = new(Vector2.Zero);
        entity.Add(new ReportingComponent("Never"));
        scene.Add(entity);
        DebugPanel panel = new();

        entity.RunDebugPanel(panel);

        Assert.Equal([.. EntitySection("(0, 0) r 0 s (1, 1)"), ("[ReportingComponent]", null)], Rows(panel));
    }

    // The collider's Enabled is a toggle that round-trips through the collider.
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
                .. EntitySection("(9, 9) r 0 s (1, 1)"),
                ("[BoxCollider2D]", null),
                ("Offset", "(1, 2)"),
                ("Layer", "solid"),
                ("Touching", "0"),
                ("Enabled", null),
                ("ReportsContacts", null),
                ("Size", "(8, 16)"),
            ],
            Rows(panel));

        DebugPanelRow enabled = panel.Rows[13];
        Assert.Equal(DebugPanelRowKind.Toggle, enabled.Kind);
        Assert.False(enabled.On);

        enabled.Activate!();
        Assert.True(collider.Enabled);

        panel.Clear();
        entity.RunDebugPanel(panel);
        Assert.True(panel.Rows[13].On);

        panel.Rows[13].Activate!();
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
                ("Mode", "Floating"),
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
                ("Paused", "False"),
                ("Restart", null),
            ],
            rows[(heading + 1)..]);
    }

    // The rows every entity's section opens with.
    private static (string Label, string? Value)[] EntitySection(string transform, string zIndex = "0") =>
    [
        ("[Entity]", null),
        ("Transform", transform),
        ("ZIndex", zIndex),
        ("ScrollFactor", "(1, 1)"),
        ("Visible", null),
        ("Tint", "#ffffffff"),
        ("Flash", "0"),
        ("StepMode", "Inherit"),
        ("Remove", null),
    ];

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
