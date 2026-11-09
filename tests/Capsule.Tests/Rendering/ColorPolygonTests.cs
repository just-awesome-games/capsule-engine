using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.UI;

namespace Capsule.Tests.Rendering;

public sealed class ColorPolygonTests
{
    private static readonly Vector2[] Wedge = [Vector2.Zero, new(10f, 0f), new(0f, 5f)];

    [Fact]
    public void APolygonInsideTheCamera_LandsPlacedByItsEntityAtBothEndsOfTheStep_OneOutsideIsCulled()
    {
        SceneFixtures.Drifter inside = new(new Vector2(100f, 50f)) { Scale = new Vector2(2f, 1f) };
        inside.Add(new ColorPolygon(Wedge));
        SceneFixtures.Drifter outside = new(new Vector2(100_000f, 100_000f));
        outside.Add(new ColorPolygon(Wedge));
        SceneSimulation simulation = Simulated(inside, outside);

        FrameView view = simulation.View;

        StoredPolygon polygon = Assert.Single(view.Polygons.ToArray());
        Assert.Equal(
            [new(100f, 50f), new(120f, 50f), new(100f, 55f), new(101f, 50f), new(121f, 50f), new(101f, 55f)],
            view.PolygonPoints.Slice(polygon.FirstPoint, 2 * polygon.Count).ToArray());
        Assert.Equal(Rect.FromEdges(100f, 50f, 121f, 55f), polygon.Bounds);
        Assert.Equal(new RenderMetrics(Submitted: 2, Visible: 1), view.Metrics);
    }

    // The renderer draws a polygon before the sprite stored after it, under the runs both cursors have reached.
    [Fact]
    public void APolygon_IsPlacedAmongTheSprites_AndItsRenderersMaterialRunsAroundIt()
    {
        Material glow = new(new Shader("glow"));
        Entity below = Sprite(zIndex: 0);
        Entity polygon = new SceneFixtures.Holder(new Vector2(160f, 90f), new ColorPolygon(Wedge) { Material = glow }) { ZIndex = 1 };
        Entity above = Sprite(zIndex: 2);
        SceneSimulation simulation = Simulated(above, polygon, below);

        FrameView view = simulation.View;

        Assert.Equal(2, view.Sprites.Length);
        Assert.Equal(1, Assert.Single(view.Polygons.ToArray()).Before);
        Assert.Equal([new MaterialRun(1, glow, FirstPolygon: 0), new MaterialRun(1, null, FirstPolygon: 1)], view.MaterialRuns.ToArray());
    }

    [Fact]
    public void APolygon_TakesItsEntitysTintAndFlash_AsASpriteDoes()
    {
        Entity tinted = new SceneFixtures.Holder(new Vector2(160f, 90f), new ColorPolygon(Wedge) { Color = new ColorRgba(200, 200, 200, 255), Blend = BlendMode.Additive })
        {
            Tint = new ColorRgba(255, 128, 0, 255),
            Flash = 0.5f,
        };
        SceneSimulation simulation = Simulated(tinted);

        StoredPolygon polygon = Assert.Single(simulation.View.Polygons.ToArray());

        Assert.Equal(ColorRgba.Multiply(new ColorRgba(200, 200, 200, 255), new ColorRgba(255, 128, 0, 255)), polygon.Color);
        Assert.Equal(BlendMode.Additive, polygon.Blend);
        Assert.NotEqual(0, polygon.Flash.A);
    }

    [Fact]
    public void APolygonUnderAScreenEntity_DrawsOnTheScreenLayerInCanvasPixels()
    {
        ScreenEntity panel = new(Anchor.TopLeft, new Vector2(8f, 4f));
        panel.Add(new ColorPolygon(Wedge));
        SceneSimulation simulation = Simulated(panel);

        FrameView view = simulation.View;

        Assert.Empty(view.Polygons.ToArray());
        StoredPolygon polygon = Assert.Single(view.ScreenPolygons.ToArray());
        Assert.Equal([new(8f, 4f), new(18f, 4f), new(8f, 9f)], view.PolygonPoints.Slice(polygon.FirstPoint + polygon.Count, polygon.Count).ToArray());
    }

    // A beam opens from a line and closes to one: its tips meet, sit a hair apart, or sit along its edge.
    [Theory]
    [InlineData(0f, 0f, 0f, 40f, 0f, 40f)]
    [InlineData(0f, 0f, -100f, 200f, -99.99999f, 200f)]
    [InlineData(0f, 0f, 0f, 40f, 0f, 80f)]
    [InlineData(0f, 0f, -20f, 40f, -20f, 40f)]
    [InlineData(0f, 0f, 0f, 0f, 0f, 0f)]
    public void SetPoints_TakesAnOutlineWithNoArea(float ax, float ay, float bx, float by, float cx, float cy)
    {
        ColorPolygon polygon = new([new(ax, ay), new(bx, by), new(cx, cy)]);

        Assert.Equal([new(ax, ay), new(bx, by), new(cx, cy)], polygon.Points.ToArray());
    }

    [Theory]
    [InlineData("two points", "A polygon takes three points or more, not 2.")]
    [InlineData("a reflex corner", "The outline turns back at point 2, and a polygon is convex.")]
    [InlineData("a reflex corner behind a repeated point", "The outline turns back at point 3, and a polygon is convex.")]
    [InlineData("a reflex corner along a short edge", "The outline turns back at point 2, and a polygon is convex.")]
    [InlineData("a star", "The outline goes around more than once, and a polygon is convex.")]
    public void SetPoints_RefusesAnOutlineThatIsNotConvex_NamingTheFix(string outline, string message)
    {
        Vector2[] points = outline switch
        {
            "two points" => [Vector2.Zero, Vector2.One],
            "a reflex corner" => [Vector2.Zero, new(10f, 0f), new(5f, 2f), new(10f, 10f), new(0f, 10f)],
            "a reflex corner behind a repeated point" => [Vector2.Zero, new(10f, 0f), new(4f, 4f), new(4f, 4f), new(0f, 10f)],
            "a reflex corner along a short edge" => [Vector2.Zero, new(10f, 0f), new(9.997f, 0.003f), new(10f, 10f), new(0f, 10f)],
            _ => [new(0f, -10f), new(6f, 8f), new(-9.5f, -3f), new(9.5f, -3f), new(-6f, 8f)],
        };
        ColorPolygon polygon = new(Wedge);

        ArgumentException refused = Assert.Throws<ArgumentException>(() => polygon.SetPoints(points));

        Assert.StartsWith(message, refused.Message, StringComparison.Ordinal);
        Assert.Equal(Wedge, polygon.Points.ToArray());
    }

    [Fact]
    public void Bounds_IsTheOutlineWhereTheNextFramePlacesIt_AndEmptyWithoutOne()
    {
        ColorPolygon polygon = new();
        SceneFixtures.Drifter drifter = new(new Vector2(100f, 50f));
        drifter.Add(polygon);
        _ = Simulated(drifter);

        Assert.True(polygon.Bounds.IsEmpty);

        polygon.SetPoints(Wedge);

        Assert.Equal(Rect.FromEdges(101f, 50f, 111f, 55f), polygon.Bounds);
    }

    [Fact]
    public void APolygonIntent_RefusesAPreviousOutlineOfAnotherLength()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => new PolygonIntent(Wedge.AsSpan(0, 3), [.. Wedge, Vector2.One], ColorRgba.White));

        Assert.StartsWith("The previous outline holds 3 points and the current one 4.", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APolygonWithAPointNotFinite_DrawsNothing()
    {
        FrameView view = new();

        view.Add(new PolygonIntent([Vector2.Zero, new(float.NaN, 0f), Vector2.One], ColorRgba.White));

        Assert.Empty(view.Polygons.ToArray());
        Assert.Equal(new RenderMetrics(Submitted: 1, Visible: 0), view.Metrics);
    }

    private static Entity Sprite(int zIndex) =>
        new SceneFixtures.Holder(new Vector2(160f, 90f), new SpriteRenderer(SceneFixtures.Frame(8, 8))) { ZIndex = zIndex };

    // One step of a scene viewed one screen wide around the centre of the first screen.
    private static SceneSimulation Simulated(params Entity[] entities)
    {
        SceneFixtures.HookScene scene = new(SceneFixtures.Opens(new Vector2(160f, 90f)));
        foreach (Entity entity in entities)
        {
            scene.Add(entity);
        }

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        return simulation;
    }
}
