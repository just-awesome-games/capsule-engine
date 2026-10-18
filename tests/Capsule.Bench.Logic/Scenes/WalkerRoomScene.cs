using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>A parked camera over a document's tile room and 1 000 walkers spread above its floor, with the walker's options stated by each workload subclass.</summary>
public abstract class WalkerRoomScene : Scene
{
    protected WalkerRoomScene(SceneContent content, float stepHeight = 0f, bool restsOnCenter = false)
        : base(content)
    {
        TileMap room = FindSingle<TileMap>();
        Camera = new ParkedCamera(room.Size / 2f);

        // Walkers start spread across the room above its features, which lie on the row over the two floor rows.
        for (int index = 0; index < 1_000; index++)
        {
            Add(new Walker(
                new Vector2(
                    World.TileSize + ((index * 7) % ((room.Width - 3) * World.TileSize)),
                    World.TileSize + ((index * 13) % ((room.Height - 7) * World.TileSize))),
                index,
                stepHeight,
                restsOnCenter));
        }
    }
}
