using Capsule.Animation;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Audio;

/// <summary>Plays one clip for its entity and holds the voice.</summary>
/// <remarks>
/// The entity can then stop, pause and re-level it. The clip is declared as a preload, and the
/// voice stops when the entity leaves the scene. A transition therefore silences what a scene's
/// entities were playing, while anything started through <see cref="Run.Audio"/> keeps playing.
/// <para>
/// Capsule mixes no position into gain. This component is a handle on a voice, not a point in
/// space.
/// </para>
/// </remarks>
/// <param name="clip">The clip <see cref="Play()"/> starts.</param>
/// <example>
/// <code>
/// _footfall = new AudioSource(CapsuleAssets.Audio.StepSoftSound) { Bus = AudioBuses.Sfx };
/// Add(_footfall);
///
/// // On the step the body lands:
/// _footfall.Play();
/// </code>
/// </example>
public sealed class AudioSource(AudioClip clip) : Component
{
    // The mixer the live voice belongs to. Kept so the voice is stopped by the mixer that started it,
    // even after the entity has left the scene it reached that mixer through.
    private AudioMixer? _playing;

    private Voice _voice;
    private float _volume = 1f;
    private float _pitch = 1f;
    private float _pan;

    /// <summary>The clip <see cref="Play()"/> starts and <see cref="Component.CollectAssets"/> declares. Read at each play.</summary>
    public AudioClip Clip { get; set; } = clip;

    /// <summary>The bus this source's voices mix and pause through. <see cref="AudioBus.Master"/> by default, and read at each play.</summary>
    public AudioBus Bus { get; set; }

    /// <summary>
    /// This source's linear amplitude in [0, 1], 1 by default. It applies to the live voice immediately
    /// and to every voice played afterwards.
    /// </summary>
    public float Volume
    {
        get => _volume;
        set
        {
            Guard.InUnit(value, nameof(value));
            _volume = value;
            _playing?.SetVolume(_voice, value);
        }
    }

    /// <summary>
    /// Ramps <see cref="Volume"/> from where the live voice is to <paramref name="volume"/> over
    /// <paramref name="seconds"/> on <paramref name="ease"/>. <see cref="Volume"/> reads
    /// <paramref name="volume"/> at once, the target, not the ramp.
    /// </summary>
    /// <remarks>Does nothing to the mixer when this source owns no voice.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="volume"/> is outside [0, 1], <paramref name="seconds"/> is negative or not
    /// finite, or <paramref name="ease"/> is not a declared curve.
    /// </exception>
    public void FadeVolume(float volume, float seconds, Ease ease = Ease.Linear)
    {
        Guard.InUnit(volume, nameof(volume));
        Guard.RequireSeconds(seconds, nameof(seconds));
        Guard.RequireEase(ease, nameof(ease));

        _volume = volume;
        _playing?.FadeVolume(_voice, volume, seconds, ease);
    }

    /// <summary>
    /// The playback-rate multiplier, positive and finite, 1 by default. It applies to the live voice
    /// immediately and to every voice played afterwards.
    /// </summary>
    public float Pitch
    {
        get => _pitch;
        set
        {
            Guard.Positive(value, nameof(value));
            _pitch = value;
            _playing?.SetPitch(_voice, value);
        }
    }

    /// <summary>
    /// Where this source's voices sit between the speakers, in [-1, 1], where -1 is hard left, 0 is
    /// centred and 1 is hard right. Centred by default.
    /// </summary>
    /// <remarks>
    /// It applies to the live voice immediately and to every voice played afterwards. A mono clip
    /// pans across the whole field, and a stereo clip is rotated within it where the device
    /// supports that.
    /// </remarks>
    public float Pan
    {
        get => _pan;
        set
        {
            Guard.InRange(value, -1f, 1f, nameof(value));
            _pan = value;
            _playing?.SetPan(_voice, value);
        }
    }

    /// <summary>
    /// The clip time this source's voice has reached on the current step, in seconds from the
    /// clip's start. Reads 0 when the source owns no voice.
    /// </summary>
    /// <remarks>It advances in whole simulation steps.</remarks>
    public double Time => _playing?.GetTime(_voice) ?? 0.0;

    /// <summary>
    /// Whether <see cref="Play()"/> starts a voice that repeats forever. Read at each play.
    /// </summary>
    /// <remarks>
    /// When the clip carries an <see cref="AudioClip.LoopRegion"/>, set by the build from the audio
    /// file or by the game, the voice plays from the clip's beginning to the region's end and then
    /// repeats the region. A clip with no region repeats in full. <see cref="PlayOneShot"/> does
    /// not loop.
    /// </remarks>
    public bool Loop { get; set; }

    /// <summary>Whether the source starts playing in <see cref="Component.OnStart"/>.</summary>
    public bool PlayOnStart { get; set; }

    /// <summary>
    /// Whether this source still owns a voice, either sounding or held by its own pause or its
    /// bus's. It reads false before the first <see cref="Play()"/> and after the voice ends,
    /// whether stopped, expired or stolen.
    /// </summary>
    /// <remarks>
    /// This reports ownership, not audibility, and it is the check to make before starting another
    /// voice. <see cref="IsPlaying"/> and <see cref="IsPaused"/> split it in two.
    /// </remarks>
    public bool IsLive => _playing?.IsLive(_voice) ?? false;

    /// <summary>Whether this source's voice is live and sounding. Reads false while it is held.</summary>
    public bool IsPlaying => _playing?.IsPlaying(_voice) ?? false;

    /// <summary>Whether this source's voice is live and held, by its own pause or its bus's.</summary>
    public bool IsPaused => _playing?.IsPaused(_voice) ?? false;

    /// <summary>
    /// Starts <see cref="Clip"/> from the beginning and stops whatever this source was already playing.
    /// Nothing starts when every voice in the mixer is a live loop.
    /// </summary>
    /// <exception cref="InvalidOperationException">This source is on no entity in a started scene.</exception>
    public void Play() => Play(0.0);

    /// <summary>
    /// Starts <see cref="Clip"/> from <paramref name="startSeconds"/> and stops whatever this source was
    /// already playing. Nothing starts when every voice in the mixer is a live loop.
    /// </summary>
    /// <param name="startSeconds">
    /// The clip time to begin at, in seconds from the clip's start. It must be zero or greater, and below
    /// the clip's duration unless the duration is zero.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="startSeconds"/> is negative, not finite, or at or past the clip's duration.</exception>
    /// <exception cref="InvalidOperationException">This source is on no entity in a started scene.</exception>
    public void Play(double startSeconds)
    {
        AudioMixer mixer = Mixer();

        mixer.Stop(_voice);
        _playing = mixer;
        _voice = mixer.Play(new AudioPlayback(Clip)
        {
            Bus = Bus,
            Volume = _volume,
            Pitch = _pitch,
            Pan = _pan,
            StartSeconds = startSeconds,
            Loop = Loop,
        });
    }

    /// <summary>
    /// Plays <paramref name="clip"/> as a separate one-shot on this source's bus and at its
    /// <see cref="Pan"/>, multiplying <see cref="Volume"/> and <see cref="Pitch"/> by the given
    /// scales. This source's own <see cref="Volume"/>, <see cref="Pitch"/> and voice are unchanged.
    /// </summary>
    /// <remarks>The source does not track the new voice, and the caller holds it.</remarks>
    /// <param name="clip">The clip to play once.</param>
    /// <param name="volumeScale">A factor on <see cref="Volume"/> for this play, itself in [0, 1].</param>
    /// <param name="pitchScale">A factor on <see cref="Pitch"/> for this play, positive and finite.</param>
    /// <returns>The voice started, or <see cref="Voice.None"/> when the mixer had none to give.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="volumeScale"/> is outside [0, 1] or is not a number, or
    /// <paramref name="pitchScale"/> is not positive and finite.
    /// </exception>
    /// <exception cref="InvalidOperationException">This source is on no entity in a started scene.</exception>
    public Voice PlayOneShot(AudioClip clip, float volumeScale = 1f, float pitchScale = 1f)
    {
        Guard.InUnit(volumeScale, nameof(volumeScale));
        Guard.Positive(pitchScale, nameof(pitchScale));

        return Mixer().Play(new AudioPlayback(clip)
        {
            Bus = Bus,
            Volume = _volume * volumeScale,
            Pitch = _pitch * pitchScale,
            Pan = _pan,
        });
    }

    /// <summary>Ends this source's voice. Does nothing when the source owns no voice.</summary>
    public void Stop()
    {
        _playing?.Stop(_voice);
        _playing = null;
        _voice = Voice.None;
    }

    /// <summary>
    /// Ramps this source's voice to 0 over <paramref name="seconds"/> and stops it on the landing tick.
    /// Removing the source's entity from its scene during the fade stops the voice at once.
    /// </summary>
    /// <remarks><see cref="IsLive"/> follows the ramp. Poll it for the edge. Does nothing when this source owns no voice.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="seconds"/> is negative or not finite.</exception>
    public void Stop(float seconds)
    {
        Guard.RequireSeconds(seconds, nameof(seconds));

        _playing?.Stop(_voice, seconds);
    }

    /// <summary>Holds this source's voice where it is. Does nothing when the source owns no voice.</summary>
    public void Pause() => _playing?.Pause(_voice);

    /// <summary>Continues this source's held voice. Does nothing when the source owns no voice.</summary>
    public void Resume() => _playing?.Resume(_voice);

    /// <inheritdoc/>
    protected internal override void CollectAssets(AssetCollection assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        if (Clip != default)
        {
            assets.Add(Clip);
        }
    }

    /// <inheritdoc/>
    protected internal override void OnStart()
    {
        if (PlayOnStart)
        {
            Play();
        }
    }

    /// <inheritdoc/>
    protected internal override void OnRemovedFromScene() => Stop();

    private AudioMixer Mixer() =>
        Entity?.SceneOrNull?.RunOrNull?.Audio
        ?? throw new InvalidOperationException($"{nameof(AudioSource)} is on no entity in a scene, so {Scene.NoRunYet}");

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("Clip", Clip.Name + Clip.Extension);
        panel.Field("Bus", Bus.Name);
        panel.Field("Volume", Volume);
        panel.Field("Pitch", Pitch);
        panel.Field("Pan", Pan);
        panel.Field("Time", Time);
        panel.Field("IsPlaying", IsPlaying);
        panel.Field("IsPaused", IsPaused);
        panel.Toggle("Loop", Loop, on => Loop = on);
        panel.Toggle("PlayOnStart", PlayOnStart, on => PlayOnStart = on);
        panel.Command("Play", Play);
        panel.Command("Stop", Stop);
        panel.Command("Pause", Pause);
        panel.Command("Resume", Resume);
    }
}
