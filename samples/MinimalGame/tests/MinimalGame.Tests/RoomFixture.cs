using Capsule;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Generated;
using MinimalGame.Game;
using MinimalGame.Game.Entities;
using MinimalGame.Game.Scenes;

namespace MinimalGame.Tests;

// The room the game ships, composed from its own document rather than one built in code, under the
// bindings the shell installs: what a test here proves is what a player at the keyboard would see.
// The build copies the derived document beside this assembly through the logic project reference,
// exactly where the shell finds it.
public static class RoomFixture
{
    private const string RoomDocument = "assets/scenes/room.scene.json";

    // The document places the player's 8x8 body with its feet on the floor row and the hazard to
    // its right on the same floor. The ledges sit two tiles up; a body at UnderLedgeX is fully
    // beneath the first run of them.
    public const float FloorTop = 176f;

    public const float LedgeTop = 144f;

    public const float UnderLedgeX = 160f;

    public static SimulationHost Simulate()
    {
        SceneDocument document = SceneDocumentFile.Load(Path.Combine(AppContext.BaseDirectory, RoomDocument));

        Run run = new();
        GameBoot.Start(run);

        return new SimulationHost(new Room(new SceneContent(document, CapsuleEntities.Registry)), run: run);
    }

    public static Player PlayerOf(SimulationHost room)
    {
        ArgumentNullException.ThrowIfNull(room);

        return room.Scene.FindSingle<Player>();
    }
}
