namespace Capsule.Levels.Cli.Tiled;

/// <summary>A Tiled map cannot be imported. The message states what to change in Tiled.</summary>
public sealed class TiledImportException : Exception
{
    public TiledImportException()
    {
    }

    public TiledImportException(string message)
        : base(message)
    {
    }

    public TiledImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
