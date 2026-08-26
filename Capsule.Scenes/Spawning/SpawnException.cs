namespace Capsule.Scenes.Spawning;

/// <summary>
/// An entity could not be spawned. The message names the spawn type and what the registry does
/// hold; a host that loaded the spawn from a file adds that file's path.
/// </summary>
public sealed class SpawnException : Exception
{
    public SpawnException()
    {
    }

    public SpawnException(string message)
        : base(message)
    {
    }

    public SpawnException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
