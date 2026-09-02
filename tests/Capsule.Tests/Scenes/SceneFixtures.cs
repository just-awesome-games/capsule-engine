using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;
using Capsule.Scenes.Tiles;

namespace Capsule.Tests.Scenes;

internal static class SceneFixtures
{
    internal const int TileSize = 16;

    // Above every placement id the fixtures mint, so the tile-map entry never collides with one.
    internal const int TerrainId = 100;

    internal static readonly ColorRgba Solid = new(0x44, 0x53, 0x6B);

    internal delegate void StepHook(Scene scene, in StepContext context);

    internal static SceneDocument Room(params EntityPlacement[] entities) =>
        new([new TileMapPlacement(TerrainId, RoomGrid()), .. entities], TerrainId + 1);

    /// <summary>A document of entities alone: no tile-map entry composes out of it.</summary>
    internal static SceneDocument RoomWithoutTerrain(params EntityPlacement[] entities) =>
        new([.. entities], TerrainId + 1);

    internal static TileGrid RoomGrid() =>
        new(TileSize, 3, 2, [TileGrid.EmptyTile, new TileDefinition("solid", Solid)], [0, 1, 0, 0, 0, 0]);

    /// <summary>A scene of one tile map drawn as rows of '#' for solid terrain and '.' for empty.</summary>
    internal static Scene Terrain(params string[] rows) =>
        new(Content(
            new SceneDocument([new TileMapPlacement(TerrainId, TerrainGrid(rows))], TerrainId + 1),
            Registry()));

    /// <summary>The grid behind <see cref="Terrain"/>: every '#' collides on the layer "solid".</summary>
    internal static TileGrid TerrainGrid(params string[] rows)
    {
        int width = rows[0].Length;
        int[] cells = new int[width * rows.Length];
        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < width; x++)
            {
                cells[(y * width) + x] = rows[y][x] == '#' ? 1 : 0;
            }
        }

        return new TileGrid(
            TileSize,
            width,
            rows.Length,
            [TileGrid.EmptyTile, new TileDefinition("solid", Solid, "solid")],
            cells);
    }

    internal static EntityRegistry Registry(params (string Type, EntitySpawner Spawner)[] entities)
    {
        List<KeyValuePair<string, EntitySpawner>> entries = new(entities.Length);
        foreach ((string type, EntitySpawner spawner) in entities)
        {
            entries.Add(new KeyValuePair<string, EntitySpawner>(type, spawner));
        }

        return new EntityRegistry(entries);
    }

    internal static SceneContent Content(SceneDocument document, EntityRegistry entities) => new(document, entities);

    internal static Scene RoomScene(SceneDocument document, EntityRegistry entities) =>
        new(Content(document, entities));

    internal static TileMap TerrainOf(Scene scene) => Assert.IsType<TileMap>(scene.Entities[0]);

    internal static StepContext Step(long tick = 0) =>
        new(1.0 / 60.0, new InputState(new ActionBindings()), tick);

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

    internal sealed class Placed(EntitySpawn spawn) : Entity(spawn.Position)
    {
        internal EntitySpawn Spawn { get; } = spawn;
    }

    internal sealed class Recorder(string name, List<string> log) : Entity(Vector2.Zero)
    {
        protected internal override void OnStep(in StepContext context) => log.Add(name);

        protected internal override void OnAddedToScene() => log.Add($"{name}+");

        protected internal override void OnRemovedFromScene() => log.Add($"{name}-");
    }

    internal sealed class RecordingComponent(string name, List<string> log) : Component
    {
        protected internal override void OnStep(in StepContext context) => log.Add(name);
    }

    internal sealed class StripeRenderer(ColorRgba color) : Renderer
    {
        public override void Draw(FrameView view)
        {
            Entity entity = Entity!;
            view.AddQuad(new QuadIntent(entity.PreviousPosition, entity.Position, new Vector2(1f, 64f), color));
        }
    }

    internal sealed class Twin(string name, List<string> log) : Entity(Vector2.Zero)
    {
        internal string Name { get; } = name;

        public override bool Equals(object? obj) => obj is Twin;

        public override int GetHashCode() => 0;

        protected internal override void OnRemovedFromScene() => log.Add($"{Name}-");
    }
}
