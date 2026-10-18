using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary><see cref="Triggers200"/>'s 200 sensors stacked on one 96 by 96 square of the same room, with its 1 000 actors drifting through a 192 by 192 corner around it: contact reporting and its per-step settle with each sensor holding hundreds of contacts. A sensor's contact buffers double when it meets more at once than ever before, and those rare steps are the one allocation, by design.</summary>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Paced)]
public sealed class Triggers200Stacked : Scene
{
    private const float Corner = 192f;

    public Triggers200Stacked(SceneContent content)
        : base(content)
    {
        Camera = new ParkedCamera(new Vector2(Corner / 2f, Corner / 2f));

        for (int index = 0; index < 200; index++)
        {
            Add(new Zone(new Vector2(Corner / 2f, Corner / 2f), new Vector2(96f, 96f)));
        }

        Vector2 room = new(Corner - 8f, Corner - 8f);
        for (int index = 0; index < 1_000; index++)
        {
            Vector2 position = new((index * 37) % room.X, (index * 53) % room.Y);
            Vector2 velocity = new(((index % 6) - 2.5f) * 0.6f, ((index % 5) - 2) * 0.75f);
            Add(new Crosser(position, velocity, room));
        }
    }
}
