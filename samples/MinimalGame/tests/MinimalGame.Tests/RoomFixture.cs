using System.Numerics;
using Capsule;
using Capsule.Generated;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tiles;
using MinimalGame.Game;
using MinimalGame.Game.Entities;
using MinimalGame.Game.Scenes;

namespace MinimalGame.Tests;

// The room the game ships, composed from its own document rather than one built in code, under the
// bindings the shell installs: what a test here proves is what a player at the keyboard would see.
// The build copies the document beside this assembly through the logic project reference, exactly
// where the shell finds it.
public static class RoomFixture
{
    // The document places the player's 8x8 body with its feet on the floor row and the hazard to
    // its right on the same floor. The ledges sit two tiles up, and a body at UnderLedgeX is fully
    // beneath the first run of them.
    public const float FloorTop = 176f;

    public const float LedgeTop = 144f;

    public const float UnderLedgeX = 160f;

    public static SimulationHost Simulate()
    {
        Run run = new();
        GameBoot.Start(run);

        return new SimulationHost(Compose(), run: run);
    }

    // The room as the scene boundary receives it, composed and not yet started.
    public static PlayableScene Compose() => (PlayableScene)CapsuleScenes.Registry.Create(CapsuleAssets.Scenes.RoomScene);

    public static Player PlayerOf(SimulationHost room)
    {
        ArgumentNullException.ThrowIfNull(room);

        return room.Scene.FindSingle<Player>();
    }

    // Places the player at position before each of steps empty steps. Neither gravity nor a push
    // carries it off.
    public static void StandAt(SimulationHost room, Vector2 position, int steps = 1)
    {
        Player player = PlayerOf(room);

        for (int step = 0; step < steps; step++)
        {
            player.Position = position;
            room.Step(DeviceSnapshot.Empty);
        }
    }

    // Stands the player's feet on the floor row at x for one step.
    public static void StandAt(SimulationHost room, float x) => StandAt(room, new Vector2(x, FloorTop - 8f));

    // The camera's bounds after one step standing at x on the floor row.
    public static Rect? BoundsWithPlayerAt(SimulationHost room, float x)
    {
        StandAt(room, x);

        return room.Scene.Camera.Bounds;
    }

    // The room also paints a backdrop from the terrain's palette, and only the terrain has a collider.
    public static TileMap TerrainOf(SimulationHost room)
    {
        ArgumentNullException.ThrowIfNull(room);

        return room.Scene.FindFirst<TileMap>(static map => map.Has<TileMapCollider2D>())!;
    }
}
