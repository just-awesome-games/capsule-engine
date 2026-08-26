using System.Numerics;
using Capsule.Input;
using Capsule.Levels;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Components;
using Capsule.Scenes.Entities;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Scenes;

/// <summary>
/// Scenes built entirely in memory. <c>Capsule.Scenes</c> reads no files, so nothing here needs
/// one, and a spec is a pure function of the level it names.
/// </summary>
internal static class SceneFixtures
{
    internal const int TileSize = 16;

    internal static readonly ColorRgba Solid = new(0x44, 0x53, 0x6B);

    /// <summary>A scene's per-step hook, as a spec supplies one.</summary>
    internal delegate void StepHook(Scene scene, in StepContext context);

    /// <summary>A 3x2 room, solid at tile (1, 0) only, holding the entities given.</summary>
    internal static Level Room(params LevelEntity[] entities) => Room(["empty", "solid"], entities);

    /// <summary>The same room over a palette of the caller's choosing; only "solid" is painted.</summary>
    internal static Level Room(string[] tileTypes, params LevelEntity[] entities)
    {
        int nextId = 1;
        foreach (LevelEntity entity in entities)
        {
            nextId = Math.Max(nextId, entity.Id + 1);
        }

        return new Level(TileSize, 3, 2, tileTypes, [0, 1, 0, 0, 0, 0], entities, nextId);
    }

    internal static LevelTypeRegistry Registry(params (string Id, EntitySpawner Spawner)[] levelTypes)
    {
        List<KeyValuePair<string, EntitySpawner>> entries = new(levelTypes.Length);
        foreach ((string id, EntitySpawner spawner) in levelTypes)
        {
            entries.Add(new KeyValuePair<string, EntitySpawner>(id, spawner));
        }

        return new LevelTypeRegistry(entries);
    }

    internal static StepContext Step(long tick = 0) =>
        new(1.0 / 60.0, new InputState(new ActionBindings()), tick);

    /// <summary>A scene with no level: the spec composes whatever entities it needs.</summary>
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

    /// <summary>The shape a level scene takes: the tilemap first, then the level's own entities.</summary>
    internal sealed class LevelScene : Scene
    {
        private readonly Action<Scene>? _start;

        internal LevelScene(
            Level level,
            LevelTypeRegistry levelTypes,
            TileColorResolver? tileColor = null,
            Action<Scene>? start = null)
        {
            Tiles = new TileMap(level, tileColor ?? (static _ => Solid));
            Add(Tiles);
            Size = Tiles.Size;
            Spawn(level, levelTypes);
            _start = start;
        }

        internal TileMap Tiles { get; }

        protected override void OnStart() => _start?.Invoke(this);
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

    /// <summary>An entity spawned from a level, keeping what it was handed.</summary>
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
