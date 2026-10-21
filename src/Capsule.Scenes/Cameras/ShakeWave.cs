namespace Capsule.Scenes;

/// <summary>The motion a <see cref="CameraShake"/> moves the view with.</summary>
public enum ShakeWave
{
    /// <summary>A smooth random wander on each axis, independent between the axes. The default.</summary>
    Noise,

    /// <summary>
    /// A cosine at the shake's frequency, both axes in step, starting at its peak on the step the shake
    /// begins.
    /// </summary>
    Sine,
}
