namespace Capsule.Bench.Logic.Entities;

/// <summary>A static 8 by 8 sprite drawn through the material it is handed.</summary>
public sealed class Swatch : Entity
{
    private static readonly Sprite Frame = new(CapsuleAssets.Textures.TileTexture, new TextureRegion(0, 0, 8, 8));

    public Swatch(Vector2 position, Material material)
        : base(position) => Add(new SpriteRenderer(Frame) { Material = material });
}
