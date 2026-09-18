namespace Capsule.Bench.Logic.Scenes;

/// <summary>50 000 drifting sprites on one texture: the atlased game's frame.</summary>
[Workload(WorkloadKind.Rendering)]
public sealed class SpriteField50k : SpriteFieldScene
{
    public SpriteField50k()
        : base(count: 50_000)
    {
    }
}
