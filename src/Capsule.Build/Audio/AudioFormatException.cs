namespace Capsule.Build.Audio;

/// <summary>
/// An audio source cannot be measured: its container is malformed, truncated, or of a shape Capsule
/// does not ship. The message states the defect and, where one exists, the fix.
/// </summary>
internal sealed class AudioFormatException : Exception
{
    internal AudioFormatException(string message)
        : base(message)
    {
    }

    internal AudioFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
