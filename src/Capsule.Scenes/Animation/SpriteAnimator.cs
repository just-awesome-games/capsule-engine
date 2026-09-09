using Capsule.Animation;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Scenes.Rendering;

namespace Capsule.Scenes.Animation;

/// <summary>
/// Plays a <see cref="SpriteClip"/> on the fixed step and writes its current frame into the
/// <see cref="SpriteRenderer"/> named at construction. It owns that renderer's
/// <see cref="SpriteRenderer.Sprite"/> and nothing else; offset, scale, flips and colour stay the
/// renderer's. Advance is ticks alone, never the frame rate, so the frame an entity is on is
/// simulation state.
/// </summary>
/// <remarks>
/// A <see cref="Component"/> steps after its entity, so an entity reading its animator's
/// <see cref="Clip"/>, <see cref="FrameIndex"/> or <see cref="Tick"/> in
/// <see cref="Entity.OnStep"/> reads the frame the previous step drew, and a
/// <see cref="Play(SpriteClip, int)"/> made there at that <see cref="Tick"/> re-enters the
/// previous step's position and costs the clip a tick. Logic that depends on the frame drawn
/// belongs in a component attached after the animator, which sees this step's frame; a
/// <see cref="Play(SpriteClip, bool)"/> from there still draws at once.
/// </remarks>
/// <param name="renderer">The renderer whose frame this animator writes.</param>
public sealed class SpriteAnimator(SpriteRenderer renderer) : Component
{
    private readonly SpriteRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));

    private AnimationPlayback _playback;
    private bool _pendingStart;
    private long? _startedOnTick;

    /// <summary>The clip playing, or null until one is played.</summary>
    public SpriteClip? Clip { get; private set; }

    /// <summary>The frame of <see cref="Clip"/> currently drawn, from 0; 0 while nothing plays.</summary>
    public int FrameIndex => _playback.FrameIndex;

    /// <summary>The frame written to the renderer, and the renderer's own sprite until a clip plays.</summary>
    public Sprite Frame => Clip is { } clip ? clip.Frames[_playback.FrameIndex] : _renderer.Sprite;

    /// <summary>
    /// Whether a non-looping clip has spent its last frame's ticks. A looping clip never finishes,
    /// and neither does an animator with nothing to play.
    /// </summary>
    public bool IsFinished => _playback.IsFinished;

    /// <summary>
    /// Ticks elapsed since the current pass through <see cref="Clip"/> began: the ticks of every
    /// earlier frame plus those spent on the frame drawn, which is the clip's total ticks once a
    /// non-looping clip has finished. 0 while nothing plays. Passed back as the tick of
    /// <see cref="Play(SpriteClip, int)"/> it reproduces this position.
    /// </summary>
    public int Tick => Clip is { } clip ? _playback.TickOf(clip.FrameTicks) : 0;

    /// <inheritdoc/>
    protected internal override void CollectAssets(AssetCollection assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        if (Clip is not { } clip)
        {
            return;
        }

        foreach (Sprite frame in clip.Frames)
        {
            if (frame.Texture != default)
            {
                assets.Add(frame.Texture);
            }
        }
    }

    /// <summary>
    /// Plays <paramref name="clip"/> from its first frame and draws that frame at once, so the
    /// change shows on this step rather than the next. The first frame is then held for exactly
    /// its own ticks, counted from the tick this was called in wherever in that step it was
    /// called: an entity, a component stepped after this animator and a late step all give the
    /// same frames. Called outside a step, the first frame is held from the next one.
    /// <para>
    /// Playing the clip already playing is ignored unless <paramref name="restart"/> is passed,
    /// and a finished non-looping clip is still the clip playing — re-triggering one from a state
    /// that has not changed is <paramref name="restart"/>.
    /// </para>
    /// </summary>
    /// <param name="clip">The clip to play.</param>
    /// <param name="restart">Whether to restart the clip when it is already the one playing.</param>
    /// <exception cref="ArgumentNullException">The clip is null.</exception>
    public void Play(SpriteClip clip, bool restart = false)
    {
        ArgumentNullException.ThrowIfNull(clip);

        if (ReferenceEquals(Clip, clip) && !restart)
        {
            return;
        }

        Clip = clip;
        _playback.Restart();
        _pendingStart = true;
        _startedOnTick = Entity?.Scene?.SteppingTick;
        _renderer.Sprite = clip.Frames[0];
    }

    /// <summary>
    /// Plays <paramref name="clip"/> positioned as though it had started <paramref name="atTick"/>
    /// ticks ago, and draws that frame at once, so the change shows on this step rather than the
    /// next. The tick lands on the frame it would have reached, with that frame's ticks partly
    /// spent: the frame is then held for the rest of its own ticks, counted from the tick this was
    /// called in wherever in that step it was called. An <paramref name="atTick"/> of 0 is
    /// <see cref="Play(SpriteClip, bool)"/> with a restart.
    /// <para>
    /// A looping clip wraps the tick modulo its total ticks and never finishes; one that does not
    /// loop clamps a tick at or past its total to the last frame, already finished. Unlike
    /// <see cref="Play(SpriteClip, bool)"/> this always repositions, including when
    /// <paramref name="clip"/> is the clip already playing.
    /// </para>
    /// <para>
    /// A pose variant — the same motion drawn with the weapon raised — is played at
    /// <see cref="Tick"/>: given the same frame count and per-frame ticks as the clip playing it
    /// draws the frame that clip stood on, this step, and carries on from there, a finished clip
    /// staying finished. A clip of any other shape simply seeks; nothing is checked.
    /// </para>
    /// </summary>
    /// <param name="clip">The clip to play.</param>
    /// <param name="atTick">Ticks elapsed since the clip would have started; not negative.</param>
    /// <exception cref="ArgumentNullException">The clip is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The tick is negative.</exception>
    public void Play(SpriteClip clip, int atTick)
    {
        ArgumentNullException.ThrowIfNull(clip);

        Clip = clip;
        _playback.Seek(clip.FrameTicks, clip.Loop, atTick);
        _pendingStart = true;
        _startedOnTick = Entity?.Scene?.SteppingTick;
        _renderer.Sprite = clip.Frames[_playback.FrameIndex];
    }

    /// <inheritdoc/>
    protected internal override void OnStep(in StepContext context)
    {
        if (Clip is not { } clip)
        {
            return;
        }

        // The frame Play chose is drawn for the tick Play ran in, so that tick spends no step on
        // it: advancing here would retire a one-tick first frame before any frame view saw it.
        // Keyed on the tick rather than on having stepped since, so a Play from a component
        // stepped after this one — which reaches this animator only on the following step — is
        // not mistaken for a Play belonging to that following tick. A Play outside a step belongs
        // to no tick and is spent on the next one.
        if (_pendingStart)
        {
            _pendingStart = false;
            if (_startedOnTick is not { } startedOn || startedOn == context.Tick)
            {
                return;
            }
        }

        _playback.Step(clip.FrameTicks, clip.Loop);
        _renderer.Sprite = clip.Frames[_playback.FrameIndex];
    }
}
