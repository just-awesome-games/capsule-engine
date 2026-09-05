using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Rendering;
using Capsule.Scenes.Spawning;
using Capsule.Scenes.Tiles;

namespace Capsule.Tests.Performance;

internal static class StageWorkload
{
    internal const int TileSize = 16;
    internal const int TilesWide = 512;
    internal const int TilesHigh = 64;
    internal const double StepSeconds = 1.0 / 60.0;

    internal const string DocumentName = "stage";

    internal const int StepsBetweenSpawns = 3;

    internal const int SparkLifeSteps = 180;

    internal const int PlacedEntities = 140;

    internal static readonly Vector2 CameraViewport = new(320f, 180f);

    private const int PaletteSolid = 1;
    private const int PalettePlatform = 2;
    private const int LowestFloorRow = 58;
    private const int CorridorTilesHigh = 9;
    private const int HeroTileY = 55;

    // One atlas of two cells across, the shape a real terrain tileset takes.
    private static readonly TextureHandle Atlas = new("terrain", ".png");
    private static readonly TileDefinition Solid = new("solid", 0);
    private static readonly TileDefinition Platform = new("platform", 1);

    private static readonly Sprite HeroFrame = new(Atlas, new TextureRegion(0, 0, 16, 24));
    private static readonly Sprite ActorFrame = new(Atlas, new TextureRegion(0, 0, 16, 16));
    private static readonly Sprite SparkFrame = new(Atlas, new TextureRegion(0, 0, 4, 4));

    internal static SceneDefaults Defaults => new(TextureSampling.Point);

    internal static SceneDocument Build()
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

        TileGrid grid = new(TileSize, TilesWide, TilesHigh, [TileGrid.EmptyTile, Solid, Platform], tiles, Atlas, 2);

        EntityPlacement[] placements = new EntityPlacement[PlacedEntities];
        placements[0] = new EntityPlacement(1, "hero", 4f * TileSize, HeroTileY * TileSize);

        float spacing = (float)TilesWide * TileSize / (PlacedEntities - 1);
        for (int index = 1; index < PlacedEntities; index++)
        {
            placements[index] = new EntityPlacement(
                index + 1,
                "actor",
                index * spacing,
                (HeroTileY + (index % 4)) * TileSize);
        }

        // The tile-map entry takes the id after the placements, so the whole workload has one id
        // space the way an authored document does.
        return new SceneDocument(
            [new TileMapPlacement(PlacedEntities + 1, grid), .. placements],
            PlacedEntities + 2);
    }

    internal static EntityRegistry Entities() =>
        new(
        [
            new EntityRegistration("hero", static spawn => new Hero(spawn)),
            new EntityRegistration("actor", static spawn => new Actor(spawn)),
        ]);

    internal static SceneRegistry Scenes() =>
        new(
            Entities(),
            [
                SceneRegistration.FromDocument(
                    typeof(StageScene),
                    DocumentName,
                    static content => new StageScene(content)),
            ]);

    internal static StageScene Compose(SceneDocument document, StageChurn churn = StageChurn.Spawning) =>
        new(new SceneContent(document, Entities()), churn);

    internal sealed class Hero : Entity
    {
        internal Hero(EntitySpawn spawn)
            : base(spawn.Position) =>
            Add(new SpriteRenderer(HeroFrame));

        protected internal override void OnStep(in StepContext context) => Position += Vector2.UnitX;
    }

    internal sealed class Actor : Entity
    {
        private readonly Vector2 _drift;

        internal Actor(EntitySpawn spawn)
            : base(spawn.Position)
        {
            _drift = new Vector2(((spawn.Id % 5) - 2) * 0.25f, 0f);

            if (spawn.Id % 3 == 0)
            {
                Add(new SpriteRenderer(ActorFrame));
            }
        }

        protected internal override void OnStep(in StepContext context) => Position += _drift;
    }

    internal sealed class Spark : Entity
    {
        private readonly Vector2 _velocity;
        private int _life = SparkLifeSteps;

        internal Spark(Vector2 origin, Vector2 velocity)
            : base(origin)
        {
            _velocity = velocity;
            Add(new SpriteRenderer(SparkFrame));
        }

        protected internal override void OnStep(in StepContext context)
        {
            Position += _velocity;

            if (--_life <= 0)
            {
                Scene!.Remove(this);
            }
        }
    }

    internal sealed class StageScene : Scene
    {
        private readonly Hero _hero;
        private readonly StageChurn _churn;
        private readonly Entity _flicker;
        private int _spawned;

        internal StageScene(SceneContent content, StageChurn churn = StageChurn.Spawning)
            : base(content)
        {
            _churn = churn;
            _hero = FindSingle<Hero>();
            _flicker = Entities[^1];
        }

        protected override void OnStart()
        {
            Camera.ViewportSize = CameraViewport;
            Camera.Teleport(_hero.Position);
        }

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
