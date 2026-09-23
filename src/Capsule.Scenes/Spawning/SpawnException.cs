namespace Capsule.Scenes.Spawning;

/// <summary>
/// An entity could not be spawned. The message names the spawn type that failed.
/// </summary>
public sealed class SpawnException : Exception
{
    internal SpawnException(string message)
        : base(message)
    {
    }

    internal SpawnException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
