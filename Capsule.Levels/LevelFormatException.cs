namespace Capsule.Levels;

/// <summary>A level is malformed. The message states the defect and, where one exists, the fix.</summary>
public sealed class LevelFormatException : Exception
{
    public LevelFormatException()
    {
    }

    public LevelFormatException(string message)
        : base(message)
    {
    }

    public LevelFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
