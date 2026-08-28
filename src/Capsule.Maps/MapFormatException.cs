namespace Capsule.Maps;

/// <summary>A map is malformed. The message states the defect and, where one exists, the fix.</summary>
public sealed class MapFormatException : Exception
{
    /// <summary>Creates the exception with the runtime's own default message.</summary>
    public MapFormatException()
    {
    }

    /// <param name="message">The defect, and where one exists the fix.</param>
    public MapFormatException(string message)
        : base(message)
    {
    }

    /// <param name="message">The defect, and where one exists the fix.</param>
    /// <param name="innerException">The parse failure underneath, kept for the stack it carries.</param>
    public MapFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
