using System.Numerics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;
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

        // The real camera shows world [368, 432] about 400. The layer's camera is centred halfway from
        // the scroll centre at 32 to that, at 216, and shows [184, 248]. Each tile is placed by its centre.
        Assert.Equal([184f, 200f, 216f, 232f, 248f], simulation.View.Sprites.ToArray().Select(tile => tile.Position.X));
    }

    // Whatever the fit, the bounds, the alpha, a zoom, an offset and the placed span, the rect a layer
    // is drawn at lies inside the region its intent was culled against. Factor one is the camera itself.
    [Theory]
    [InlineData(ViewportFit.Letterbox, true)]
    [InlineData(ViewportFit.Expand, false)]
    [InlineData(ViewportFit.Expand, true)]
    [InlineData(ViewportFit.FixedHeight, true)]
    public void TheLayersCullRegion_CoversEveryRectTheFrameCanDrawItAt(ViewportFit fit, bool bounded)
    {
        Vector2[] factors = [Vector2.One, Vector2.Zero, new Vector2(0.5f, 0.5f), new Vector2(1.2f, 1.2f), new Vector2(2f, 0.5f), new Vector2(-0.5f, 1f)];
        // Up to the four-to-one aspect the camera's own cull covers.
        Vector2[] outputs = [new Vector2(320, 180), new Vector2(640, 180), new Vector2(320, 400), new Vector2(720, 180)];
        Vector2 scrollCenter = new(160, 90);
        CameraView camera = new(
            new Vector2(30, 40),
            new Vector2(1300, 700),
            new Vector2(320, 180),
            fit,
            bounded ? new Rect(0, 0, 1200, 600) : null,
            scrollCenter)
        {
            PreviousSize = new Vector2(400, 225),
            PreviousOffset = new Vector2(-6, 4),
            Offset = new Vector2(9, -3),
        };

        foreach (Vector2 factor in factors)
        {
            FrameView view = new() { Camera = camera, ScrollFactor = factor };
            Rect cull = view.Camera.SweptBounds;

            foreach (Vector2 output in outputs)
            {
                for (float alpha = 0f; alpha <= 1f; alpha += 0.125f)
                {
                    CameraView still = camera.At(alpha);
                    Vector2 span = still.ResolveSpan(output);
                    Rect real = still.Place(1f, span);
                    Vector2 center = scrollCenter + ((real.Position + (span / 2f) - scrollCenter) * factor);
                    Rect drawn = new(center - (span / 2f), span);

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
    public void TheCamerasScrollCenter_TravelsWithTheView()
    {
        SceneFixtures.HookScene scene = new(SceneFixtures.Opens(new Vector2(300, 40)));
        scene.Camera.ScrollCenter = new Vector2(160, 90);

        SceneSimulation simulation = new(scene);

        Assert.Equal(new Vector2(160, 90), simulation.View.Camera.ScrollCenter);
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.Camera.ScrollCenter = new Vector2(float.NaN, 0));
    }

    // Unset, the scroll centre is half the declared viewport. A camera centred there draws every
    // layer as authored, whatever span the fit places, and a simple scene authors nothing.
    [Theory]
    [InlineData(ViewportFit.Letterbox)]
    [InlineData(ViewportFit.Expand)]
    public void AnUnsetScrollCenter_DrawsTheFirstScreenAsAuthored(ViewportFit fit)
    {
        Vector2 viewport = new(256, 224);
        SceneFixtures.HookScene scene = new(opened =>
        {
            SceneFixtures.Open(opened, viewport / 2f, viewport);
            opened.Camera.Fit = fit;
        });
        SceneSimulation simulation = new(scene);

        Assert.Null(scene.Camera.ScrollCenter);
        Assert.Equal(viewport / 2f, simulation.View.Camera.ScrollCenter);

        foreach (Vector2 factor in new[] { Vector2.Zero, new Vector2(0.5f, 0.25f), new Vector2(-1f, 2f) })
        {
            (Rect frame, Vector2 layer) = Drawn(simulation.View.Camera, new Vector2(2560, 1080), factor);
            Assert.Equal(frame.Left, layer.X, 0.001f);
            Assert.Equal(frame.Top, layer.Y, 0.001f);
        }
    }

    // A layer is measured from the view's centre, the one point the fit and the zoom leave in place.
    // At one camera centre a half-speed layer lands the same distance from it under a letterbox, a
    // wider output under Expand, and a zoom.
    [Theory]
    [InlineData(ViewportFit.Letterbox, 1f, 1920f)]
    [InlineData(ViewportFit.Expand, 1f, 1920f)]
    [InlineData(ViewportFit.Expand, 1f, 2520f)]
    [InlineData(ViewportFit.Letterbox, 2f, 1920f)]
    public void AScrolledLayer_HoldsItsPlaceAboutTheCentreAtAnyAspectAndZoom(ViewportFit fit, float zoom, float outputWidth)
    {
        Vector2 center = new(1000, 112);
        SceneFixtures.HookScene scene = new(opened =>
        {
            SceneFixtures.Open(opened, center, new Vector2(256, 224));
            opened.Camera.Fit = fit;
            opened.Camera.Zoom = zoom;
        });
        SceneSimulation simulation = new(scene);

        (Rect frame, Vector2 layer) = Drawn(simulation.View.Camera, new Vector2(outputWidth, 1080), new Vector2(0.5f, 0.5f));
        Vector2 authored = new(700, 150);
        Vector2 drawn = frame.Position + (authored - layer);

        // Moved by (centre - (128, 112)) * (1 - 0.5) from where it was authored.
        Assert.Equal(1136f, drawn.X, 0.001f);
        Assert.Equal(150f, drawn.Y, 0.001f);
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

    // The document's scroll centre is the scene's, so a camera the scene installs over the default one
    // takes it too.
    [Fact]
    public void ADocumentsScrollCenter_ReachesTheCameraTheSceneInstalls()
    {
        SceneDocument document = new([], 1, settings: new SceneSettings { ScrollCenter = new Vector2(160, 90) });
        Camera installed = new();
        Scene scene = new ComposedScene(SceneFixtures.Content(document, SceneFixtures.Registry()), installed);

        SceneSimulation simulation = new(scene);

        Assert.Same(installed, scene.Camera);
        Assert.Equal(new Vector2(160, 90), installed.ScrollCenter);
        Assert.Equal(new Vector2(160, 90), simulation.View.Camera.ScrollCenter);
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

    // The frame the host places for this view on an output, and the corner of the layer at factor, as
    // the host draws it.
    private static (Rect Frame, Vector2 Layer) Drawn(in CameraView camera, Vector2 output, Vector2 factor)
    {
        Vector2 span = camera.ResolveSpan(output);
        Rect frame = camera.Place(1f, span);

        return (frame, ScrollLayout.Corner(frame.Position, ScrollLayout.Parallax(frame.Position, span, camera.ScrollCenter), factor));
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
