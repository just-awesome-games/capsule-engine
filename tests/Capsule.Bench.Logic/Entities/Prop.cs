namespace Capsule.Bench.Logic.Entities;

/// <summary>A static 4 by 4 sprite that never steps.</summary>
public sealed class Prop : Entity
{
    private static readonly Sprite Frame = new(CapsuleAssets.Textures.TerrainTexture, new TextureRegion(0, 0, 4, 4));

    public Prop(Vector2 position)
        : base(position) => Add(new SpriteRenderer(Frame));
}
