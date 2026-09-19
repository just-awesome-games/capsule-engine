using NVorbis;

namespace Capsule.Runtime.Audio;

// An Ogg Vorbis file decoded as it plays. The reader belongs to the streaming worker for the voice's
// whole life, so nothing here locks and nothing else touches it.
internal sealed class VorbisPcmSource(VorbisReader reader) : IPcmSource
{
    public int Channels => reader.Channels;

    public int SampleRate => reader.SampleRate;

    public void SeekTo(long frame) => reader.SeekTo(frame);

    public int Read(Span<float> target) => reader.ReadSamples(target);

    public void Dispose() => reader.Dispose();
}
