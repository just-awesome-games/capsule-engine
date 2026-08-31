using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Components;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Entities;
using Capsule.Scenes.Spawning;
using Capsule.Scenes.Tiles;

namespace Capsule.Tests.Scenes;

internal static class SceneFixtures
{
    internal const int TileSize = 16;

    // Above every placement id the fixtures mint, so the terrain entry never collides with one.
    internal const int TerrainId = 100;

    internal static readonly ColorRgba Solid = new(0x44, 0x53, 0x6B);

    internal delegate void StepHook(Scene scene, in StepContext context);

    internal static SceneDocument Room(params EntityPlacement[] entities) =>
        new(new TileMapPlacement(TerrainId, RoomGrid()), entities, TerrainId + 1);

    /// <summary>A document of entities alone: no terrain entry, so no tile map composes out of it.</summary>
    internal static SceneDocument RoomWithoutTerrain(params EntityPlacement[] entities) =>
        new(null, entities, TerrainId + 1);

    internal static TileGrid RoomGrid() =>
        new(TileSize, 3, 2, [TileGrid.EmptyTile, new TileDefinition("solid", Solid)], [0, 1, 0, 0, 0, 0]);

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
        internal SpawnScene(EntityRegistry entities, params EntitySpawn[] spawns) => Spawn(spawns, entities);
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

        public override void Update(in StepContext context) => Position += Vector2.UnitX;
    }

    internal sealed class Meddler(Action<Scene> onAdded) : Entity(Vector2.Zero)
    {
        protected override void OnAddedToScene() => onAdded(Scene!);
    }

    internal sealed class Watcher(Action<Scene> observe) : Entity(Vector2.Zero)
    {
        public override void Update(in StepContext context) => observe(Scene!);
    }

    internal sealed class Placed(EntitySpawn spawn) : Entity(spawn.Position)
    {
        internal EntitySpawn Spawn { get; } = spawn;
    }

    internal sealed class Recorder(string name, List<string> log) : Entity(Vector2.Zero)
    {
        public override void Update(in StepContext context) => log.Add(name);

        // protected, not protected internal: that is what an override outside the engine's own
        // assembly must be, and every game entity is outside it.
        protected override void OnAddedToScene() => log.Add($"{name}+");

        protected override void OnRemovedFromScene() => log.Add($"{name}-");
    }

    internal sealed class RecordingComponent(string name, List<string> log) : Component
    {
        public override void Update(in StepContext context) => log.Add(name);
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

        protected override void OnRemovedFromScene() => log.Add($"{Name}-");
    }
}
