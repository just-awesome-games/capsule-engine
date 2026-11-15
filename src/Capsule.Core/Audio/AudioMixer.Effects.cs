using System.Runtime.InteropServices;
using Capsule.Animation;

namespace Capsule.Audio;

// A bus's effects: the settings a game last set or faded toward, and the values the mixer steps toward
// them each tick, raised to the host as one SetBusEffects command whenever a value moves.
public sealed partial class AudioMixer
{
    // Cutoffs step in log frequency, which moves a sweep evenly through every octave. A cutoff faded to
    // null moves to the end of the band it is off at.
    private static readonly float LowestLog = DeterministicMath.Log2(AudioEffects.LowestHertz);

    private static readonly float HighestLog = DeterministicMath.Log2(AudioEffects.HighestHertz);

    /// <summary>The effects this bus was last set or faded toward. An untouched bus reads none.</summary>
    /// <remarks>A fade's target reads here from the call that starts it.</remarks>
    public AudioEffects GetEffects(AudioBus bus)
    {
        int index = Find(bus);

        return index < 0 ? default : _buses[index].Effects.Target;
    }

    /// <summary>Sets this bus's effects at once, registering the bus if it is new.</summary>
    /// <remarks>
    /// This cancels any ramp <see cref="FadeEffects(AudioBus, in AudioEffects, float, Ease)"/> started on the
    /// bus. Effects equal to the bus's current ones change nothing, and its echo rings on. A changed
    /// <see cref="AudioEcho.DelaySeconds"/> cuts the repeats already ringing.
    /// </remarks>
    public void SetEffects(AudioBus bus, in AudioEffects effects)
    {
        RequireEffects(in effects, nameof(effects));

        int index = Register(bus);
        ref EffectState state = ref CollectionsMarshal.AsSpan(_buses)[index].Effects;
        state.Target = effects;

        bool moved = Snap(ref state.HighPassRamp, ref state.HighPass, LogOf(effects.HighPassHertz, LowestLog));
        moved |= Snap(ref state.LowPassRamp, ref state.LowPass, LogOf(effects.LowPassHertz, HighestLog));

        if (effects.Echo is { } echo)
        {
            moved |= SnapEcho(ref state, in echo);
            moved |= Snap(ref state.EchoVolumeRamp, ref state.EchoVolume, echo.Volume);
        }
        else
        {
            // The echo's other values wait unheard, with no ramp left on them, for the next echo to set.
            state.FeedbackRamp.Active = false;
            state.LowCutRamp.Active = false;
            state.HighCutRamp.Active = false;
            moved |= Snap(ref state.EchoVolumeRamp, ref state.EchoVolume, 0f);
            moved |= state.EchoDelay != 0f;
            state.EchoDelay = 0f;
        }

        if (moved)
        {
            RaiseEffects(index);
        }
    }

    /// <summary>
    /// Ramps each of this bus's effect settings whose target changes from where it is toward
    /// <paramref name="effects"/> over <paramref name="seconds"/> on <paramref name="ease"/>, registering the
    /// bus if it is new. A duration of zero sets those settings at once.
    /// </summary>
    /// <remarks>
    /// A setting whose target is unchanged keeps any ramp it has. Cutoffs move in log frequency, and one
    /// faded to null moves to the end of the range it is off at. An echo turning on fades its
    /// <see cref="AudioEcho.Volume"/> in from 0, and one turning off fades it out and is then off. A
    /// changed <see cref="AudioEcho.DelaySeconds"/> never fades: it takes effect on this call and cuts the
    /// repeats already ringing. A changed <see cref="AudioEcho.InvertLeft"/> takes effect on this call too,
    /// swinging the repeats' left side over a few milliseconds. A ramp does not move on this call. Its
    /// first move lands on the next step.
    /// </remarks>
    public void FadeEffects(AudioBus bus, in AudioEffects effects, float seconds, Ease ease = Ease.Linear)
    {
        RequireEffects(in effects, nameof(effects));
        Guard.NonNegative(seconds, nameof(seconds));
        Guard.RequireEase(ease, nameof(ease));

        int index = Register(bus);
        ref EffectState state = ref CollectionsMarshal.AsSpan(_buses)[index].Effects;
        AudioEffects from = state.Target;
        state.Target = effects;
        bool moved = false;

        if (from.HighPassHertz != effects.HighPassHertz)
        {
            moved |= Fade(ref state.HighPassRamp, ref state.HighPass, LogOf(effects.HighPassHertz, LowestLog), seconds, ease);
        }

        if (from.LowPassHertz != effects.LowPassHertz)
        {
            moved |= Fade(ref state.LowPassRamp, ref state.LowPass, LogOf(effects.LowPassHertz, HighestLog), seconds, ease);
        }

        if (effects.Echo is { } echo)
        {
            moved |= FadeEcho(ref state, from.Echo, in echo, seconds, ease);
        }
        else if (from.Echo is not null)
        {
            moved |= Fade(ref state.EchoVolumeRamp, ref state.EchoVolume, 0f, seconds, ease);
            moved |= SilenceEcho(ref state);
        }

        if (moved)
        {
            RaiseEffects(index);
        }
    }

    // A changed delay takes effect at the call. Anything else on an echo that has a line fades from where
    // it is, but an echo with no line has nothing ringing, and only its volume fades in.
    private bool FadeEcho(ref EffectState state, AudioEcho? from, in AudioEcho echo, float seconds, Ease ease)
    {
        if (state.EchoDelay == 0f)
        {
            return SnapEcho(ref state, in echo) | Fade(ref state.EchoVolumeRamp, ref state.EchoVolume, echo.Volume, seconds, ease);
        }

        bool moved = state.EchoDelay != echo.DelaySeconds || state.EchoInvertLeft != echo.InvertLeft;
        state.EchoDelay = echo.DelaySeconds;
        state.EchoInvertLeft = echo.InvertLeft;

        // An echo fading back in before its line dropped fades every value from where it is.
        bool fresh = from is null;
        AudioEcho was = from.GetValueOrDefault();

        if (fresh || was.Volume != echo.Volume)
        {
            moved |= Fade(ref state.EchoVolumeRamp, ref state.EchoVolume, echo.Volume, seconds, ease);
        }

        if (fresh || was.Feedback != echo.Feedback)
        {
            moved |= Fade(ref state.FeedbackRamp, ref state.EchoFeedback, echo.Feedback, seconds, ease);
        }

        if (fresh || was.LowCutHertz != echo.LowCutHertz)
        {
            moved |= Fade(ref state.LowCutRamp, ref state.EchoLowCut, LogOf(echo.LowCutHertz, LowestLog), seconds, ease);
        }

        if (fresh || was.HighCutHertz != echo.HighCutHertz)
        {
            moved |= Fade(ref state.HighCutRamp, ref state.EchoHighCut, LogOf(echo.HighCutHertz, HighestLog), seconds, ease);
        }

        return moved;
    }

    // Everything on an echo but its volume, set at once.
    private static bool SnapEcho(ref EffectState state, in AudioEcho echo)
    {
        bool moved = state.EchoDelay != echo.DelaySeconds || state.EchoInvertLeft != echo.InvertLeft;
        state.EchoDelay = echo.DelaySeconds;
        state.EchoInvertLeft = echo.InvertLeft;
        moved |= Snap(ref state.FeedbackRamp, ref state.EchoFeedback, echo.Feedback);
        moved |= Snap(ref state.LowCutRamp, ref state.EchoLowCut, LogOf(echo.LowCutHertz, LowestLog));
        moved |= Snap(ref state.HighCutRamp, ref state.EchoHighCut, LogOf(echo.HighCutHertz, HighestLog));

        return moved;
    }

    // Drops the line of an echo whose target is off once its volume has faded out. Returns whether that
    // changed the delay.
    private static bool SilenceEcho(ref EffectState state)
    {
        if (state.Target.Echo is not null || state.EchoDelay == 0f || state.EchoVolumeRamp.Active)
        {
            return false;
        }

        state.EchoDelay = 0f;

        return true;
    }

    // Moves every effect ramp on this bus to the current tick. Returns whether any value changed.
    private bool AdvanceEffects(ref EffectState state)
    {
        bool moved = AdvanceRamp(ref state.HighPassRamp, ref state.HighPass, _tick);
        moved |= AdvanceRamp(ref state.LowPassRamp, ref state.LowPass, _tick);
        moved |= AdvanceRamp(ref state.EchoVolumeRamp, ref state.EchoVolume, _tick);
        moved |= AdvanceRamp(ref state.FeedbackRamp, ref state.EchoFeedback, _tick);
        moved |= AdvanceRamp(ref state.LowCutRamp, ref state.EchoLowCut, _tick);
        moved |= AdvanceRamp(ref state.HighCutRamp, ref state.EchoHighCut, _tick);

        return SilenceEcho(ref state) | moved;
    }

    private void RebaseEffects(ref EffectState state, double oldStepSeconds, double newStepSeconds)
    {
        RebaseRamp(ref state.HighPassRamp, oldStepSeconds, newStepSeconds);
        RebaseRamp(ref state.LowPassRamp, oldStepSeconds, newStepSeconds);
        RebaseRamp(ref state.EchoVolumeRamp, oldStepSeconds, newStepSeconds);
        RebaseRamp(ref state.FeedbackRamp, oldStepSeconds, newStepSeconds);
        RebaseRamp(ref state.LowCutRamp, oldStepSeconds, newStepSeconds);
        RebaseRamp(ref state.HighCutRamp, oldStepSeconds, newStepSeconds);
    }

    // Starts a ramp on one field, or for a zero-tick one sets it at once. Returns whether the field changed
    // on this call.
    private bool Fade(ref Ramp ramp, ref float value, float to, float seconds, Ease ease)
    {
        StartRamp(ref ramp, value, to, seconds, ease);

        return !ramp.Active && Snap(ref ramp, ref value, to);
    }

    // Cancels the field's ramp and sets it. Returns whether that changed it.
    private static bool Snap(ref Ramp ramp, ref float value, float to)
    {
        ramp.Active = false;
        bool moved = value != to;
        value = to;

        return moved;
    }

    private void RaiseEffects(int index)
    {
        ref readonly EffectState state = ref CollectionsMarshal.AsSpan(_buses)[index].Effects;
        EffectLevels levels = new(
            HertzOf(state.HighPass, LowestLog, AudioEffects.LowestHertz),
            HertzOf(state.LowPass, HighestLog, AudioEffects.HighestHertz),
            state.EchoDelay,
            state.EchoFeedback,
            state.EchoVolume,
            HertzOf(state.EchoLowCut, LowestLog, AudioEffects.LowestHertz),
            HertzOf(state.EchoHighCut, HighestLog, AudioEffects.HighestHertz),
            state.EchoInvertLeft);

        _commands.Add(new AudioCommand(AudioCommandKind.SetBusEffects, Voice.None, Bus: BusOf(index), Effects: levels));
    }

    private static float LogOf(float? hertz, float offLog) => hertz is { } value ? DeterministicMath.Log2(value) : offLog;

    // A ramp lands exactly on its target, which keeps the off ends exact for the host's bypass.
    private static float HertzOf(float log, float offLog, float offHertz) => log == offLog ? offHertz : DeterministicMath.Exp2(log);

    private static void RequireEffects(in AudioEffects effects, string parameterName)
    {
        RequireCutoff(effects.HighPassHertz, nameof(AudioEffects.HighPassHertz), parameterName);
        RequireCutoff(effects.LowPassHertz, nameof(AudioEffects.LowPassHertz), parameterName);

        if (effects.Echo is not { } echo)
        {
            return;
        }

        if (!(echo.DelaySeconds > 0f && echo.DelaySeconds <= AudioEcho.LongestDelaySeconds))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                echo.DelaySeconds,
                $"Expected an echo's DelaySeconds in (0, {AudioEcho.LongestDelaySeconds}]. Build the echo with new AudioEcho(seconds), since default(AudioEcho) has no delay.");
        }

        if (!(echo.Feedback >= 0f && echo.Feedback < 1f))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                echo.Feedback,
                "Expected an echo's Feedback in [0, 1). At 1 or more its repeats would never die away.");
        }

        if (!(echo.Volume >= 0f && echo.Volume <= 1f))
        {
            throw new ArgumentOutOfRangeException(parameterName, echo.Volume, "Expected an echo's Volume in [0, 1].");
        }

        RequireCutoff(echo.LowCutHertz, nameof(AudioEcho.LowCutHertz), parameterName);
        RequireCutoff(echo.HighCutHertz, nameof(AudioEcho.HighCutHertz), parameterName);
    }

    private static void RequireCutoff(float? hertz, string field, string parameterName)
    {
        if (hertz is { } value && !(value >= AudioEffects.LowestHertz && value <= AudioEffects.HighestHertz))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Expected {field} in [{AudioEffects.LowestHertz}, {AudioEffects.HighestHertz}] hertz, or null for no cut.");
        }
    }

    // One bus's effects. Cutoffs are held as base-two logarithms of hertz, which is what they ramp in.
    // EchoDelay is 0 while the bus has no echo line, and the echo's other values then wait unheard.
    private struct EffectState()
    {
        internal AudioEffects Target;

        internal float HighPass = LowestLog;

        internal float LowPass = HighestLog;

        internal float EchoDelay;

        internal float EchoFeedback;

        internal float EchoVolume;

        internal float EchoLowCut = LowestLog;

        internal float EchoHighCut = HighestLog;

        // Never ramps here. The host swings the repeats' left side across one block.
        internal bool EchoInvertLeft;

        internal Ramp HighPassRamp;

        internal Ramp LowPassRamp;

        internal Ramp EchoVolumeRamp;

        internal Ramp FeedbackRamp;

        internal Ramp LowCutRamp;

        internal Ramp HighCutRamp;
    }
}
