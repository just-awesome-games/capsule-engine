using System.Numerics;
using Capsule.Assets;
using Capsule.Collision;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Physics;
using Capsule.Scenes.Spawning;
using Capsule.Scenes.Tiles;

namespace Capsule.Tests.Performance;

/// <summary>
/// A room the size a Capsule game actually ships: a tiled floor and ceiling with platforms, a
/// scattering of entity colliders, and one mover walking the length of it.
/// </summary>
internal static class CollisionWorkload
{
    internal const int TileSize = 16;
    internal const int TilesWide = 256;
    internal const int TilesHigh = 48;
    internal const int Actors = 120;

    internal const string Solid = "solid";
    internal const string Platform = "platform";
    internal const string Actor = "actor";

    private const int FloorRow = 40;
    private const int RoofRow = 30;

    private static readonly TextureHandle Atlas = new("terrain", ".png");

    private static readonly TileDefinition[] Palette =
    [
        TileGrid.EmptyTile,
        new(Solid, 0, Solid),
        new(Platform, 1, Platform, CellFaces2D.Top),
    ];

    /// <summary>The starting box of the mover: a character-sized body on the floor.</summary>
    internal static Aabb2D Mover =>
        Aabb2D.FromCorner(new Vector2(4f * TileSize, (FloorRow - 2) * TileSize), new Vector2(12f, 24f));

    internal static int[] Cells()
    {
        int[] cells = new int[TilesWide * TilesHigh];

        for (int x = 0; x < TilesWide; x++)
        {
            for (int y = 0; y <= RoofRow; y++)
            {
                cells[(y * TilesWide) + x] = 1;
            }

            for (int y = FloorRow; y < TilesHigh; y++)
            {
                cells[(y * TilesWide) + x] = 1;
            }

            // A top-face ledge every seventh column, three columns wide.
            if (x % 7 < 3)
            {
                cells[((FloorRow - 5) * TilesWide) + x] = 2;
            }
        }

        return cells;
    }

    internal static CollisionWorld2D World()
    {
        CollisionWorld2D world = new();
        CellProfile2D[] profiles =
        [
            new(null),
            new(world.Layer(Solid)),
            new(world.Layer(Platform), CellFaces2D.Top),
        ];
        world.AddGrid(TileSize, TilesWide, TilesHigh, Cells(), profiles);

        CollisionLayer actor = world.Layer(Actor);
        float spacing = (float)TilesWide * TileSize / Actors;
        for (int index = 0; index < Actors; index++)
        {
            world.Add(
                Shape2D.Box(Vector2.Zero, new Vector2(12f, 12f)),
                new Vector2(index * spacing, ((FloorRow - 1 - (index % 3)) * TileSize) + 4f),
                actor,
                CollisionFilter.None);
        }

        return world;
    }

    internal static Scene Room()
    {
        TileGrid grid = new(TileSize, TilesWide, TilesHigh, Palette, Cells(), Atlas, 2);

        return new Scene(new SceneContent(
            new SceneDocument([new TileMapPlacement(1, grid)], 2),
            new EntityRegistry([])));
    }

    /// <summary>An entity that walks right, falls, and turns around at the far wall.</summary>
    internal sealed class Walker : Entity
    {
        private readonly BoxCollider2D _collider;
        private readonly KinematicBody2D _mover;
        private float _direction = 1f;

        internal Walker(Vector2 position)
            : base(position)
        {
            _collider = new BoxCollider2D(new Vector2(12f, 24f));
            _collider.SetFilter(Solid, Platform);
            _collider.ReportsContacts = true;
            _collider.ContactEntered += _ => Contacts++;
            _collider.ContactExited += _ => Contacts--;
            Add(_collider);
            _mover = new KinematicBody2D(_collider);
            _mover.BlocksOn(Solid, Platform);
            Add(_mover);
        }

        internal int Contacts { get; private set; }

        protected internal override void OnStep(in StepContext context)
        {
            MoveResult2D result = _mover.Move(new Vector2(_direction * 2f, 4f));

            if (result.BlockedX)
            {
                _direction = -_direction;
            }
        }
    }
}
