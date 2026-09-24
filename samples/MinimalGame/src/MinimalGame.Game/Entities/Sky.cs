using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// The screen-fixed backdrop spawned by the <c>sky</c> entry of <c>scenes/room.scene.json</c>: dusk,
/// repeating without bound along X, pinned to the screen and banded under everything else. Authored at
/// the world origin, it fills the screen. The factor, the tiling and the band are this class's own,
/// while <see cref="Hills"/> has its factor authored in the document.
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
