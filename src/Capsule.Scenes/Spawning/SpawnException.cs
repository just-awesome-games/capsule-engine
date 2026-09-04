namespace Capsule.Scenes.Spawning;

/// <summary>
/// An entity could not be spawned. The message names the spawn type and what the registry holds.
/// </summary>
public sealed class SpawnException : Exception
{
    /// <summary>Creates the exception with the runtime's own default message.</summary>
    public SpawnException()
    {
    }

    /// <param name="message">The spawn type that failed, and what the registry does hold.</param>
    public SpawnException(string message)
        : base(message)
    {
    }

    /// <param name="message">The spawn type that failed, and what the registry does hold.</param>
    /// <param name="innerException">The failure being re-thrown with more context.</param>
    public SpawnException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
