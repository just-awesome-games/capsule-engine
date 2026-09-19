namespace Capsule.Audio;

/// <summary>
/// Everything one <see cref="AudioMixer.Play(in AudioPlayback)"/> takes. Build it from the
/// constructor, because the defaults below are the constructor's. <c>default(AudioPlayback)</c>
/// carries a pitch of zero and no mixer accepts it.
/// </summary>
public readonly record struct AudioPlayback(AudioClip Clip)
{
    /// <summary>The bus this voice mixes and pauses through. <see cref="AudioBus.Master"/> by default.</summary>
    public AudioBus Bus { get; init; }

    /// <summary>
    /// This voice's own linear amplitude in [0, 1], multiplied by its bus's volume and the master
    /// volume to give the gain the host applies. 1 by default.
    /// </summary>
    public float Volume { get; init; } = 1f;

    /// <summary>Playback-rate multiplier, positive and finite, and 1 by default. A one-shot at pitch 2 runs for half its clip's duration.</summary>
    public float Pitch { get; init; } = 1f;

    /// <summary>
    /// Where the voice sits between the speakers, in [-1, 1], with -1 hard left, 0 centred and 1 hard
    /// right. Centred by default. A mono clip pans across the field, and a stereo one rotates within it
    /// where the device supports that.
    /// </summary>
    public float Pan { get; init; }

    /// <summary>
    /// Clip time the voice begins at, in seconds from the clip's start, and 0 by default. Any other
    /// value must be below <see cref="AudioClip.DurationSeconds"/>. A one-shot then runs for what is
    /// left of the clip, and a looping voice plays from here to its loop region's end before repeating
    /// the region. A looping start at or past that region's end is folded into the region.
    /// </summary>
    public double StartSeconds { get; init; }

    /// <summary>
    /// Whether the voice repeats instead of ending after one pass of the clip. Where the clip carries
    /// an <see cref="AudioClip.LoopRegion"/>, the repeat covers that region.
    /// </summary>
    public bool Loop { get; init; }
}
