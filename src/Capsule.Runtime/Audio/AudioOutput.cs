namespace Capsule.Runtime.Audio;

/// <summary>
/// The device the host's finished mix plays on, as a platform opens it through
/// <see cref="HostPlatform.OpenAudioOutput"/>.
/// </summary>
/// <remarks>
/// The host mixes every voice itself and hands the output interleaved stereo frames at
/// <see cref="SampleRate"/>. Only the host's mix thread reads <see cref="QueuedFrames"/> and calls
/// <see cref="Submit"/>. A platform whose device pulls frames through a callback keeps its own queue
/// behind <see cref="Submit"/>. Following the system's default device as it changes is the output's own
/// job, and an output left with no device to follow reports it through <see cref="Connected"/>. The host
/// disposes the output on the game thread once its mix thread has stopped.
/// </remarks>
public abstract class AudioOutput : IDisposable
{
    /// <summary>The frames per second of every frame the host submits.</summary>
    protected internal const int SampleRate = 48_000;

    /// <summary>The samples in one frame the host submits, left then right.</summary>
    protected internal const int Channels = 2;

    /// <summary>The frames handed to <see cref="Submit"/> and not yet played.</summary>
    protected internal abstract int QueuedFrames { get; }

    /// <summary>The frames the device takes from the queue at a time, or 0 when the platform cannot tell.</summary>
    /// <remarks>
    /// Read on the game thread as the host starts mixing into the output. The host keeps at least one and a
    /// half of it queued, whatever lead the game asked for.
    /// </remarks>
    protected internal virtual int DeviceFrames => 0;

    /// <summary>Whether the output still reaches a device.</summary>
    /// <remarks>
    /// Read on the mix thread and the game thread. While it reads false the host mixes into nothing on
    /// the clock and stops reading <see cref="QueuedFrames"/>, and it reopens the output through
    /// <see cref="HostPlatform.OpenAudioOutput"/> on an interval. A platform whose device cannot be lost
    /// keeps the default of true.
    /// </remarks>
    protected internal virtual bool Connected => true;

    /// <summary>Queues interleaved frames to play after everything already queued.</summary>
    /// <remarks>
    /// The span is the host's and is reused once the call returns. The host never queues more than a
    /// short lead ahead of what the device has played, and passes values above 1 through unclipped.
    /// </remarks>
    /// <param name="frames">Whole frames of <see cref="Channels"/> samples each.</param>
    protected internal abstract void Submit(ReadOnlySpan<float> frames);

    /// <summary>
    /// Raises the calling thread's scheduling priority above the system's ordinary work. Does nothing by
    /// default.
    /// </summary>
    /// <remarks>
    /// Called once on each host mix thread as it starts, before its first <see cref="Submit"/>. A
    /// platform that cannot raise it leaves it as it was. Must not throw.
    /// </remarks>
    protected internal virtual void RaiseMixThreadPriority()
    {
    }

    /// <inheritdoc/>
    public abstract void Dispose();
}
