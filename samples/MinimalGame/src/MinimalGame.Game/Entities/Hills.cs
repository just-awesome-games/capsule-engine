using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// The distant hills: one frame repeating along X, under the terrain and over the <see cref="Sky"/>.
/// Their <c>scrollFactor</c> is authored in the room's document.
/// </summary>
public sealed class Hills : Entity
{
    /// <summary>The whole of <c>textures/backdrops/hills.png</c>, anchored at its top-left corner.</summary>
    private static readonly Sprite Silhouette = new(CapsuleAssets.Textures.Backdrops.HillsTexture, new TextureRegion(0, 0, 160, 64));

    public Hills(EntitySpawn spawn)
        : base(spawn)
    {
        ZIndex = -10;
        Add(new SpriteRenderer(Silhouette) { Tiling = new Vector2(float.PositiveInfinity, 0f) });
    }
}
