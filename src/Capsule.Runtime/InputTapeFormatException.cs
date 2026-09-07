namespace Capsule.Runtime;

/// <summary>
/// A tape file is not one Capsule wrote, is of a version this engine does not read, or ends
/// mid-step. The message states which.
/// </summary>
public sealed class InputTapeFormatException : Exception
{
    /// <summary>Creates the exception with the runtime's own default message.</summary>
    public InputTapeFormatException()
    {
    }

    /// <param name="message">The defect, and where one exists the fix.</param>
    public InputTapeFormatException(string message)
        : base(message)
    {
    }

    /// <param name="message">The defect, and where one exists the fix.</param>
    /// <param name="innerException">The read failure underneath, kept for the stack it carries.</param>
    public InputTapeFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
