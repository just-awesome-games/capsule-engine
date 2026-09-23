namespace Capsule.Scenes;

/// <summary>When an entity steps against its scene's <see cref="Scene.Paused"/> and <see cref="Scene.Freeze"/>.</summary>
public enum StepMode
{
    /// <summary>Takes the parent's mode. A root that inherits is <see cref="Pausable"/>.</summary>
    Inherit,

    /// <summary>Steps while the scene is neither paused nor frozen.</summary>
    Pausable,

    /// <summary>Steps only while the scene is paused, whether or not a freeze is running.</summary>
    WhenPaused,

    /// <summary>Steps through pause and freeze alike.</summary>
    Always,

    /// <summary>Never steps, and still draws and collides.</summary>
    Never,
}
