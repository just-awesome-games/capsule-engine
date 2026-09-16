using System.Numerics;
using Capsule.Assets.Generated;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// The distant hills spawned by the <c>hills</c> entry of <c>scenes/room.scene.json</c>. The entry
/// authors the <c>scrollFactor</c> — half the camera's travel along X, all of it along Y — and this
/// class only says what the hills look like: one frame repeating without bound along X, banded
/// under the terrain and over the <see cref="Sky"/>. Authored with its base on the floor line, so
/// the silhouette sits on the ground however far the room has scrolled.
/// </summary>
public sealed class Hills : Entity
{
    /// <summary>The whole of <c>textures/backdrops/hills.png</c>, anchored at its top-left corner.</summary>
    private static readonly Sprite Silhouette = new(CapsuleAssets.Textures.Backdrops.Hills, new TextureRegion(0, 0, 160, 64));

    public Hills(EntitySpawn spawn)
        : base(spawn)
    {
        ZIndex = -10;
        Add(new SpriteRenderer(Silhouette) { Tiling = new Vector2(float.PositiveInfinity, 0f) });
    }
}
