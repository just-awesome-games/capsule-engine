namespace Capsule.Bench.Logic.Scenes;

/// <summary>20 000 drifting sprites on one texture: the atlased game's frame.</summary>
[Workload(WorkloadKind.Rendering)]
public sealed class SpriteField20k : SpriteFieldScene
{
    public SpriteField20k()
        : base(count: 20_000)
    {
    }
}
