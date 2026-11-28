using System.Numerics;
using Capsule.Assets;
using Capsule.Particles;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.Tiles;
using Capsule.UI;

namespace Capsule.Tests.Rendering;

public sealed class LightingTests
{
    private static readonly ColorRgba Marked = new(255, 0, 0);

    // Unlit reaches every renderer beneath the entity, whatever it draws: a sprite, a polygon, a line, text,
    // particles and a tile map, which is an entity of its own. A sibling marked by its tint stays lit, and so does
    // a child set back to false beneath an unlit parent, which cannot light it again.
    [Fact]
    public void Unlit_ReachesEveryDrawKindBeneathIt_AndLeavesASiblingLit()
    {
        EntityHierarchyFixtures.Node unlit = new(new Vector2(20f, 20f)) { Unlit = true };
        Add(unlit);
        EntityHierarchyFixtures.Node relit = new(Vector2.Zero) { Unlit = false };
        relit.Add(new SpriteRenderer(SceneFixtures.Frame(4, 4)));
        relit.Parent = unlit;
        TileMap map = new(SceneFixtures.RoomGrid()) { Unlit = true };

        EntityHierarchyFixtures.Node lit = new(new Vector2(40f, 20f)) { Tint = Marked };
        Add(lit);

        SimulationHost run = SceneFixtures.Simulate(unlit, map, lit);
        unlit.Get<ParticleEmitter>().Emit(2);
        lit.Get<ParticleEmitter>().Emit(2);
        run.Step();
        FrameView view = run.Simulation.View;
        ReadOnlySpan<PlacementRun> runs = view.PlacementRuns;

        Assert.True(view.Sprites.Length > 4);
        for (int index = 0; index < view.Sprites.Length; index++)
        {
            Assert.Equal(view.Sprites[index].Color.G != 0, UnlitAt(runs, static run => run.FirstSprite, index));
        }

        Assert.Equal(2, view.Polygons.Length);
        for (int index = 0; index < view.Polygons.Length; index++)
        {
            Assert.Equal(view.Polygons[index].Color.G != 0, UnlitAt(runs, static run => run.FirstPolygon, index));
        }

        Assert.Equal(2, view.Lines.Length);
        for (int index = 0; index < view.Lines.Length; index++)
        {
            Assert.Equal(view.Lines[index].Color.G != 0, UnlitAt(runs, static run => run.FirstLine, index));
        }

        // A sprite, a polygon and a line each under both, two glyphs and two particles each, and the map's tiles.
        static void Add(Entity entity)
        {
            entity.Add(new SpriteRenderer(SceneFixtures.Frame(4, 4)));
            entity.Add(new ColorPolygon([Vector2.Zero, new(4f, 0f), new(0f, 4f)]));
            entity.Add(new Segment());
            entity.Add(new Label(FontFixtures.Font(), "AB"));
            entity.Add(new ParticleEmitter(SceneFixtures.Frame(2, 2), capacity: 2));
        }
    }

    // A polygon light is stored for the light alone, tinted and placed by its entity's transform, and lights the
    // frame. One that adds nothing is dropped.
    [Fact]
    public void APolygonLight_IsStoredForTheLightAlone_TintedAndPlaced()
    {
        PolygonLight light = new([Vector2.Zero, new(10f, 0f), new(0f, 10f)]) { Color = ColorRgba.White, Intensity = 3f };
        EntityHierarchyFixtures.Node holder = new(new Vector2(50f, 50f)) { Tint = Marked, Scale = new Vector2(2f, 1f) };
        holder.Add(light);
        SimulationHost run = SceneFixtures.Simulate(holder);
        run.Step();

        FrameView view = run.Simulation.View;
        StoredLightPolygon stored = Assert.Single(view.LightPolygons.ToArray());
        Assert.Equal((Marked, 3f, 3), (stored.Color, stored.Intensity, stored.Count));
        Assert.Equal(new Vector2(70f, 50f), view.PolygonPoints[stored.FirstPoint + stored.Count + 1]);
        Assert.Equal((0, 0, 1), (view.Polygons.Length, view.Lights.Length, view.Metrics.Lights));
        Assert.True(view.LitWorld);

        light.Intensity = 0f;
        run.Step();
        Assert.Equal(0, run.Simulation.View.LightPolygons.Length);
        Assert.False(run.Simulation.View.LitWorld);
    }

    // A point light stretches with each axis of its entity's scale, keeps its sprite's aspect, and mirrors on an
    // axis the scale turns negative, as a sprite does. An intensity past two is kept whole.
    [Theory]
    [InlineData(1f, 1f, 20f, 10f, false, false)]
    [InlineData(2f, 0.5f, 40f, 5f, false, false)]
    [InlineData(-1f, 3f, 20f, 30f, true, false)]
    [InlineData(1f, -2f, 20f, 20f, false, true)]
    public void APointLight_FollowsEachAxisOfItsScale_AndMirrorsOnANegativeOne(float scaleX, float scaleY, float width, float height, bool flipX, bool flipY)
    {
        Sprite wide = new(TextureHandle.White, new TextureRegion(0, 0, 8, 4), new Vector2(4f, 2f));
        EntityHierarchyFixtures.Node holder = new(new Vector2(50f, 50f)) { Scale = new Vector2(scaleX, scaleY) };
        holder.Add(new PointLight { Sprite = wide, Radius = 10f, Intensity = 3f });
        SimulationHost run = SceneFixtures.Simulate(holder);
        run.Step();

        LightIntent light = Assert.Single(run.Simulation.View.Lights.ToArray());
        Assert.Equal((new Vector2(width, height), flipX, flipY, 3f), (light.Size, light.FlipX, light.FlipY, light.Intensity));
    }

    // An additive draw is no light: a frame of additive sprites and polygons and unlit entities, with no light and a
    // white ambient, runs no light pass and stores nothing for one.
    [Fact]
    public void AdditiveAndUnlitDraws_LightNothing_AndAFrameOfThemRunsNoLightPass()
    {
        EntityHierarchyFixtures.Node glow = new(new Vector2(50f, 50f)) { Unlit = true };
        glow.Add(new ColorRect(new Vector2(4f, 4f)) { Blend = BlendMode.Additive });
        glow.Add(new ColorPolygon([Vector2.Zero, new(4f, 0f), new(0f, 4f)]) { Blend = BlendMode.Additive });
        SimulationHost run = SceneFixtures.Simulate(glow);
        run.Step();

        FrameView view = run.Simulation.View;
        Assert.False(view.LitWorld);
        Assert.Equal((0, 0, 0), (view.Lights.Length, view.LightPolygons.Length, view.Metrics.Lights));
    }

    // An unlit scene marks every run unlit, whatever its entities set, and its light is still drawn.
    [Fact]
    public void AnUnlitScene_MarksEveryRunUnlit_AndStillDrawsItsLight()
    {
        EntityHierarchyFixtures.Node lamp = new(new Vector2(20f, 20f)) { Unlit = false };
        lamp.Add(new SpriteRenderer(SceneFixtures.Frame(4, 4)));
        lamp.Add(new PointLight { Radius = 8f });
        SceneFixtures.HookScene scene = new();
        scene.Light(ColorRgba.Black, ColorRgba.White, unlit: true);
        scene.Add(lamp);
        using SceneSimulation simulation = new(scene);

        FrameView view = simulation.View;
        Assert.Equal((true, true, 1), (view.UnlitWorld, view.LitWorld, view.Lights.Length));
        Assert.True(UnlitAt(view.PlacementRuns, static run => run.FirstSprite, 0));
    }

    // The Unlit flag of the run in force at an intent's index on its own list.
    private static bool UnlitAt(ReadOnlySpan<PlacementRun> runs, Func<PlacementRun, int> first, int index)
    {
        bool unlit = false;
        foreach (PlacementRun run in runs)
        {
            if (first(run) <= index)
            {
                unlit = run.Unlit;
            }
        }

        return unlit;
    }

    private sealed class Segment : Renderer
    {
        internal override bool Steps => false;

        protected internal override void Draw(FrameView view) =>
            view.Add(new LineIntent(Entity!.Position, Entity.Position + new Vector2(4f, 0f), ColorRgba.White));
    }
}
