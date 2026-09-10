namespace Capsule.Runtime.Audio;

// Interleaved samples read in order, with a seek: what a streamed voice decodes through. Two
// implementations — an Ogg Vorbis file decoded as it plays, and a resident clip's samples already in
// memory — so the loop reader over it is exercised with neither a file nor a device.
internal interface IPcmSource : IDisposable
{
    int Channels { get; }

    int SampleRate { get; }

    // Frames the source holds, one frame being one sample per channel.
    long Frames { get; }

    // Positions the next read at this frame.
    void SeekTo(long frame);

    // Fills what it can of target from the current position and answers how many interleaved samples
    // it wrote; 0 at the end of the source. A short read is not the end.
    int Read(Span<float> target);
}
