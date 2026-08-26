namespace Capsule.Scenes.Spawning;

/// <summary>
/// A level entity could not be spawned. The message names the entity's type and what the
/// registry does hold; the host adds the level's path.
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
