using System.Numerics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;
using Capsule.Tiles;
using Capsule.UI;

namespace Capsule.Tests.Scenes;

public sealed class ParallaxTests
{
    private static readonly Vector2 Half = new(0.5f, 1f);

    // The simulation never moves a scrolled entity's intent: the frame carries the authored
    // positions and, beside them, the factor each run is drawn by.
    [Fact]
    public void TheFrame_CarriesAuthoredPositionsWithTheFactorBesideEachRun()
    {
        SceneFixtures.Drifter far = new(new Vector2(100, 50)) { ScrollFactor = Half };
        far.Add(new SpriteRenderer(SceneFixtures.Frame(4, 4)));
        SceneFixtures.Drifter near = new(new Vector2(200, 10));
        near.Add(new SpriteRenderer(SceneFixtures.Frame(4, 4)));
        near.Add(new SceneFixtures.StripeRenderer(ColorRgba.White));

        SceneFixtures.HookScene scene = new(SceneFixtures.Opens(new Vector2(300, 40)));
        scene.Add(far);
        scene.Add(near);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        FrameView view = simulation.View;
        Assert.Equal(3, view.Sprites.Length);
        Assert.Equal(new Vector2(101, 50), view.Sprites[0].Position);
        Assert.Equal(new Vector2(100, 50), view.Sprites[0].PreviousPosition);
        Assert.Equal(new Vector2(201, 10), view.Sprites[1].Position);
        Assert.Equal(
            [new ParallaxLayer(0, 0, Half), new ParallaxLayer(1, 0, Vector2.One)],
            view.ParallaxLayers.ToArray());
        Assert.Equal(Vector2.One, view.ScrollFactor);
        Assert.Equal(scene.Camera.Center, view.Camera.Center);
    }

    [Fact]
    public void AFrameWithNoScrolledEntity_CarriesNoLayers()
    {
        SceneFixtures.Drifter drifter = new(Vector2.Zero);
        drifter.Add(new SpriteRenderer(SceneFixtures.Frame(4, 4)));
        SceneFixtures.HookScene scene = new();
        scene.Add(drifter);

        SceneSimulation simulation = new(scene);

        Assert.Empty(simulation.View.ParallaxLayers.ToArray());
    }

    // Inside a scrolled entity's Draw the view answers as the camera that entity is drawn by, so a
    // tile map culls to the cells its own layer shows rather than the cells under the real camera.
    [Fact]
    public void ATileMap_SubmitsTheTilesItsVirtualCameraCovers()
    {
        TileMap backdrop = new(new TileGrid(
            SceneFixtures.TileSize,
            40,
            1,
            [TileGrid.EmptyTile, new TileDefinition("back", 0)],
            Enumerable.Repeat(1, 40).ToArray(),
            SceneFixtures.Atlas,
            1))
        {
            ScrollFactor = new Vector2(0.5f, 1f),
        };
        SceneFixtures.HookScene scene = new(SceneFixtures.Opens(new Vector2(400, 8), new Vector2(64, 16)));
        scene.Add(backdrop);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        // The real camera shows world [368, 432]; the layer's camera has its corner at half that.
        Assert.Equal([176f, 192f, 208f, 224f, 240f], simulation.View.Sprites.ToArray().Select(tile => tile.Position.X));
    }

    // Whatever the fit, the bounds, the alpha and the placed span, the rect a layer is drawn at
    // lies inside the region its intent was culled against.
    [Theory]
    [InlineData(ViewportFit.Letterbox, true)]
    [InlineData(ViewportFit.Expand, false)]
    [InlineData(ViewportFit.Expand, true)]
    [InlineData(ViewportFit.FixedHeight, true)]
    public void TheLayersCullRegion_CoversEveryRectTheFrameCanDrawItAt(ViewportFit fit, bool bounded)
    {
        Vector2[] factors = [Vector2.Zero, new Vector2(0.5f, 0.5f), new Vector2(1.2f, 1.2f), new Vector2(2f, 0.5f), new Vector2(-0.5f, 1f)];
        // Up to the four-to-one aspect the camera's own cull covers.
        Vector2[] outputs = [new Vector2(320, 180), new Vector2(640, 180), new Vector2(320, 400), new Vector2(720, 180)];
        Vector2 origin = new(160, 90);
        CameraView camera = new(
            new Vector2(30, 40),
            new Vector2(1300, 700),
            new Vector2(320, 180),
            fit,
            bounded ? new Rect(0, 0, 1200, 600) : null,
            origin);

        foreach (Vector2 factor in factors)
        {
            FrameView view = new() { Camera = camera, ScrollFactor = factor };
            Rect cull = view.Camera.SweptBounds;

            foreach (Vector2 output in outputs)
            {
                Vector2 span = camera.ResolveSpan(output);
                for (float alpha = 0f; alpha <= 1f; alpha += 0.125f)
                {
                    Rect real = camera.Place(alpha, span);
                    Rect drawn = new(origin + ((real.Position - origin) * factor), span);

                    Assert.True(
                        drawn.Left >= cull.Left && drawn.Top >= cull.Top && drawn.Right <= cull.Right && drawn.Bottom <= cull.Bottom,
                        $"factor {factor}, output {output}, alpha {alpha}: drew {drawn} outside {cull}");
                }
            }
        }
    }

    [Fact]
    public void TheViewOutsideADraw_IsTheRealCamera()
    {
        CameraView camera = new(new Vector2(30, 40), new Vector2(320, 180));
        FrameView view = new() { Camera = camera, ScrollFactor = Half };

        Assert.NotEqual(camera, view.Camera);
        view.ScrollFactor = Vector2.One;
        Assert.Equal(camera, view.Camera);
    }

    [Fact]
    public void TheCamerasScrollOrigin_TravelsWithTheView()
    {
        SceneFixtures.HookScene scene = new(SceneFixtures.Opens(new Vector2(300, 40)));
        scene.Camera.ScrollOrigin = new Vector2(160, 90);

        SceneSimulation simulation = new(scene);

        Assert.Equal(new Vector2(160, 90), simulation.View.Camera.ScrollOrigin);
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.Camera.ScrollOrigin = new Vector2(float.NaN, 0));
    }

    [Theory]
    [InlineData(float.NaN, 1f)]
    [InlineData(1f, float.PositiveInfinity)]
    public void AScrollFactorThatIsNotFinite_IsRefused(float x, float y)
    {
        SceneFixtures.Drifter drifter = new(Vector2.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() => drifter.ScrollFactor = new Vector2(x, y));
        Assert.Equal(Vector2.One, drifter.ScrollFactor);
    }

    // An entity draws where it collides, watches the screen from where it is and sits on a layer no
    // camera moves: each refuses a factor, from either side of the attach.
    [Fact]
    public void ACollider_RefusesAScrolledEntityAndAScrolledEntityRefusesACollider()
    {
        SceneFixtures.Drifter scrolled = new(Vector2.Zero) { ScrollFactor = Half };
        BoxCollider2D box = new(new Vector2(8, 8));
        Assert.Throws<InvalidOperationException>(() => scrolled.Add(box));
        Assert.Null(box.Entity);
        Assert.Throws<InvalidOperationException>(() => scrolled.Add(new KinematicBody2D(box)));
        Assert.Throws<InvalidOperationException>(() => scrolled.Add(new VisibleOnScreenNotifier2D(new Vector2(8, 8))));
        Assert.Empty(scrolled.Components.ToArray());

        SceneFixtures.Body body = new(Vector2.Zero);
        Assert.Throws<InvalidOperationException>(() => body.ScrollFactor = Half);
        Assert.Equal(Vector2.One, body.ScrollFactor);

        SceneFixtures.Drifter watching = new(Vector2.Zero);
        watching.Add(new VisibleOnScreenNotifier2D(new Vector2(8, 8)));
        Assert.Throws<InvalidOperationException>(() => watching.ScrollFactor = Half);

        // One on both axes is the world, which every component is at home in.
        body.ScrollFactor = Vector2.One;
        scrolled.ScrollFactor = Vector2.One;
        scrolled.Add(box);
    }

    [Fact]
    public void ACollidingTileMap_RefusesAFactorAndADecorativeOneTakesIt()
    {
        TileMap colliding = new(SceneFixtures.TerrainGrid("#"));
        Assert.Throws<InvalidOperationException>(() => colliding.ScrollFactor = Half);

        TileMap decorative = new(SceneFixtures.RoomGrid()) { ScrollFactor = Half };
        Assert.Equal(Half, decorative.ScrollFactor);
    }

    [Fact]
    public void AScreenEntity_RefusesAFactor()
    {
        ScreenEntity element = new(Anchor.TopLeft, Vector2.Zero);

        Assert.Throws<InvalidOperationException>(() => element.ScrollFactor = Vector2.Zero);
        Assert.Equal(Vector2.One, element.ScrollFactor);
    }

    // The document's origin is the scene's, so a camera the scene installs over the default one
    // takes it too.
    [Fact]
    public void ADocumentsScrollOrigin_ReachesTheCameraTheSceneInstalls()
    {
        SceneDocument document = new([], 1, settings: new SceneSettings { ScrollOrigin = new Vector2(160, 90) });
        Camera installed = new();
        Scene scene = new ComposedScene(SceneFixtures.Content(document, SceneFixtures.Registry()), installed);

        SceneSimulation simulation = new(scene);

        Assert.Same(installed, scene.Camera);
        Assert.Equal(new Vector2(160, 90), installed.ScrollOrigin);
        Assert.Equal(new Vector2(160, 90), simulation.View.Camera.ScrollOrigin);
    }

    // The document supplies the factor ahead of the constructor's body: a class that sets none
    // takes it, a class that sets its own keeps it, and the engine-built tile map takes it as
    // composed.
    [Fact]
    public void ADocumentsScrollFactor_IsTheConstructorsToKeepOrOverride()
    {
        SceneDocument document = new(
            [
                new TileMapPlacement(1, SceneFixtures.RoomGrid(), ScrollFactor: new Vector2(0.25f, 1f)),
                new EntityPlacement(2, "placed", 4f, 4f, ScrollFactor: Vector2.Zero),
                new EntityPlacement(3, "placed", 4f, 4f),
                new EntityPlacement(4, "fixed", 4f, 4f, ScrollFactor: Half),
            ],
            5);
        Scene scene = SceneFixtures.RoomScene(
            document,
            SceneFixtures.Registry(
                ("placed", spawn => new SceneFixtures.Placed(spawn)),
                ("fixed", spawn => new ScreenFixed(spawn))));

        Assert.Equal(new Vector2(0.25f, 1f), scene.Entities[0].ScrollFactor);
        Assert.Equal(Vector2.Zero, scene.Entities[1].ScrollFactor);
        Assert.Equal(Vector2.One, scene.Entities[2].ScrollFactor);
        Assert.Equal(Vector2.Zero, scene.Entities[3].ScrollFactor);
    }

    // The factor is applied before the body runs, so a body that then attaches a collider is
    // refused as it would be for any scrolled entity, and the placement fails at construction.
    [Fact]
    public void ASpawnsFactor_IsRefusedByTheColliderTheBodyAttaches()
    {
        Assert.Throws<InvalidOperationException>(
            () => new Colliding(new EntitySpawn(1, "colliding", Vector2.Zero, Vector2.One, ScrollFactor: Half)));
    }

    private sealed class ScreenFixed : Entity
    {
        public ScreenFixed(EntitySpawn spawn)
            : base(spawn) => ScrollFactor = Vector2.Zero;
    }

    private sealed class Colliding : Entity
    {
        public Colliding(EntitySpawn spawn)
            : base(spawn) => Add(new BoxCollider2D(new Vector2(8, 8)));
    }

    private sealed class ComposedScene : Scene
    {
        public ComposedScene(SceneContent content, Camera camera)
            : base(content) =>
            Camera = camera;
    }
}
