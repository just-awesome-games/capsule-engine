using System.IO.Compression;
using Capsule;
using Capsule.Generated;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Tiles;
using MinimalGame.Game;
using MinimalGame.Game.Entities;
using MinimalGame.Game.Scenes;

namespace MinimalGame.Tests;

// The room the game ships, composed from its own document rather than one built in code, under the
// bindings the shell installs: what a test here proves is what a player at the keyboard would see.
// The build copies the derived document beside this assembly through the logic project reference,
// exactly where the shell finds it, gzipped as it ships.
public static class RoomFixture
{
    private const string RoomDocument = "assets/scenes/room.scene.json.gz";

    // The document places the player's 8x8 body with its feet on the floor row and the hazard to
    // its right on the same floor. The ledges sit two tiles up; a body at UnderLedgeX is fully
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
    public static PlayableScene Compose()
    {
        SceneDocument document;
        using (StreamReader inflated = new(new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, RoomDocument)), CompressionMode.Decompress)))
        {
            document = SceneDocumentFile.Parse(inflated.ReadToEnd());
        }

        return (PlayableScene)CapsuleScenes.Registry.Create(CapsuleAssets.Scenes.RoomScene, document);
    }

    public static Player PlayerOf(SimulationHost room)
    {
        ArgumentNullException.ThrowIfNull(room);

        return room.Scene.FindSingle<Player>();
    }

    // The room also paints a backdrop from the terrain's palette, and only the terrain has a collider.
    public static TileMap TerrainOf(SimulationHost room)
    {
        ArgumentNullException.ThrowIfNull(room);

        return room.Scene.FindFirst<TileMap>(static map => map.TryGet<TileMapCollider2D>(out _))!;
    }
}
