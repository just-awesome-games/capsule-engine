namespace Capsule.Scenes.Spawning;

/// <summary>
/// An entity could not be spawned. The message names the spawn type and what the registry holds instead.
/// </summary>
public sealed class SpawnException : Exception
{
    /// <param name="message">The spawn type that failed, and what the registry holds instead.</param>
    public SpawnException(string message)
        : base(message)
    {
    }

    /// <param name="message">The spawn type that failed, and what the registry holds instead.</param>
    /// <param name="innerException">The failure being rethrown with more context.</param>
    public SpawnException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
