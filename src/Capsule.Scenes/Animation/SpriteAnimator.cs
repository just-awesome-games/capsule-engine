using Capsule.Animation;
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
/// <param name="renderer">The renderer whose frame this animator writes.</param>
public sealed class SpriteAnimator(SpriteRenderer renderer) : Component
{
    private readonly SpriteRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));

    private AnimationPlayback _playback;
    private bool _startedSinceStep;

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
    /// Plays <paramref name="clip"/> from its first frame and draws that frame at once, so the
    /// change shows on this step rather than the next. The first frame is then held for its own
    /// ticks counted from the step this was called in; called after this animator has already
    /// stepped, it is drawn one tick longer. Playing the clip already playing is ignored unless
    /// <paramref name="restart"/> is passed.
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
        _startedSinceStep = true;
        _renderer.Sprite = clip.Frames[0];
    }

    /// <inheritdoc/>
    protected internal override void OnStep(in StepContext context)
    {
        if (Clip is not { } clip)
        {
            return;
        }

        // The frame Play chose is drawn for the step Play ran in, so that step spends no tick on
        // it: advancing here would retire a one-tick first frame before any frame view saw it.
        if (_startedSinceStep)
        {
            _startedSinceStep = false;
            return;
        }

        _playback.Step(clip.FrameTicks, clip.Loop);
        _renderer.Sprite = clip.Frames[_playback.FrameIndex];
    }
}
