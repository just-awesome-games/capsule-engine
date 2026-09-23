namespace Capsule.Scenes.Documents;

/// <summary>A scene document is malformed. The message states the defect and the fix when there is one.</summary>
public sealed class SceneDocumentFormatException : Exception
{
    internal SceneDocumentFormatException(string message)
        : base(message)
    {
    }

    internal SceneDocumentFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
