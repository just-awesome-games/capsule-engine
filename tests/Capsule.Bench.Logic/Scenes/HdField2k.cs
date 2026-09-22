using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// 2 000 drifting 256-pixel sprites from a 2048-texel atlas on a 1920 by 1080 linear surface, some
/// 130 megapixels of fill a frame: GPU fill, read in <c>intervalMs</c> p95 and max beside <c>drawMs</c>.
/// </summary>
[Workload(WorkloadKind.Rendering, Surface.Hd1080)]
public sealed class HdField2k : SpriteFieldScene
{
    public HdField2k()
        : base(new Sprite(CapsuleAssets.Textures.HdAtlas, new TextureRegion(0, 0, 256, 256), new Vector2(128f, 128f)), 256f, World.HdViewportSize, count: 2_000)
    {
    }
}
