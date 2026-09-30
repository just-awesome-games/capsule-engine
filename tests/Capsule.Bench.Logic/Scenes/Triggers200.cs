using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>200 overlapping 96 by 96 sensors over the ledges of <c>triggers-200.scene.json</c>, with 1 000 actors drifting through them: contact reporting and its per-step settle. A sensor's contact buffers double when it meets more at once than ever before, and those rare steps are the one allocation, by design.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Triggers200 : Scene
{
    // The room's extent in tiles, as the document authors it.
    private const int TilesWide = 80;

    private const int TilesHigh = 45;

    public Triggers200(SceneContent content)
        : base(content)
    {
        Vector2 room = new(TilesWide * World.TileSize, TilesHigh * World.TileSize);
        Camera = new ParkedCamera(room / 2f);

        // A 20 by 10 grid at a 64 by 72 pitch. Neighbouring sensors overlap at this pitch.
        for (int index = 0; index < 200; index++)
        {
            Add(new Zone(new Vector2((index % 20) * 64f, (index / 20) * 72f), new Vector2(96f, 96f)));
        }

        for (int index = 0; index < 1_000; index++)
        {
            Vector2 position = new((index * 37) % (room.X - 8f), (index * 53) % (room.Y - 8f));
            Vector2 velocity = new(((index % 6) - 2.5f) * 0.6f, ((index % 5) - 2) * 0.75f);
            Add(new Crosser(position, velocity, room - new Vector2(8f, 8f)));
        }
    }
}
