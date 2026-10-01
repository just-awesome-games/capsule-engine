using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// The dusk backdrop, pinned to the screen under everything else. Placed at the world origin, it fills
/// the screen. Its scroll factor is set in code, where <see cref="Hills"/> authors its own.
/// </summary>
public sealed class Sky : Entity
{
    /// <summary>The whole of <c>textures/backdrops/sky.png</c>, anchored at its top-left corner.</summary>
    private static readonly Sprite Dusk = new(CapsuleAssets.Textures.Backdrops.SkyTexture, new TextureRegion(0, 0, 320, 180));

    public Sky(EntitySpawn spawn)
        : base(spawn)
    {
        ScrollFactor = Vector2.Zero;
        ZIndex = -20;
        Add(new SpriteRenderer(Dusk) { Tiling = new Vector2(float.PositiveInfinity, 0f) });
    }
}
