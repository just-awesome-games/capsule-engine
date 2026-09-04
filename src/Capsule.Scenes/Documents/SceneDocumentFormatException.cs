namespace Capsule.Scenes.Documents;

/// <summary>A scene document is malformed. The message states the defect and, where one exists, the fix.</summary>
public sealed class SceneDocumentFormatException : Exception
{
    /// <summary>Creates the exception with the runtime's own default message.</summary>
    public SceneDocumentFormatException()
    {
    }

    /// <param name="message">The defect, and where one exists the fix.</param>
    public SceneDocumentFormatException(string message)
        : base(message)
    {
    }

    /// <param name="message">The defect, and where one exists the fix.</param>
    /// <param name="innerException">The parse failure underneath, kept for the stack it carries.</param>
    public SceneDocumentFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
