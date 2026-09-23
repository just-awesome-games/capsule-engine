namespace Capsule.Scenes;

// Whether the entity steps, resolved down the tree by the scene once a step and again on attach.
public partial class Entity
{
    private StepMode _resolvedStepMode = StepMode.Pausable;

    /// <summary>
    /// When this entity and its components step against the scene's <see cref="Scene.Paused"/> and
    /// <see cref="Scene.Freeze"/>. A write takes effect from the next step.
    /// </summary>
    public StepMode StepMode
    {
        get;

        set
        {
            if ((uint)value > (uint)StepMode.Never)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The value names no StepMode. Pass one of its declared members.");
            }

            field = value;
        }
    }

    // Whether this step skips the entity's step and late step, its components' and its contact and
    // screen settles. It still draws, collides and runs its structural hooks.
    internal bool Held { get; private set; }

    // A parent precedes its children in the scene's list and attaches before them, so an inherited mode
    // is one read of the parent's resolved mode.
    internal void ResolveHold(bool paused, bool frozen)
    {
        _resolvedStepMode = StepMode == StepMode.Inherit
            ? _parent?._resolvedStepMode ?? StepMode.Pausable
            : StepMode;

        Held = _resolvedStepMode switch
        {
            StepMode.Pausable => paused || frozen,
            StepMode.WhenPaused => !paused,
            StepMode.Never => true,
            _ => false,
        };
    }
}
