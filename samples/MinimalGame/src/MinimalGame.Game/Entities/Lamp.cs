using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>A dark post topped by a warm glow and the point light that lights the room around it.</summary>
public sealed class Lamp : Entity
{
    private const float GlowScale = 1.5f;

    private static readonly Vector2 PostSize = new(2f, 10f);
    private static readonly ColorRgba PostColor = new(40, 40, 44);
    private static readonly ColorRgba HeadColor = new(255, 200, 140);

    // An 8-texel greyscale falloff. Its config makes it an r8 mask the lamp's colour tints, sampled
    // linearly so it stays soft in this point-sampled game.
    private static readonly Sprite Glow = new(CapsuleAssets.Textures.GlowTexture, new TextureRegion(0, 0, 8, 8), new Vector2(4f, 4f));

    public Lamp(EntitySpawn spawn)
        : base(spawn)
    {
        Add(new ColorRect(PostSize) { Color = PostColor, Offset = new Vector2(-PostSize.X / 2f, -PostSize.Y) });
        Add(new PointLight { Radius = 56f, Color = HeadColor, Offset = new Vector2(0f, -PostSize.Y) });

        Entity head = new(this, new Vector2(0f, -PostSize.Y - 2f)) { Scale = new Vector2(GlowScale) };
        head.Add(new SpriteRenderer(Glow) { Color = HeadColor, Blend = BlendMode.Additive });
    }
}
