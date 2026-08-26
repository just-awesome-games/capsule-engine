namespace Capsule.Scenes;

/// <summary>
/// A slot of behaviour or appearance on one <see cref="Scenes.Entity"/>. Updated after its
/// entity, in the order it was attached.
/// </summary>
public abstract class Component
{
    /// <summary>The entity this component is attached to; null until it is attached.</summary>
    public Entity? Entity { get; internal set; }

    /// <summary>Advances this component by one fixed step.</summary>
    public virtual void Update(in StepContext context)
    {
    }
}
