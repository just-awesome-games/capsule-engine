using Capsule.Animation;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;
using SpikesSheet = Capsule.Generated.CapsuleAssets.Sprites.Traps.SpikesSheet;

namespace MinimalGame.Game.Entities;

/// <summary>A floor trap whose spikes rise, hold and sink on a loop, hurting only while they are out.</summary>
public sealed class Spikes : Entity
{
    // Ticks into the cycle the trap starts at. A row of traps placed with rising phases ripples
    // instead of firing together.
    [Authorable]
    private int _phase;

    /// <param name="spawn">The trap's top-left corner, a frame's height above the floor.</param>
    public Spikes(EntitySpawn spawn)
        : base(spawn)
    {
        SpriteRenderer sprite = new(SpikesSheet.Frames.Down);
        Add(sprite);

        // Live on the frames the sheet gives a spikes box, off on the rest.
        sprite.Box(SpikesSheet.Boxes.Spikes).Layer = CollisionLayers.Hazard;

        SpriteAnimator animator = new(sprite);
        Add(animator);
        animator.Play(SpikesSheet.Clips.Cycle, atTick: _phase);
    }
}
