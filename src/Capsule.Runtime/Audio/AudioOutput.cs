using System.Diagnostics.CodeAnalysis;

namespace Capsule.Runtime.Audio;

/// <summary>
/// The output the sound device plays on, which a platform module attaches to through
/// <see cref="HostPlatform.WatchDefaultAudioOutput"/>: whether it is still connected, what the system
/// calls it, and moving it to the system's current default output with every voice still playing.
/// Every member is called on the game thread. Disposing detaches from the output and stops the change
/// callback, and the host disposes it before the device closes.
/// </summary>
public abstract class AudioOutput : IDisposable
{
    /// <summary>
    /// Whether the output is still attached to a system device. An output the system pulled, such as a
    /// headset switched off, plays into nothing until reopened. Polled on an interval, not per frame.
    /// </summary>
    public abstract bool Connected { get; }

    /// <summary>The system's name for the device the output is on, for the log.</summary>
    public abstract string Name { get; }

    /// <summary>
    /// Moves the output to the system's current default, keeping every source and buffer, and
    /// reconnects a disconnected one. On failure the output stays where it was and
    /// <paramref name="reason"/> says why in the platform's own words. The host retries on an
    /// interval.
    /// </summary>
    /// <param name="reason">Why the move failed, or null when it succeeded.</param>
    public abstract bool TryReopen([NotNullWhen(false)] out string? reason);

    /// <inheritdoc/>
    public abstract void Dispose();
}
