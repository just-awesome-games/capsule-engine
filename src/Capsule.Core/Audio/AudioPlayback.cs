namespace Capsule.Audio;

/// <summary>
/// Everything one <see cref="AudioMixer.Play(in AudioPlayback)"/> takes. Build it from the
/// constructor: the defaults below are the constructor's, so <c>default(AudioPlayback)</c> carries
/// a pitch of zero and no mixer accepts it.
/// </summary>
/// <param name="Clip">The clip to play.</param>
public readonly record struct AudioPlayback(AudioClip Clip)
{
    /// <summary>The bus this voice mixes and pauses through; <see cref="AudioBus.Master"/> by default.</summary>
    public AudioBus Bus { get; init; }

    /// <summary>
    /// This voice's own linear amplitude in [0, 1], multiplied by its bus's volume and the master
    /// volume to give the gain the host applies; 1 by default.
    /// </summary>
    public float Volume { get; init; } = 1f;

    /// <summary>
    /// Playback-rate multiplier, positive and finite; 1 by default. A one-shot at pitch 2 runs for
    /// half its clip's duration.
    /// </summary>
    public float Pitch { get; init; } = 1f;

    /// <summary>
    /// Where the voice sits between the speakers, in [-1, 1]: -1 hard left, 0 centred, 1 hard right;
    /// centred by default. A mono clip is panned across the whole field; a stereo one is rotated
    /// within it where the device supports that, and plays centred where it does not.
    /// </summary>
    public float Pan { get; init; }

    /// <summary>
    /// Clip time the voice begins at, in seconds from the clip's start; 0 by default, which is always
    /// legal. Anything else is before <see cref="AudioClip.DurationSeconds"/>: a one-shot then runs
    /// for what is left of the clip, and a looping voice plays from here to its loop region's end
    /// before repeating the region. A looping start at or past that region's end is folded into the
    /// region, so the voice begins where a repeat from it would already have reached.
    /// </summary>
    public double StartSeconds { get; init; }

    /// <summary>
    /// Whether the voice repeats forever rather than ending after one pass of the clip. Where the
    /// clip carries an <see cref="AudioClip.LoopRegion"/>, the repeat is of that region: the voice
    /// plays from the clip's beginning to the region's end and then repeats the region. A voice that
    /// does not loop ignores the region and plays the clip to its end.
    /// </summary>
    public bool Loop { get; init; }
}
