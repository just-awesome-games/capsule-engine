namespace Capsule.Bench.Logic.Scenes;

/// <summary>5 000 drifting sprites on one texture: the atlased game's frame.</summary>
[Workload(WorkloadKind.Rendering)]
public sealed class SpriteField5k : SpriteFieldScene
{
    public SpriteField5k()
        : base(count: 5_000)
    {
    }
}
