using Capsule.Assets.Generated;

namespace Capsule.Bench.Logic.Scenes;

/// <summary><see cref="Transition"/> with two 4096-texel pages on a 1920 by 1080 surface, so each boundary decodes, uploads and releases HD-sized media.</summary>
[Workload(WorkloadKind.Rendering, Surface.Hd1080)]
public sealed class HdTransition : Transition
{
    public HdTransition()
        : base(CapsuleAssets.Textures.HdPageA, CapsuleAssets.Textures.HdPageB, 512, 160f, World.HdViewportSize, 96)
    {
    }
}
