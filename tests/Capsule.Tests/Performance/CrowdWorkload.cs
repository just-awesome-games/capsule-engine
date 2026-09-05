using System.Numerics;
using Capsule.Animation;
using Capsule.Assets;
using Capsule.Collision;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Animation;
using Capsule.Scenes.Physics;
using Capsule.Scenes.Rendering;
using Capsule.Scenes.Tiles;

namespace Capsule.Tests.Performance;

/// <summary>
/// A crowd in one room: a thousand kinematic bodies with box colliders walking a tile ring, each
/// drawing an animated sprite. They collide with the room and never with each other, which is the
/// shape a game's population takes — the density is in one place and the collision is against
/// terrain.
/// </summary>
internal static class CrowdWorkload
{
    internal const int TileSize = 16;
    internal const int TilesWide = 16;
    internal const int TilesHigh = 14;
    internal const int Players = 1000;

    internal const string Solid = "solid";
    internal const string Platform = "platform";
    internal const string Actor = "actor";

    private const int PlatformRow = 8;

    private static readonly TextureHandle Atlas = new("terrain", ".png");

    private static readonly Sprite[] Walk =
    [
        new(Atlas, new TextureRegion(0, 0, 16, 24)),
        new(Atlas, new TextureRegion(16, 0, 16, 24)),
        new(Atlas, new TextureRegion(32, 0, 16, 24)),
        new(Atlas, new TextureRegion(48, 0, 16, 24)),
    ];

    private static readonly SpriteClip Clip = new(Walk, [4, 4, 4, 4], loop: true);

    private static readonly TileDefinition[] Palette =
    [
        TileGrid.EmptyTile,
        new(Solid, 0, Solid),
        new(Platform, 1, Platform, CellFaces2D.Top),
    ];

    internal static Scene Room()
    {
        int[] cells = new int[TilesWide * TilesHigh];

        for (int x = 0; x < TilesWide; x++)
        {
            for (int y = 0; y < TilesHigh; y++)
            {
                bool wall = x == 0 || y == 0 || x == TilesWide - 1 || y == TilesHigh - 1;
                cells[(y * TilesWide) + x] = wall ? 1 : 0;
            }
        }

        for (int x = 4; x < TilesWide - 4; x++)
        {
            cells[(PlatformRow * TilesWide) + x] = 2;
        }

        Scene scene = new();
        scene.Add(new TileMap(new TileGrid(TileSize, TilesWide, TilesHigh, Palette, cells, Atlas, 2)));

        for (int index = 0; index < Players; index++)
        {
            scene.Add(new Player(
                new Vector2(
                    TileSize + ((index * 7) % ((TilesWide - 2) * TileSize)),
                    TileSize + ((index * 13) % ((TilesHigh - 3) * TileSize))),
                index));
        }

        return scene;
    }

    internal sealed class Player : Entity
    {
        private readonly KinematicBody2D _body;
        private readonly SpriteAnimator _animator;
        private float _direction;

        internal Player(Vector2 position, int index)
            : base(position)
        {
            _direction = (index % 2) == 0 ? 1f : -1f;

            BoxCollider2D collider = new(new Vector2(12f, 24f));
            collider.Layer = Actor;
            collider.SetFilter(Solid, Platform);
            Add(collider);

            _body = new KinematicBody2D(collider);
            _body.BlocksOn(Solid, Platform);
            Add(_body);

            SpriteRenderer renderer = new(Walk[0]);
            Add(renderer);

            _animator = new SpriteAnimator(renderer);
            Add(_animator);
        }

        protected internal override void OnStart() => _animator.Play(Clip);

        protected internal override void OnStep(in StepContext context)
        {
            MoveResult2D result = _body.Move(new Vector2(_direction * 2f, 4f));

            if (result.BlockedX)
            {
                _direction = -_direction;
            }
        }
    }
}
