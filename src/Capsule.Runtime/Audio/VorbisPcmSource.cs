using Capsule.Runtime.Audio.Vorbis;

namespace Capsule.Runtime.Audio;

// An Ogg Vorbis file decoded as it plays. The reader belongs to the streaming worker for the voice's
// whole life, so nothing here locks and nothing else touches it.
//
// The decoder is NVorbis vendored under Audio/Vorbis/ and patched at its `// Capsule:` sites, because
// the released package allocates on every packet (D-capsule-134). It leaves the moment a release does
// not: delete Audio/Vorbis/, restore the NVorbis PackageReference in Capsule.Runtime.csproj at that
// release, return this file's and SoundDevice's using to `NVorbis`, drop the notices section, and
// keep VorbisAllocationTests as the proof. MonoGame carries its own NVorbis for Song either way.
internal sealed class VorbisPcmSource(VorbisReader reader) : IPcmSource
{
    public int Channels => reader.Channels;

    public int SampleRate => reader.SampleRate;

    public void SeekTo(long frame) => reader.SeekTo(frame);

    public int Read(Span<float> target) => reader.ReadSamples(target);

    public void Dispose() => reader.Dispose();
}
