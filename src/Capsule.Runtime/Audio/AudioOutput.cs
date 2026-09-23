using System.Diagnostics.CodeAnalysis;

namespace Capsule.Runtime.Audio;

/// <summary>
/// The output the sound device plays on, as a platform module attaches to it through
/// <see cref="HostPlatform.WatchDefaultAudioOutput"/>. Only the engine calls these members, on the
/// game thread.
/// </summary>
/// <remarks>
/// Disposing detaches from the output and stops the change callback. The host disposes it before
/// the device closes.
/// </remarks>
public abstract class AudioOutput : IDisposable
{
    /// <summary>
    /// Whether the output is still attached to a system device. An output the system pulled, such
    /// as a headset switched off, plays into nothing until reopened.
    /// </summary>
    /// <remarks>Polled on an interval, not per frame.</remarks>
    protected internal abstract bool Connected { get; }

    /// <summary>The system's name for the device the output is on, for the log.</summary>
    protected internal abstract string Name { get; }

    /// <summary>
    /// Moves the output to the system's current default with every voice still playing, and
    /// reconnects a disconnected one. On failure the output stays where it was.
    /// </summary>
    /// <remarks>The host retries on an interval.</remarks>
    /// <param name="reason">Why the move failed in the platform's own words, or null when it succeeded.</param>
    /// <returns>Whether the output now plays on the system's default.</returns>
    protected internal abstract bool TryReopen([NotNullWhen(false)] out string? reason);

    /// <inheritdoc/>
    public abstract void Dispose();
}
