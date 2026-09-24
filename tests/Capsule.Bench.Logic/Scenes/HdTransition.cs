using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary><see cref="Transition"/> with two 4096-texel pages on a 1920 by 1080 surface, so each boundary decodes, uploads and releases HD-sized media.</summary>
[Workload(WorkloadKind.Rendering, Surface.Hd1080)]
public sealed class HdTransition : Scene
{
    protected override void OnStep(in StepContext context) => Run.RequestScene<HdPageA>();

    /// <summary>The half drawing <c>hd-page-a</c>.</summary>
    public sealed class HdPageA() : TransitionHalf<HdPageB>(CapsuleAssets.Textures.HdPageATexture, 512, 160f, World.HdViewportSize, 96);

    /// <summary>The half drawing <c>hd-page-b</c>.</summary>
    public sealed class HdPageB() : TransitionHalf<HdPageA>(CapsuleAssets.Textures.HdPageBTexture, 512, 160f, World.HdViewportSize, 96);
}
