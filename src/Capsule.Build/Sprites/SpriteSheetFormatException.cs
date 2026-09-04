namespace Capsule.Build.Sprites;

/// <summary>
/// A sprite sheet document is malformed. The message states the defect and, where one exists, the
/// fix.
/// </summary>
internal sealed class SpriteSheetFormatException : Exception
{
    internal SpriteSheetFormatException(string message)
        : base(message)
    {
    }

    internal SpriteSheetFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
