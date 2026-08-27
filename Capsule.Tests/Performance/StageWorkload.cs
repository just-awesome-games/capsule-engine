using System.Numerics;
using Capsule.Maps;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Components;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Performance;

/// <summary>
/// A scrolling stage at the scale a game runs at: a 512x64 map, two hundred live entities of
/// which roughly half draw, a camera that moves every step, and twenty spawns and twenty
/// despawns a second running through it. Every measurement in <see cref="StagePerformanceTests"/>
/// is taken against this one workload, so two numbers taken at different times compare.
/// </summary>
internal static class StageWorkload
{
    internal const int TileSize = 16;
    internal const int TilesWide = 512;
    internal const int TilesHigh = 64;
    internal const double StepSeconds = 1.0 / 60.0;

    /// <summary>The map's bare name, and so the file a loader resolves it to.</summary>
    internal const string MapName = "stage";

    /// <summary>One spawn and one despawn every third step: twenty of each a second at 60 Hz.</summary>
    internal const int StepsBetweenSpawns = 3;

    /// <summary>Steps a spark lives, which holds the churning population at sixty.</summary>
    internal const int SparkLifeSteps = 180;

    /// <summary>Entities the map places; the sparks bring the live population to two hundred.</summary>
    internal const int PlacedEntities = 140;

    internal static readonly Vector2 CameraViewport = new(320f, 180f);

    private const int PaletteSolid = 1;
    private const int PalettePlatform = 2;
    private const int LowestFloorRow = 58;
    private const int CorridorTilesHigh = 9;
    private const int HeroTileY = 55;

    private static readonly TileDefinition Solid = new("solid", new ColorRgba(0x44, 0x53, 0x6B));
    private static readonly TileDefinition Platform = new("platform", new ColorRgba(0x6B, 0x53, 0x44));

    /// <summary>The stage's defaults, as a game's boot would set them for a 320x180 pixel-art game.</summary>
    internal static SceneDefaults Defaults => new(CameraViewport, TextureSampling.Point);

    /// <summary>
    /// The stage map: a rolling corridor running the full width, cut into solid rock, with the
    /// placed population strung along it. About half the cells the camera spans are painted, as a
    /// screen of a real stage is.
    /// </summary>
    internal static Map Build()
    {
        int[] tiles = new int[TilesWide * TilesHigh];
        for (int x = 0; x < TilesWide; x++)
        {
            int floorRow = LowestFloorRow + ((x / 16) % 3);
            int roofRow = floorRow - CorridorTilesHigh;

            for (int y = 0; y <= roofRow; y++)
            {
                tiles[(y * TilesWide) + x] = PaletteSolid;
            }

            for (int y = floorRow; y < TilesHigh; y++)
            {
                tiles[(y * TilesWide) + x] = PaletteSolid;
            }

            if (x % 7 < 3)
            {
                tiles[((floorRow - 4) * TilesWide) + x] = PalettePlatform;
            }
        }

        TileGrid grid = new(TileSize, TilesWide, TilesHigh, [TileGrid.EmptyTile, Solid, Platform], tiles);

        MapObject[] objects = new MapObject[PlacedEntities];
        objects[0] = new MapObject(1, "hero", 4f * TileSize, HeroTileY * TileSize);

        float spacing = (float)TilesWide * TileSize / (PlacedEntities - 1);
        for (int index = 1; index < PlacedEntities; index++)
        {
            objects[index] = new MapObject(
                index + 1,
                "actor",
                index * spacing,
                (HeroTileY + (index % 4)) * TileSize);
        }

        return new Map(grid, objects, PlacedEntities + 1);
    }

    internal static EntityRegistry Entities() =>
        new(
        [
            new KeyValuePair<string, EntitySpawner>("hero", static spawn => new Hero(spawn)),
            new KeyValuePair<string, EntitySpawner>("actor", static spawn => new Actor(spawn)),
        ]);

    internal static SceneRegistry Scenes() =>
        new(
            Entities(),
            [SceneRegistration.MapBacked(typeof(StageScene), MapName, static context => new StageScene(context))]);

    /// <summary>The stage as a scene, composed straight from a map already in hand.</summary>
    internal static StageScene Compose(Map map, StageChurn churn = StageChurn.Spawning) =>
        new(new MapSceneContext(map, Entities()), churn);

    /// <summary>The player: drawn, and moving one world unit right per step for the camera to follow.</summary>
    internal sealed class Hero : Entity
    {
        internal Hero(EntitySpawn spawn)
            : base(spawn.Position) =>
            Add(new QuadRenderer(new Vector2(16f, 24f), new ColorRgba(0x3C, 0xA6, 0xE8)));

        public override void Update(in StepContext context) => Position += Vector2.UnitX;
    }

    /// <summary>A placed entity; every third one draws, so about half the population carries a renderer.</summary>
    internal sealed class Actor : Entity
    {
        private readonly Vector2 _drift;

        internal Actor(EntitySpawn spawn)
            : base(spawn.Position)
        {
            _drift = new Vector2(((spawn.Id % 5) - 2) * 0.25f, 0f);

            if (spawn.Id % 3 == 0)
            {
                Add(new QuadRenderer(new Vector2(16f, 16f), new ColorRgba(0xC8, 0x4A, 0x4A)));
            }
        }

        public override void Update(in StepContext context) => Position += _drift;
    }

    /// <summary>What churns: spawned on a fixed cadence, drawn, and removing itself when it expires.</summary>
    internal sealed class Spark : Entity
    {
        private readonly Vector2 _velocity;
        private int _life = SparkLifeSteps;

        internal Spark(Vector2 origin, Vector2 velocity)
            : base(origin)
        {
            _velocity = velocity;
            Add(new QuadRenderer(new Vector2(4f, 4f), new ColorRgba(0xFF, 0xD0, 0x40)));
        }

        public override void Update(in StepContext context)
        {
            Position += _velocity;

            if (--_life <= 0)
            {
                Scene!.Remove(this);
            }
        }
    }

    /// <summary>The stage scene: a camera locked to the hero, and whatever churn is being measured.</summary>
    internal sealed class StageScene : MapScene
    {
        private readonly Hero _hero;
        private readonly StageChurn _churn;
        private readonly Entity _flicker;
        private int _spawned;

        internal StageScene(MapSceneContext context, StageChurn churn = StageChurn.Spawning)
            : base(context)
        {
            _churn = churn;
            _hero = FindSingle<Hero>();
            _flicker = Entities[^1];
        }

        protected override void OnStart() => Camera.Teleport(_hero.Position);

        protected override void OnStep(in StepContext context)
        {
            switch (_churn)
            {
                case StageChurn.DrawListOnly:
                    // One structural change a step and nothing else: the population, the update
                    // work and the draw work are the same as StageChurn.None either side of it.
                    if (_flicker.Scene is null)
                    {
                        Add(_flicker);
                    }
                    else
                    {
                        Remove(_flicker);
                    }

                    break;

                case StageChurn.Spawning:
                    if (context.Tick % StepsBetweenSpawns == 0)
                    {
                        Add(new Spark(_hero.Position, new Vector2(4f, ((_spawned++ % 5) - 2) * 0.5f)));
                    }

                    break;

                default:
                    break;
            }
        }

        protected override void OnLateStep(in StepContext context) => Camera.Center = _hero.Position;
    }
}
