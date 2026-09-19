namespace Capsule.Scenes.Documents;

/// <summary>A scene document is malformed. The message states the defect and the fix when there is one.</summary>
public sealed class SceneDocumentFormatException : Exception
{
    /// <param name="message">The defect, and the fix when there is one.</param>
    public SceneDocumentFormatException(string message)
        : base(message)
    {
    }

    /// <param name="message">The defect, and the fix when there is one.</param>
    /// <param name="innerException">The underlying parse failure, kept for its stack trace.</param>
    public SceneDocumentFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
