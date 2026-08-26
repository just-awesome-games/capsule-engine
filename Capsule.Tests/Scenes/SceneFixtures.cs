using System.Numerics;
using Capsule.Input;
using Capsule.Maps;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Components;
using Capsule.Scenes.Entities;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Scenes;

/// <summary>
/// Scenes built entirely in memory. <c>Capsule.Scenes</c> reads no files, so nothing here needs
/// one, and a spec is a pure function of the map it names.
/// </summary>
internal static class SceneFixtures
{
    internal const int TileSize = 16;

    internal static readonly ColorRgba Solid = new(0x44, 0x53, 0x6B);

    /// <summary>A scene's per-step hook, as a spec supplies one.</summary>
    internal delegate void StepHook(Scene scene, in StepContext context);

    /// <summary>A 3x2 room, solid at tile (1, 0) only, holding the objects given.</summary>
    internal static Map Room(params MapObject[] objects)
    {
        int nextId = 1;
        foreach (MapObject placed in objects)
        {
            nextId = Math.Max(nextId, placed.Id + 1);
        }

        return new Map(RoomGrid(), objects, nextId);
    }

    /// <summary>The room's grid alone, for a tilemap built with no map in sight.</summary>
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

    /// <summary>A map scene's context.</summary>
    internal static MapSceneContext Context(Map map, EntityRegistry entities) => new(map, entities);

    /// <summary>The engine's own map scene, composed from a room.</summary>
    internal static MapScene RoomScene(Map map, EntityRegistry entities) => new(Context(map, entities));

    /// <summary>A composed map scene's terrain: its first entity, always.</summary>
    internal static TileMap TerrainOf(Scene scene) => Assert.IsType<TileMap>(scene.Entities[0]);

    internal static StepContext Step(long tick = 0) =>
        new(1.0 / 60.0, new InputState(new ActionBindings()), tick);

    /// <summary>A scene with no map: the spec composes whatever entities it needs.</summary>
    internal sealed class HookScene(Action<Scene>? start = null, StepHook? step = null) : Scene
    {
        internal int Starts { get; private set; }

        protected override void OnStart()
        {
            Starts++;
            start?.Invoke(this);
        }

        protected override void OnStep(in StepContext context) => step?.Invoke(this, in context);
    }

    /// <summary>A scene with no map in sight, composed from spawn data alone.</summary>
    internal sealed class SpawnScene : Scene
    {
        internal SpawnScene(EntityRegistry entities, params EntitySpawn[] spawns) => Spawn(spawns, entities);
    }

    /// <summary>A map scene subclass, taking its context in one argument and passing it straight on.</summary>
    internal sealed class Room01(MapSceneContext context) : MapScene(context)
    {
        internal Map Composed => Map;

        internal TileMap Terrain => Tiles;
    }

    /// <summary>An entity that drifts one world unit right per step.</summary>
    internal sealed class Drifter(Vector2 position) : Entity(position)
    {
        internal Drifter()
            : this(Vector2.Zero)
        {
        }

        public override void Update(in StepContext context) => Position += Vector2.UnitX;
    }

    /// <summary>An entity that mutates its scene from inside its own attach hook.</summary>
    internal sealed class Meddler(Action<Scene> onAdded) : Entity(Vector2.Zero)
    {
        protected override void OnAddedToScene() => onAdded(Scene!);
    }

    /// <summary>An entity that reads its scene while the step is running.</summary>
    internal sealed class Watcher(Action<Scene> observe) : Entity(Vector2.Zero)
    {
        public override void Update(in StepContext context) => observe(Scene!);
    }

    /// <summary>An entity spawned from a map, keeping what it was handed.</summary>
    internal sealed class Placed(EntitySpawn spawn) : Entity(spawn.Position)
    {
        internal EntitySpawn Spawn { get; } = spawn;
    }

    /// <summary>An entity that writes its name into a shared log every time anything runs.</summary>
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

    /// <summary>A renderer the engine has never heard of, to prove the seam takes one.</summary>
    internal sealed class StripeRenderer(ColorRgba color) : Renderer
    {
        public override void Draw(FrameView view)
        {
            Entity entity = Entity!;
            view.AddQuad(new QuadIntent(entity.PreviousPosition, entity.Position, new Vector2(1f, 64f), color));
        }
    }

    /// <summary>Any two of these compare equal; scene membership must not care.</summary>
    internal sealed class Twin(string name, List<string> log) : Entity(Vector2.Zero)
    {
        internal string Name { get; } = name;

        public override bool Equals(object? obj) => obj is Twin;

        public override int GetHashCode() => 0;

        protected override void OnRemovedFromScene() => log.Add($"{Name}-");
    }
}
