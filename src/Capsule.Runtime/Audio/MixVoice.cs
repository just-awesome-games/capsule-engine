namespace Capsule.Runtime.Audio;

// One voice inside the mixer: its source, its cursor, the four-frame window the spline reads, its
// per-channel gains and the fade that starts, stops, pauses and resumes it without a click. Only the
// mix thread touches it.
//
// The cursor is 32.32 fixed point. Between output frames its integer part is 0 and its fraction is how
// far the output has moved from the window's second frame toward its third. Adding the step and
// shifting the window by the integer part reproduces exactly what one long-running cursor would, with
// no drift and nothing to overflow.
//
// Source frames are fetched in order into the window. A resident loop's fetch position jumps from the
// region's end back to its start, and a stream's worker has already joined its loop. Either way the
// window always holds the frames the voice actually plays, which keeps the taps around a loop seam
// continuous. A frame before sample 0, or past the end of a one-shot or of a spent stream, reads 0.
internal sealed class MixVoice
{
    private const long One = 1L << 32;
    private const long FractionMask = One - 1;
    private const float FractionScale = 1f / One;

    // A step outside 10 octaves of unit pitch is clamped there. A game's arithmetic never stalls or
    // races the mix.
    private const double MinPitch = 1.0 / 1024.0;
    private const double MaxPitch = 1024.0;

    private const float QuarterPi = MathF.PI / 4f;
    private const float Sqrt2 = 1.41421356f;

    // The fetch position for a voice that never wraps.
    private const long NoLoop = long.MaxValue;

    // Frames a voice needs to have fetched past its source's end before all four taps read 0.
    private const int Window = 4;

    private short[]? _pcm16;
    private float[]? _float;
    private AudioStream? _stream;
    private int _channels;
    private int _rate;
    private long _frames;

    // The next source frame a resident voice fetches, and the region its fetch position repeats.
    private long _read;
    private long _loopStart;
    private long _loopEnd;

    // Frames fetched past the end. The voice has ended once a whole window has been.
    private int _pastEnd;

    private long _cursor;
    private long _step;

    // The window: frames at the cursor's integer position minus 1, 0, +1 and +2, left then right. A
    // mono voice reads only the left.
    private float _l0;
    private float _l1;
    private float _l2;
    private float _l3;
    private float _r0;
    private float _r1;
    private float _r2;
    private float _r3;

    private float _gain;
    private float _pan;

    // The per-channel gain the last block ended on, which the next block ramps from.
    private float _left;
    private float _right;

    // Out of VoiceMixer.FadeFrames. Full while sounding, 0 while held, and between the two in a fade.
    private int _envelope;
    private State _state;

    // Whether the window holds the voice's first frames, which happens on its first block.
    private bool _primed;

    // A stream's position for the block being mixed: what the mixer has read, what the worker had
    // written when the block began, and whether the worker had finished by then.
    private long _streamRead;
    private long _streamWritten;
    private bool _streamEnded;

    private enum State : byte
    {
        Sounding,
        FadingIn,

        // A pause's fade, which ends held.
        FadingToHold,

        // A stop's fade, which ends the voice.
        FadingToEnd,

        // Paused and silent. A held voice neither sounds nor advances.
        Held,
    }

    private interface IFrameSource
    {
        static abstract bool Stereo { get; }

        static abstract void Fetch(MixVoice voice, out float left, out float right);

        static abstract void Skip(MixVoice voice, long frames);
    }

    internal bool Live { get; private set; }

    internal int Slot { get; private set; }

    internal int Generation { get; private set; }

    internal int Bus { get; private set; }

    // Whether the voice repeats. A loop has no end of its own to play out to.
    internal bool Loops { get; private set; }

    internal bool Held => _state == State.Held;

    // Whether the last block found a stream's ring empty before its end.
    internal bool Starved { get; private set; }

    internal AudioStream? Stream => _stream;

    internal bool Pausing => _state == State.FadingToHold;

    // Whether ending the voice now would cut off sound that was heard, which a fade avoids.
    internal bool Audible => Live && _primed && !Held && _envelope > 0 && (_left != 0f || _right != 0f || HasTargetGain);

    private bool HasTargetGain
    {
        get
        {
            TargetGains(out float left, out float right);

            return left != 0f || right != 0f;
        }
    }

    private bool FadingOut => _state is State.FadingToHold or State.FadingToEnd;

    internal void Start(in MixCommand play)
    {
        Live = true;
        Slot = play.Slot;
        Generation = play.Generation;
        Bus = play.Bus;
        Loops = play.Loop;
        _primed = false;
        _pastEnd = 0;
        _cursor = 0;

        if (play.Stream is { } stream)
        {
            _stream = stream;
            _channels = stream.Channels;
            _rate = stream.SampleRate;
            _streamRead = stream.ReadPosition;
            _loopEnd = NoLoop;
        }
        else
        {
            PcmAudio samples = play.Samples!;
            _pcm16 = samples.Pcm16;
            _float = samples.Float;
            _channels = samples.Channels;
            _rate = samples.SampleRate;
            _frames = samples.Frames;

            long start = play.StartFrame;
            _loopStart = 0;
            _loopEnd = NoLoop;
            if (play.Loop)
            {
                _loopEnd = Math.Clamp(play.LoopEnd, 1, Math.Max(1, _frames));
                _loopStart = Math.Clamp(play.LoopStart, 0, _loopEnd - 1);
                if (start >= _loopEnd)
                {
                    start = _loopStart + ((start - _loopEnd) % (_loopEnd - _loopStart));
                }
            }

            // The window opens on the frame before the start, which a start at 0 reads as silence.
            _read = start - 1;
        }

        SetPitch(play.Pitch);
        _gain = play.Gain;
        _pan = play.Pan;
        TargetGains(out _left, out _right);

        // An authored attack stays sharp. Only a start mid-clip, which cuts into a waveform, fades in.
        _state = play.Held ? State.Held : play.StartFrame > 0 ? State.FadingIn : State.Sounding;
        _envelope = _state == State.Sounding ? VoiceMixer.FadeFrames : 0;
    }

    internal void SetGain(float gain) => _gain = gain;

    internal void SetPan(float pan) => _pan = pan;

    internal void SetPitch(float pitch)
    {
        double rate = Math.Clamp(double.IsNaN(pitch) ? 1.0 : pitch, MinPitch, MaxPitch);
        _step = (long)Math.Round(rate * _rate / VoiceMixer.SampleRate * One);
    }

    // Fades the voice out and holds it. A voice that has rendered nothing yet, or is silent, holds at
    // once. Fading it would sound a fresh voice's first frames, and passing a silent block would spend
    // time the game holds. A resume fades it in from 0.
    internal void Pause()
    {
        if (Held)
        {
            return;
        }

        if (!Audible)
        {
            _state = State.Held;
            _envelope = 0;

            return;
        }

        _state = State.FadingToHold;
    }

    // Fades a held voice in, or turns back a pause still fading out.
    internal void Resume()
    {
        if (_state is State.Held or State.FadingToHold)
        {
            _state = State.FadingIn;
        }
    }

    // Fades the voice out to its end. The caller checked Audible.
    internal void Stop() => _state = State.FadingToEnd;

    // Lets go of the source. The voice is free for the next play.
    internal void Clear()
    {
        Live = false;
        _pcm16 = null;
        _float = null;
        _stream = null;
    }

    // Adds the next frames of this voice into bus, interleaved stereo. Returns whether the voice ended
    // in them, by running out or by a stop's fade reaching 0. Gain and pan changes since the last
    // block ramp linearly across this one.
    internal bool Render(Span<float> bus, int frames)
    {
        Starved = false;
        if (Held)
        {
            return false;
        }

        if (_stream is { } stream)
        {
            // The end is read before the count. A seen end then covers every sample.
            _streamEnded = stream.Ended;
            _streamWritten = stream.WrittenSamples;
        }

        bool ended = (_stream, _float, _channels) switch
        {
            (null, null, 1) => Render<Pcm16Mono>(bus, frames),
            (null, null, _) => Render<Pcm16Stereo>(bus, frames),
            (null, _, 1) => Render<FloatMono>(bus, frames),
            (null, _, _) => Render<FloatStereo>(bus, frames),
            (_, _, 1) => Render<StreamMono>(bus, frames),
            _ => Render<StreamStereo>(bus, frames),
        };

        if (_stream is { } read)
        {
            read.Consume(_streamRead);

            // An end the worker published mid-block, with nothing written after the block began, is the
            // stream running out, not an underrun.
            Starved &= !(read.Ended && read.WrittenSamples == _streamWritten);
        }

        return ended;
    }

    // The 4-point Catmull-Rom spline through p1 and p2 at t in [0, 1).
    private static float Spline(float p0, float p1, float p2, float p3, float t) =>
        p1 + (0.5f * t * (p2 - p0 + (t * ((2f * p0) - (5f * p1) + (4f * p2) - p3 + (t * ((3f * (p1 - p2)) + p3 - p0))))));

    private bool Render<TSource>(Span<float> bus, int frames)
        where TSource : struct, IFrameSource
    {
        if (!_primed)
        {
            Prime<TSource>();
        }

        float left = _left;
        float right = _right;
        TargetGains(out float targetLeft, out float targetRight);
        _left = targetLeft;
        _right = targetRight;

        // Not mixed, but the cursor and any fade still run. The voice resumes where its time says.
        if (left == 0f && right == 0f && targetLeft == 0f && targetRight == 0f)
        {
            return AdvanceSilently<TSource>(frames);
        }

        float leftSlope = (targetLeft - left) / frames;
        float rightSlope = (targetRight - right) / frames;

        for (int frame = 0; frame < frames; frame++)
        {
            float envelope = _state == State.Sounding ? 1f : StepEnvelope();

            float sampleLeft;
            float sampleRight;
            if ((_cursor & FractionMask) == 0)
            {
                // On a source frame exactly. A unit step at a whole-frame start passes every sample
                // through untouched.
                sampleLeft = _l1;
                sampleRight = TSource.Stereo ? _r1 : sampleLeft;
            }
            else
            {
                float t = (_cursor & FractionMask) * FractionScale;
                sampleLeft = Spline(_l0, _l1, _l2, _l3, t);
                sampleRight = TSource.Stereo ? Spline(_r0, _r1, _r2, _r3, t) : sampleLeft;
            }

            int at = VoiceMixer.Channels * frame;
            bus[at] += sampleLeft * ((left + (leftSlope * frame)) * envelope);
            bus[at + 1] += sampleRight * ((right + (rightSlope * frame)) * envelope);

            _cursor += _step;
            Shift<TSource>(_cursor >> 32);
            _cursor &= FractionMask;

            if (_pastEnd >= Window)
            {
                return true;
            }

            if (_envelope == 0 && FadingOut)
            {
                return FinishFadeOut();
            }
        }

        return false;
    }

    // A silent block: the cursor moves as far as the frames would have moved it, in one shift, and a
    // fade runs on unheard.
    private bool AdvanceSilently<TSource>(int frames)
        where TSource : struct, IFrameSource
    {
        long cursor = _cursor + (_step * frames);
        Shift<TSource>(cursor >> 32);
        _cursor = cursor & FractionMask;

        if (FadingOut)
        {
            _envelope = Math.Max(0, _envelope - frames);
            if (_envelope == 0)
            {
                return _pastEnd >= Window || FinishFadeOut();
            }
        }
        else if (_state == State.FadingIn)
        {
            _envelope = Math.Min(VoiceMixer.FadeFrames, _envelope + frames);
            if (_envelope == VoiceMixer.FadeFrames)
            {
                _state = State.Sounding;
            }
        }

        return _pastEnd >= Window;
    }

    // A fade out has reached 0. Returns whether that ended the voice, as a stop's does. A pause's
    // holds it instead.
    private bool FinishFadeOut()
    {
        if (_state == State.FadingToEnd)
        {
            return true;
        }

        _state = State.Held;

        return false;
    }

    // This frame's envelope. A fade out's last frame is exactly 0, and a fade in's first.
    private float StepEnvelope()
    {
        if (FadingOut)
        {
            _envelope--;

            return (float)_envelope / VoiceMixer.FadeFrames;
        }

        float envelope = (float)_envelope / VoiceMixer.FadeFrames;
        if (++_envelope == VoiceMixer.FadeFrames)
        {
            _state = State.Sounding;
        }

        return envelope;
    }

    private void Prime<TSource>()
        where TSource : struct, IFrameSource
    {
        _primed = true;
        if (_stream is null)
        {
            Shift<TSource>(Window);

            return;
        }

        // A stream's ring starts on its first frame. The frame before it reads 0.
        (_l0, _r0) = (0f, 0f);
        TSource.Fetch(this, out _l1, out _r1);
        TSource.Fetch(this, out _l2, out _r2);
        TSource.Fetch(this, out _l3, out _r3);
    }

    // Moves the window frames ahead.
    private void Shift<TSource>(long frames)
        where TSource : struct, IFrameSource
    {
        if (frames >= Window)
        {
            TSource.Skip(this, frames - Window);
            TSource.Fetch(this, out _l0, out _r0);
            TSource.Fetch(this, out _l1, out _r1);
            TSource.Fetch(this, out _l2, out _r2);
            TSource.Fetch(this, out _l3, out _r3);

            return;
        }

        for (; frames > 0; frames--)
        {
            (_l0, _l1, _l2) = (_l1, _l2, _l3);
            (_r0, _r1, _r2) = (_r1, _r2, _r3);
            TSource.Fetch(this, out _l3, out _r3);
        }
    }

    // The per-channel gains the voice's gain and pan ask for. A mono voice pans at equal power, and its
    // centre is cos(pi/4) on each side. A stereo voice balances, keeping the near side at full and
    // turning the far side down, and its centre is exact unity on each side.
    private void TargetGains(out float left, out float right)
    {
        float pan = Math.Clamp(_pan, -1f, 1f);
        float angle = (pan + 1f) * QuarterPi;
        float cosine = DeterministicMath.Cos(angle);
        float sine = DeterministicMath.Sin(angle);

        if (_channels == 1)
        {
            (left, right) = (Math.Max(0f, cosine), Math.Max(0f, sine));
        }
        else
        {
            // Clamped at 0 as well. The cosine at a hard right pan lands a hair below it.
            left = pan > 0f ? Math.Clamp(Sqrt2 * cosine, 0f, 1f) : 1f;
            right = pan < 0f ? Math.Clamp(Sqrt2 * sine, 0f, 1f) : 1f;
        }

        left *= _gain;
        right *= _gain;
    }

    // Counts frames past the end among count positions ending at position, for a source whose last
    // frame is end - 1.
    private void PassEnd(long position, long count, long end) =>
        _pastEnd = (int)Math.Min(Window, _pastEnd + Math.Max(0, position - Math.Max(position - count, end)));

    // The next resident frame's index, after its loop's jump, or -1 past the end and before the start.
    private long NextResident()
    {
        long frame = _read == _loopEnd ? _loopStart : _read;
        _read = frame + 1;
        if ((ulong)frame < (ulong)_frames)
        {
            return frame;
        }

        if (frame >= _frames)
        {
            _pastEnd = Math.Min(Window, _pastEnd + 1);
        }

        return -1;
    }

    private void SkipResident(long frames)
    {
        long read = _read + frames;
        if (_loopEnd != NoLoop)
        {
            if (read > _loopEnd)
            {
                read = _loopStart + ((read - _loopEnd) % (_loopEnd - _loopStart));
            }
        }
        else
        {
            PassEnd(read, frames, _frames);
        }

        _read = read;
    }

    private void SkipStream(long frames)
    {
        long available = (_streamWritten - _streamRead) / _channels;
        long taken = Math.Min(frames, available);
        _streamRead += taken * _channels;

        if (taken < frames)
        {
            if (_streamEnded)
            {
                _pastEnd = (int)Math.Min(Window, _pastEnd + frames - taken);
            }
            else
            {
                Starved = true;
            }
        }
    }

    // The next streamed sample pair, or false when the ring is empty, which past the stream's end
    // counts toward the voice's end and before it is an underrun.
    private bool NextStream(out int at)
    {
        if (_streamRead < _streamWritten)
        {
            at = (int)_streamRead & (AudioStream.Capacity - 1);
            _streamRead += _channels;

            return true;
        }

        at = 0;
        if (_streamEnded)
        {
            _pastEnd = Math.Min(Window, _pastEnd + 1);
        }
        else
        {
            Starved = true;
        }

        return false;
    }

    private readonly struct Pcm16Mono : IFrameSource
    {
        public static bool Stereo => false;

        public static void Fetch(MixVoice voice, out float left, out float right)
        {
            long frame = voice.NextResident();
            left = frame < 0 ? 0f : voice._pcm16![frame] * PcmAudio.Pcm16Scale;
            right = left;
        }

        public static void Skip(MixVoice voice, long frames) => voice.SkipResident(frames);
    }

    private readonly struct Pcm16Stereo : IFrameSource
    {
        public static bool Stereo => true;

        public static void Fetch(MixVoice voice, out float left, out float right)
        {
            long frame = voice.NextResident();
            if (frame < 0)
            {
                (left, right) = (0f, 0f);

                return;
            }

            short[] samples = voice._pcm16!;
            left = samples[2 * frame] * PcmAudio.Pcm16Scale;
            right = samples[(2 * frame) + 1] * PcmAudio.Pcm16Scale;
        }

        public static void Skip(MixVoice voice, long frames) => voice.SkipResident(frames);
    }

    private readonly struct FloatMono : IFrameSource
    {
        public static bool Stereo => false;

        public static void Fetch(MixVoice voice, out float left, out float right)
        {
            long frame = voice.NextResident();
            left = frame < 0 ? 0f : voice._float![frame];
            right = left;
        }

        public static void Skip(MixVoice voice, long frames) => voice.SkipResident(frames);
    }

    private readonly struct FloatStereo : IFrameSource
    {
        public static bool Stereo => true;

        public static void Fetch(MixVoice voice, out float left, out float right)
        {
            long frame = voice.NextResident();
            if (frame < 0)
            {
                (left, right) = (0f, 0f);

                return;
            }

            float[] samples = voice._float!;
            left = samples[2 * frame];
            right = samples[(2 * frame) + 1];
        }

        public static void Skip(MixVoice voice, long frames) => voice.SkipResident(frames);
    }

    private readonly struct StreamMono : IFrameSource
    {
        public static bool Stereo => false;

        public static void Fetch(MixVoice voice, out float left, out float right)
        {
            left = voice.NextStream(out int at) ? voice._stream!.Ring[at] : 0f;
            right = left;
        }

        public static void Skip(MixVoice voice, long frames) => voice.SkipStream(frames);
    }

    private readonly struct StreamStereo : IFrameSource
    {
        public static bool Stereo => true;

        public static void Fetch(MixVoice voice, out float left, out float right)
        {
            if (!voice.NextStream(out int at))
            {
                (left, right) = (0f, 0f);

                return;
            }

            float[] ring = voice._stream!.Ring;
            left = ring[at];
            right = ring[at + 1];
        }

        public static void Skip(MixVoice voice, long frames) => voice.SkipStream(frames);
    }
}
