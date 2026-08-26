namespace Capsule.Maps;

/// <summary>A map is malformed. The message states the defect and, where one exists, the fix.</summary>
public sealed class MapFormatException : Exception
{
    public MapFormatException()
    {
    }

    public MapFormatException(string message)
        : base(message)
    {
    }

    public MapFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
