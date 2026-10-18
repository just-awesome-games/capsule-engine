namespace MinimalGame.Game.Entities;

/// <summary>
/// The dust a jump kicks up where it leaves the ground. The puff plays its clip once and leaves the
/// scene, which returns it to its pool.
/// </summary>
public sealed class Puff : Entity
{
    public Puff()
        : base(Vector2.Zero)
    {
        SpriteRenderer sprite = new(CapsuleAssets.Sprites.Effects.PuffSheet.Frames.Puff0);
        Add(sprite);

        // Leaving the scene rewinds the clip. A puff taken from the pool again plays it from the start.
        SpriteAnimator animator = new(sprite) { RemovesEntityWhenFinished = true };
        Add(animator);
        animator.Play(CapsuleAssets.Sprites.Effects.PuffSheet.Clips.Puff);
    }

    /// <summary>Places the puff with its bottom centre on a point.</summary>
    /// <param name="feet">Where the puff stands, in world units.</param>
    /// <returns>This puff, for adding it to the scene in one expression.</returns>
    public Puff Place(Vector2 feet)
    {
        Position = feet;

        return this;
    }
}
