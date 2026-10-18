using System.Numerics;
using Capsule.Animation;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

namespace Capsule.Tests.Allocation;

// A thousand animated kinematic bodies walking a tile ring. They collide with the room and never with
// each other. One slab sits among them on a layer they block on, which keeps the broadphase queried.
internal static class CrowdWorkload
{
    internal const int TilesWide = 16;
    internal const int TilesHigh = 14;
    internal const int Players = 1000;

    private const int TileSize = CollisionWorkload.TileSize;
    private const int PlatformRow = 8;

    private static readonly TextureHandle Atlas = SceneFixtures.TerrainAtlas;

    private static readonly Sprite[] Walk =
    [
        new(Atlas, new TextureRegion(0, 0, 16, 24)),
        new(Atlas, new TextureRegion(16, 0, 16, 24)),
        new(Atlas, new TextureRegion(32, 0, 16, 24)),
        new(Atlas, new TextureRegion(48, 0, 16, 24)),
    ];

    private static readonly SpriteClip Clip = new(Walk, [4, 4, 4, 4], loop: true);

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
        scene.Add(SceneFixtures.Colliding(new TileGrid(TileSize, TilesWide, TilesHigh, CollisionWorkload.Palette, cells, Atlas, 2)));
        scene.Add(new Slab(new Vector2(8 * TileSize, 4 * TileSize)));

        for (int index = 0; index < Players; index++)
        {
            scene.Add(Player(
                new Vector2(
                    TileSize + ((index * 7) % ((TilesWide - 2) * TileSize)),
                    TileSize + ((index * 13) % ((TilesHigh - 3) * TileSize))),
                (index % 2) == 0 ? 1f : -1f));
        }

        return scene;
    }

    // A walker on the actor layer, playing the walk cycle.
    private static CollisionWorkload.Walker Player(Vector2 position, float direction)
    {
        CollisionWorkload.Walker walker = new(position, direction);
        walker.Collider.Layer = CollisionWorkload.Actor;

        SpriteRenderer renderer = new(Walk[0]);
        walker.Add(renderer);

        SpriteAnimator animator = new(renderer);
        walker.Add(animator);
        animator.Play(Clip);

        return walker;
    }

    /// <summary>A still slab on the platform layer.</summary>
    internal sealed class Slab : Entity
    {
        internal Slab(Vector2 position)
            : base(position) =>
            Add(new BoxCollider2D(new Vector2(32f, 8f)) { Layer = CollisionWorkload.Platform });
    }
}
