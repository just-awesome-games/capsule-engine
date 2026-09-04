namespace Capsule.Build.Sprites;

/// <summary>
/// A sprite sheet document is malformed. The message states the defect and, where one exists, the
/// fix.
/// </summary>
public sealed class SpriteSheetFormatException : Exception
{
    /// <summary>Creates the exception with the runtime's own default message.</summary>
    public SpriteSheetFormatException()
    {
    }

    /// <param name="message">The defect, and where one exists the fix.</param>
    public SpriteSheetFormatException(string message)
        : base(message)
    {
    }

    /// <param name="message">The defect, and where one exists the fix.</param>
    /// <param name="innerException">The parse failure underneath, kept for the stack it carries.</param>
    public SpriteSheetFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
