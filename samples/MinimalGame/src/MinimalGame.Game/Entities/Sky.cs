using System.Numerics;
using Capsule.Assets.Generated;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// The screen-fixed backdrop spawned by the <c>sky</c> entry of <c>scenes/room.scene.json</c>: a
/// <see cref="Entity.ScrollFactor"/> of zero on both axes keeps it where it is whatever the camera
/// does, and the frame repeats without bound along X so no edge of it is ever seen. Authored at the
/// world origin, which is the camera corner every layer is measured from, it fills the screen.
/// The factor, the tiling and the band are this class's own; <see cref="Hills"/> shows the same
/// factor authored in the document instead.
/// </summary>
public sealed class Sky : Entity
{
    /// <summary>The whole of <c>textures/backdrops/sky.png</c>, anchored at its top-left corner.</summary>
    private static readonly Sprite Dusk = new(CapsuleAssets.Textures.Backdrops.Sky, new TextureRegion(0, 0, 320, 180));

    public Sky(EntitySpawn spawn)
        : base(spawn)
    {
        ScrollFactor = Vector2.Zero;
        ZIndex = -20;
        Add(new SpriteRenderer(Dusk) { Tiling = new Vector2(float.PositiveInfinity, 0f) });
    }
}
