namespace Capsule.Rendering;

/// <summary>Whether a run of text draws as written, in capitals or in small letters.</summary>
public enum TextCase
{
    /// <summary>Each character draws as written. The default.</summary>
    AsWritten,

    /// <summary>Each character draws as its capital.</summary>
    Upper,

    /// <summary>Each character draws as its small letter.</summary>
    Lower,
}
