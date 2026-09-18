namespace Capsule.Bench.Logic.Scenes;

/// <summary>50 000 drifting sprites with every fiftieth on the white texel: the unatlased game's frame, about two thousand texture switches.</summary>
[Workload(WorkloadKind.Rendering)]
public sealed class TextureSwitches50k : SpriteFieldScene
{
    public TextureSwitches50k()
        : base(count: 50_000, switches: true)
    {
    }
}
