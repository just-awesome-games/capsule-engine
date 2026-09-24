using System.Numerics;
using Capsule.Assets;
using Capsule.Input;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;
using Capsule.Tests.Physics;
using Capsule.Tiles;

namespace Capsule.Tests.Scenes;

internal static class SceneFixtures
{
    internal const int TileSize = 16;

    // Above every placement id the fixtures mint, so the tile-map entry never collides with one.
    internal const int TerrainId = 100;

    /// <summary>The one texture every fixture draws from; a column of <see cref="TileSize"/> cells.</summary>
    internal static readonly TextureHandle Atlas = new("atlas", ".png");

    /// <summary>The tile atlas the tile, document and workload fixtures name.</summary>
    internal static readonly TextureHandle TerrainAtlas = new("terrain", ".png");

    /// <summary>The viewport span a scene opens at unless a test needs another.</summary>
    internal static readonly Vector2 Viewport = new(320, 180);

    /// <summary>The top half of a tile.</summary>
    internal static readonly Shape2D HalfHeight = Shape2D.Polygon([new(0f, 0f), new(TileSize, 0f), new(TileSize, TileSize / 2f), new(0f, TileSize / 2f)]);

    internal delegate void StepHook(Scene scene, in StepContext context);

    /// <summary>A frame of <see cref="Atlas"/> cut from its top-left corner.</summary>
    internal static Sprite Frame(int width, int height) => new(Atlas, new TextureRegion(0, 0, width, height));

    internal static SceneDocument Room(params EntityPlacement[] entities) =>
        new([new TileMapPlacement(TerrainId, RoomGrid()), .. entities], TerrainId + 1);

    /// <summary>A document of entities alone: no tile-map entry composes out of it.</summary>
    internal static SceneDocument RoomWithoutTerrain(params EntityPlacement[] entities) =>
        new([.. entities], TerrainId + 1);

    /// <summary>One palette entry: <paramref name="type"/> drawing <paramref name="cell"/>.</summary>
    internal static TileDefinition Tile(string type, int cell, string? layer = null) => new(type, cell, layer);

    internal static TileGrid RoomGrid() =>
        new(TileSize, 3, 2, [TileGrid.EmptyTile, new TileDefinition("solid", 0)], [0, 1, 0, 0, 0, 0], Atlas, 1);

    /// <summary>A scene of one tile map drawn as rows of '#' for solid terrain and '.' for empty.</summary>
    internal static Scene Terrain(params string[] rows) =>
        new(Content(
            new SceneDocument([new TileMapPlacement(TerrainId, TerrainGrid(rows))], TerrainId + 1),
            Registry()));

    /// <summary>
    /// The grid behind <see cref="Terrain"/>. Every '#' is a solid tile on the layer "solid", '-' a one-way
    /// tile, '=' a one-way tile with solid sides and '~' the top half of one, '/' a 45 degree slope rising
    /// to the right and '\' one falling to the right, all on that layer.
    /// </summary>
    internal static TileGrid TerrainGrid(params string[] rows)
    {
        int width = rows[0].Length;
        int[] cells = new int[width * rows.Length];
        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < width; x++)
            {
                cells[(y * width) + x] = rows[y][x] switch
                {
                    '#' => 1,
                    '-' => 2,
                    '/' => 3,
                    '\\' => 4,
                    '=' => 5,
                    '~' => 6,
                    _ => 0,
                };
            }
        }

        return new TileGrid(
            TileSize,
            width,
            rows.Length,
            [
                TileGrid.EmptyTile,
                new TileDefinition("solid", 0, "solid"),
                new TileDefinition("ledge", 0, "solid", OneWay: true),
                new TileDefinition("slope-up", 0, "solid", CollisionFixtures.SlopeUp),
                new TileDefinition("slope-down", 0, "solid", CollisionFixtures.SlopeDown),
                new TileDefinition("girder", 0, "solid", OneWay: true, SolidSides: true),
                new TileDefinition("half-girder", 0, "solid", HalfHeight, OneWay: true, SolidSides: true),
            ],
            cells,
            Atlas,
            1);
    }

    internal static EntityRegistry Registry(params (string Type, EntitySpawner Spawner)[] entities)
    {
        List<EntityRegistration> entries = new(entities.Length);
        foreach ((string type, EntitySpawner spawner) in entities)
        {
            entries.Add(new EntityRegistration(type, spawner));
        }

        return new EntityRegistry(entries);
    }

    internal static SceneContent Content(SceneDocument document, EntityRegistry entities) => new(document, entities);

    internal static Scene RoomScene(SceneDocument document, EntityRegistry entities) =>
        new(Content(document, entities));

    internal static TileMap TerrainOf(Scene scene) => Assert.IsType<TileMap>(scene.Entities[0]);

    /// <summary>A simulation of <paramref name="scene"/> with <paramref name="entities"/> already in it.</summary>
    internal static SceneSimulation Simulation(Scene scene, params Entity[] entities)
    {
        foreach (Entity entity in entities)
        {
            scene.Add(entity);
        }

        return new SceneSimulation(scene);
    }

    internal static StepContext Step(long tick = 0, Vector2 output = default) =>
        new(1.0 / 60.0, new InputState(new ActionBindings()), tick, output);

    /// <summary>Opens <paramref name="scene"/>'s camera on a centre, spanning <paramref name="size"/>.</summary>
    internal static void Open(Scene scene, Vector2 center, Vector2 size)
    {
        scene.Camera.Center = center;
        scene.Camera.ViewportSize = size;
    }

    /// <summary>The same as a scene hook, spanning <see cref="Viewport"/> unless given a size.</summary>
    internal static Action<Scene> Opens(Vector2 center, Vector2? size = null) =>
        scene => Open(scene, center, size ?? Viewport);

    internal sealed class HookScene(Action<Scene>? start = null, StepHook? step = null, StepHook? lateStep = null)
        : Scene
    {
        internal int Starts { get; private set; }

        /// <summary>Reaches the protected camera setter, which only a scene can call.</summary>
        internal void Install(Camera camera) => Camera = camera;

        protected override void OnStart()
        {
            Starts++;
            start?.Invoke(this);
        }

        protected override void OnStep(in StepContext context) => step?.Invoke(this, in context);

        protected override void OnLateStep(in StepContext context) => lateStep?.Invoke(this, in context);
    }

    internal sealed class SpawnScene : Scene
    {
        internal SpawnScene(EntityRegistry entities, params EntitySpawn[] spawns)
            : base(Content(Placements(spawns), entities))
        {
        }

        private static SceneDocument Placements(EntitySpawn[] spawns)
        {
            EntityPlacement[] placements = new EntityPlacement[spawns.Length];
            for (int index = 0; index < spawns.Length; index++)
            {
                EntitySpawn spawn = spawns[index];
                placements[index] = new EntityPlacement(spawn.Id, spawn.Type, spawn.Position.X, spawn.Position.Y);
            }

            return RoomWithoutTerrain(placements);
        }
    }

    internal sealed class Room01(SceneContent content) : Scene(content)
    {
        internal TileMap Terrain => FindSingle<TileMap>();
    }

    /// <summary>An 8x8 box with the <see cref="KinematicBody2D"/> that sweeps it.</summary>
    internal sealed class Body : Entity
    {
        /// <param name="position">Where the box's corner starts.</param>
        /// <param name="blocksOn">The layer that stops the sweep, or null to be stopped by nothing.</param>
        /// <param name="bodyFirst">Attach the body before its collider, which a whole entity may do.</param>
        internal Body(Vector2 position, string? blocksOn = null, bool bodyFirst = false)
            : base(position)
        {
            Collider = new BoxCollider2D(new Vector2(8f, 8f));
            Mover = new KinematicBody2D(Collider);
            if (blocksOn is not null)
            {
                Mover.BlocksOn(blocksOn);
            }

            if (bodyFirst)
            {
                Add(Mover);
                Add(Collider);
                return;
            }

            Add(Collider);
            Add(Mover);
        }

        internal BoxCollider2D Collider { get; }

        internal KinematicBody2D Mover { get; }
    }

    internal sealed class Drifter(Vector2 position) : Entity(position)
    {
        internal Drifter()
            : this(Vector2.Zero)
        {
        }

        protected internal override void OnStep(in StepContext context) => Position += Vector2.UnitX;
    }

    internal sealed class Meddler(Action<Scene> onAdded) : Entity(Vector2.Zero)
    {
        protected internal override void OnAddedToScene() => onAdded(Scene!);
    }

    /// <summary>Runs a hook the moment time begins for it, when the scene is fully composed.</summary>
    internal sealed class Starter(Action<Scene> onStart) : Entity(Vector2.Zero)
    {
        protected internal override void OnStart() => onStart(Scene!);
    }

    internal sealed class Watcher(Action<Scene> observe) : Entity(Vector2.Zero)
    {
        protected internal override void OnStep(in StepContext context) => observe(Scene!);
    }

    internal sealed class Placed(EntitySpawn spawn) : Entity(spawn)
    {
        internal EntitySpawn Spawn { get; } = spawn;
    }

    /// <param name="logsStart">Also log <c>name!</c> as time begins; off, so a log cleared before
    /// the scene starts stays a record of the steps alone.</param>
    internal sealed class Recorder(string name, List<string> log, bool logsStart = false) : Entity(Vector2.Zero)
    {
        protected internal override void OnStart()
        {
            if (logsStart)
            {
                log.Add($"{name}!");
            }
        }

        protected internal override void OnStep(in StepContext context) => log.Add(name);

        protected internal override void OnLateStep(in StepContext context) => log.Add($"{name}.late");

        protected internal override void OnAddedToScene() => log.Add($"{name}+");

        protected internal override void OnRemovedFromScene() => log.Add($"{name}-");
    }

    internal sealed class RecordingComponent(string name, List<string> log) : Component
    {
        protected internal override void OnStep(in StepContext context) => log.Add(name);

        protected internal override void OnLateStep(in StepContext context) => log.Add($"{name}.late");
    }

    internal sealed class StripeRenderer(ColorRgba color) : Renderer
    {
        protected internal override void Draw(FrameView view)
        {
            Entity entity = Entity!;
            view.Add(new SpriteIntent(
                Frame(1, 64),
                entity.PreviousTransform.Position,
                entity.Position,
                PreviousRotation: 0f,
                Rotation: 0f,
                new Vector2(1f, 64f),
                FlipX: false,
                FlipY: false,
                color));
        }
    }

    internal sealed class Twin(string name, List<string> log) : Entity(Vector2.Zero)
    {
        public override bool Equals(object? obj) => obj is Twin;

        public override int GetHashCode() => 0;

        protected internal override void OnRemovedFromScene() => log.Add($"{name}-");
    }
}
