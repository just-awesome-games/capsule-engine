using Capsule.Audio;

namespace Capsule.Runtime.Audio;

// One bus on the mix thread: the buffer its voices sum into, its effects, and the gain it applies after
// them. The game thread builds it and hands it over in a Buses command, and from then on only the mix
// thread touches it. It neither allocates nor locks.
//
// A block runs the echo, the high-pass, the low-pass and the gain, in that order. Whatever a command
// changes ramps across the next block from where the last block ended: the gain and the echo's volume
// and feedback linearly, a cutoff through its filter's coefficient.
internal sealed class MixBus
{
    // A recursive value below this, some 300 dB under full scale, is set to exactly 0. Left alone it would
    // decay into the denormal range, where arithmetic runs many times slower, or hold a limit cycle that
    // never lets the echo go idle.
    private const float Floor = 1e-15f;

    // A damping of root 2 gives 12 dB per octave with no peak at the cutoff.
    private const float Damping = 1.41421356f;

    private Filter _highPass = new(high: true);
    private Filter _lowPass = new(high: false);

    // The echo sounding, and the one a changed delay replaced, which fades out over VoiceMixer.FadeFrames.
    private Echo _echo = new();
    private Echo _retiring = new();

    // The gain the last block ended on, and the one the next block ramps to.
    private float _blockGain = 1f;
    private float _gain = 1f;

    internal float[] Buffer { get; } = new float[VoiceMixer.BlockFrames * VoiceMixer.Channels];

    // Whether the voices on this bus go unheard this block, which the mixer settles before rendering them.
    internal bool Quiet { get; set; }

    // Whether the bus's echo still has repeats to sound.
    internal bool Ringing => (_echo.Line is not null && !_echo.Idle) || _retiring.Line is not null;

    // Whether the next block is silent whatever the voices on the bus play. They need only keep time.
    internal bool Muted => _blockGain == 0f && _gain == 0f && !Ringing;

    internal void SetGain(float gain) => _gain = gain;

    // A line hands the echo a new delay. With none, a delay of 0 drops the echo and any other keeps it.
    internal void SetEffects(in EffectLevels levels, float[]? line)
    {
        _highPass.To = levels.HighPassHertz;
        _lowPass.To = levels.LowPassHertz;

        if (line is not null || (levels.EchoDelaySeconds == 0f && _echo.Line is not null))
        {
            // An idle echo holds nothing, as one started since the last block does not. It is replaced where
            // it stands, and the echo already fading out keeps fading.
            if (_echo.Line is not null && !_echo.Idle)
            {
                (_echo, _retiring) = (_retiring, _echo);
                _retiring.Retire();
            }

            _echo.Start(line, in levels);
        }

        _echo.Retarget(in levels);
    }

    // Runs the bus's effects and gain over the first frames of samples, in place.
    internal void Process(Span<float> samples, int frames)
    {
        if (_echo.Line is not null)
        {
            if (_echo.Idle && IsSilent(samples))
            {
                _echo.Skip();
            }
            else
            {
                _echo.Process(samples, frames);
            }
        }

        if (_retiring.Line is not null)
        {
            _retiring.Fade(samples, frames);
        }

        RunFilter(ref _highPass, samples, frames);
        RunFilter(ref _lowPass, samples, frames);
        ApplyGain(samples, frames);
    }

    private static float Flush(float value) => MathF.Abs(value) < Floor ? 0f : value;

    private static bool IsSilent(ReadOnlySpan<float> samples) => samples.IndexOfAnyExcept(0f) < 0;

    // A filter whose state has settled to 0 passes silence as silence, and skips it.
    private static void RunFilter(ref Filter filter, Span<float> samples, int frames)
    {
        if (filter.Settled && IsSilent(samples))
        {
            filter.Skip();

            return;
        }

        if (!filter.Begin(frames, samples[0], samples[1]))
        {
            return;
        }

        for (int at = 0; at < samples.Length; at += VoiceMixer.Channels)
        {
            filter.Step(ref samples[at], ref samples[at + 1]);
        }
    }

    private void ApplyGain(Span<float> samples, int frames)
    {
        float from = _blockGain;
        float to = _gain;
        _blockGain = to;

        if (from == 1f && to == 1f)
        {
            return;
        }

        if (from == 0f && to == 0f)
        {
            samples.Clear();

            return;
        }

        float slope = (to - from) / frames;
        for (int frame = 0; frame < frames; frame++)
        {
            float gain = from + (slope * frame);
            int at = VoiceMixer.Channels * frame;
            samples[at] *= gain;
            samples[at + 1] *= gain;
        }
    }

    // A 12 dB per octave state-variable filter over both channels, in the trapezoidal form, which stays
    // stable while its cutoff moves. The cutoff ramps across each block through the filter's coefficient.
    // At its off end the filter passes the signal untouched and runs nothing. Neither off end is exactly
    // the dry signal. The block that lands there crossfades from the filtered output to the dry input.
    private struct Filter(bool high)
    {
        // The cutoff the next block ramps to, in hertz.
        internal float To = high ? AudioEffects.LowestHertz : AudioEffects.HighestHertz;

        private readonly bool _high = high;
        private readonly float _off = high ? AudioEffects.LowestHertz : AudioEffects.HighestHertz;

        // The cutoff the last block ended on.
        private float _from = high ? AudioEffects.LowestHertz : AudioEffects.HighestHertz;

        // Whether the last block ran the filter, which leaves its state live.
        private bool _running;

        // Frames of the block that lands at the off end, and how many have been crossfaded toward dry. 0
        // frames means the block does not land.
        private int _landingFrames;
        private int _landed;

        // The two integrators of each channel.
        private float _left1;
        private float _left2;
        private float _right1;
        private float _right2;

        // The prewarped cutoff this frame, its step per frame within the block, and the coefficients it gives.
        private float _g;
        private float _gStep;
        private float _a1;
        private float _a2;
        private float _a3;

        internal readonly bool Settled => _left1 == 0f && _left2 == 0f && _right1 == 0f && _right2 == 0f;

        // Readies a block of frames whose first frame is left and right. Returns whether the filter runs. A
        // low-pass coming out of its bypass starts settled on that frame, not from silence, or it would click.
        internal bool Begin(int frames, float left, float right)
        {
            if (_from == _off && To == _off)
            {
                this = new Filter(_high);

                return false;
            }

            if (!_running && !_high)
            {
                (_left2, _right2) = (left, right);
            }

            _running = true;
            _landingFrames = To == _off ? frames : 0;
            _landed = 0;
            float from = Prewarp(_from);
            float to = Prewarp(To);
            _from = To;
            _gStep = (to - from) / frames;
            _g = from;
            Coefficients(_gStep == 0f ? to : from);

            return true;
        }

        // A block it does not run: the cutoff lands where the block would have taken it.
        internal void Skip() => _from = To;

        internal void Step(ref float left, ref float right)
        {
            if (_gStep != 0f)
            {
                _g += _gStep;
                Coefficients(_g);
            }

            float dryLeft = left;
            float dryRight = right;
            left = Tick(left, ref _left1, ref _left2);
            right = Tick(right, ref _right1, ref _right2);

            if (_landingFrames > 0)
            {
                // Exactly dry on the block's last frame, which the next block's bypass continues.
                float dry = (float)++_landed / _landingFrames;
                left += (dryLeft - left) * dry;
                right += (dryRight - right) * dry;

                // Landed, the filter is exactly as bypass leaves it. A target leaving the off end in the
                // next block then starts from dry as any activation does, not from the held state.
                if (_landed == _landingFrames)
                {
                    this = new Filter(_high) { To = To };
                }
            }
        }

        private static float Prewarp(float hertz) => MathF.Tan(MathF.PI * hertz / VoiceMixer.SampleRate);

        private void Coefficients(float g)
        {
            _a1 = 1f / (1f + (g * (g + Damping)));
            _a2 = g * _a1;
            _a3 = g * _a2;
        }

        private readonly float Tick(float input, ref float band, ref float low)
        {
            float v3 = input - low;
            float v1 = (_a1 * band) + (_a2 * v3);
            float v2 = low + (_a2 * band) + (_a3 * v3);
            band = Flush((2f * v1) - band);
            low = Flush((2f * v2) - low);

            return _high ? input - (Damping * v1) - v2 : v2;
        }
    }

    // A delay line the bus's sum is written into, read back after the delay through a low cut and a high
    // cut, and fed back. Each repeat passes the cuts again, and every value written to the line is flushed.
    private sealed class Echo
    {
        private Filter _lowCut = new(high: true);
        private Filter _highCut = new(high: false);

        private int _frames;
        private int _position;

        // Frames written in a row that were exactly 0, up to the line's length. At that length the line
        // holds nothing.
        private int _quiet;

        // The values the last block ended on, and the ones the next block ramps to.
        private float _volume;
        private float _volumeTo;
        private float _feedback;
        private float _feedbackTo;

        // The sign of the repeats' left side, -1 when inverted. A change swings through 0 across one block.
        private float _left = 1f;
        private float _leftTo = 1f;

        // The frames of a retired echo's fade still to play.
        private int _fade;

        // Interleaved stereo, or null for no echo.
        internal float[]? Line { get; private set; }

        // Whether the line and the cuts hold nothing. Silence in then gives silence out.
        internal bool Idle => _quiet >= _frames && _lowCut.Settled && _highCut.Settled;

        internal void Start(float[]? line, in EffectLevels levels)
        {
            Line = line;
            _frames = line is null ? 0 : line.Length / VoiceMixer.Channels;
            _position = 0;
            _quiet = _frames;
            _lowCut = new Filter(high: true);
            _highCut = new Filter(high: false);
            _volume = levels.EchoVolume;
            _feedback = levels.EchoFeedback;
            _left = SideOf(in levels);
        }

        internal void Retarget(in EffectLevels levels)
        {
            _volumeTo = levels.EchoVolume;
            _feedbackTo = levels.EchoFeedback;
            _leftTo = SideOf(in levels);
            _lowCut.To = levels.EchoLowCutHertz;
            _highCut.To = levels.EchoHighCutHertz;
        }

        // Fades out from the volume the last block ended on. A target set since was never heard.
        internal void Retire() => _fade = VoiceMixer.FadeFrames;

        internal void Skip()
        {
            _volume = _volumeTo;
            _feedback = _feedbackTo;
            _left = _leftTo;
            _lowCut.Skip();
            _highCut.Skip();
        }

        internal void Process(Span<float> samples, int frames)
        {
            float[] line = Line!;
            int read = VoiceMixer.Channels * _position;
            bool lowCut = _lowCut.Begin(frames, line[read], line[read + 1]);
            bool highCut = _highCut.Begin(frames, line[read], line[read + 1]);
            float volumeSlope = (_volumeTo - _volume) / frames;
            float leftVolume = _volume * _left;
            float leftSlope = ((_volumeTo * _leftTo) - leftVolume) / frames;
            float feedbackSlope = (_feedbackTo - _feedback) / frames;

            for (int frame = 0; frame < frames; frame++)
            {
                int at = VoiceMixer.Channels * _position;
                float left = line[at];
                float right = line[at + 1];
                Cut(ref left, ref right, lowCut, highCut);

                int sample = VoiceMixer.Channels * frame;
                float feedback = _feedback + (feedbackSlope * frame);
                float writeLeft = Flush(samples[sample] + (feedback * left));
                float writeRight = Flush(samples[sample + 1] + (feedback * right));
                line[at] = writeLeft;
                line[at + 1] = writeRight;
                _quiet = writeLeft != 0f || writeRight != 0f ? 0 : Math.Min(_quiet + 1, _frames);

                samples[sample] += (leftVolume + (leftSlope * frame)) * left;
                samples[sample + 1] += (_volume + (volumeSlope * frame)) * right;

                if (++_position == _frames)
                {
                    _position = 0;
                }
            }

            _volume = _volumeTo;
            _feedback = _feedbackTo;
            _left = _leftTo;
        }

        // A retired echo's last repeats, read on and faded out to exactly 0, after which it lets its line go.
        internal void Fade(Span<float> samples, int frames)
        {
            float[] line = Line!;
            int read = VoiceMixer.Channels * _position;
            bool lowCut = _lowCut.Begin(frames, line[read], line[read + 1]);
            bool highCut = _highCut.Begin(frames, line[read], line[read + 1]);

            for (int frame = 0; frame < frames && _fade > 0; frame++)
            {
                int at = VoiceMixer.Channels * _position;
                float left = line[at];
                float right = line[at + 1];
                Cut(ref left, ref right, lowCut, highCut);

                float envelope = _volume * --_fade / VoiceMixer.FadeFrames;
                int sample = VoiceMixer.Channels * frame;
                samples[sample] += envelope * _left * left;
                samples[sample + 1] += envelope * right;

                if (++_position == _frames)
                {
                    _position = 0;
                }
            }

            if (_fade == 0)
            {
                Line = null;
            }
        }

        private static float SideOf(in EffectLevels levels) => levels.EchoInvertLeft ? -1f : 1f;

        private void Cut(ref float left, ref float right, bool lowCut, bool highCut)
        {
            if (lowCut)
            {
                _lowCut.Step(ref left, ref right);
            }

            if (highCut)
            {
                _highCut.Step(ref left, ref right);
            }
        }
    }
}
