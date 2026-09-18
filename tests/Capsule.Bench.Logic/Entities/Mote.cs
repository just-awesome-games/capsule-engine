using System.Numerics;
using Capsule.Assets.Generated;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

public sealed class Mote : Entity
{
    private static readonly Sprite Frame = new(CapsuleAssets.Textures.Terrain, new TextureRegion(0, 0, 4, 4));

    public Mote()
        : base(Vector2.Zero)
    {
        Add(new SpriteRenderer(Frame));
        Add(new VisibleOnScreenNotifier2D(new Vector2(4f, 4f)));
    }

    public long DiesAt { get; set; }
}
