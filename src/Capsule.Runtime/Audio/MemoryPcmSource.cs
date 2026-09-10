namespace Capsule.Runtime.Audio;

// A cursor over one resident clip's decoded samples: what a streamed voice reads when the clip is
// already in memory. Owns nothing but a position and the clip it points at, so several voices read
// one clip at once and letting go of one frees neither the samples nor the others.
//
// The cursor belongs to the voice, like the loop reader over it: a voice played again is pointed at
// the new clip's samples in place rather than handed another cursor.
internal sealed class MemoryPcmSource : IPcmSource
{
    private PcmAudio _samples = PcmAudio.None;
    private int _offset;

    internal MemoryPcmSource(PcmAudio samples) => Arm(samples);

    public int Channels => _samples.Channels;

    public int SampleRate => _samples.SampleRate;

    public long Frames => _samples.Samples.Length / _samples.Channels;

    // Points this cursor at samples, from their start.
    internal void Arm(PcmAudio samples)
    {
        _samples = samples;
        _offset = 0;
    }

    public void SeekTo(long frame) =>
        _offset = (int)Math.Clamp(frame * _samples.Channels, 0, _samples.Samples.Length);

    public int Read(Span<float> target)
    {
        ReadOnlySpan<float> samples = _samples.Samples;
        int read = Math.Min(target.Length, samples.Length - _offset);
        samples.Slice(_offset, read).CopyTo(target);
        _offset += read;

        return read;
    }

    // Lets go of the clip without giving up the cursor: a pooled voice still pointing at a retired
    // clip would keep it decoded for the rest of the run.
    public void Dispose() => Arm(PcmAudio.None);
}
