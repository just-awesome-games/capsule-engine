using Capsule.Animation;
using Capsule.Rendering;
using Capsule.Scenes.Rendering;

namespace Capsule.Scenes.Animation;

/// <summary>
/// Plays a <see cref="SpriteClip"/> on the fixed step and writes its current frame into a
/// <see cref="SpriteRenderer"/>. The renderer is named at construction rather than looked up, so an
/// entity drawing itself as several sprites animates whichever of them it says.
/// <para>
/// The animator owns the renderer's <see cref="SpriteRenderer.Sprite"/> — region and pivot together,
/// since a pivot is per frame — and nothing else: <see cref="SpriteRenderer.Offset"/>,
/// <see cref="SpriteRenderer.Scale"/>, the flips and the colour stay the renderer's, and stay
/// whatever the game sets them to.
/// </para>
/// <para>
/// Advance is ticks alone. Frames never move with the frame rate or the render clock, so the frame
/// an entity is on is simulation state: deterministic, assertable headlessly, and readable by
/// gameplay for an attack's active window or a landing's recovery.
/// </para>
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

    /// <summary>
    /// The frame written to the renderer, and the renderer's own sprite until a clip plays.
    /// </summary>
    public Sprite Frame => Clip is { } clip ? clip.Frames[_playback.FrameIndex] : _renderer.Sprite;

    /// <summary>
    /// Whether a non-looping clip has spent its last frame's ticks. A looping clip never finishes,
    /// and neither does an animator with nothing to play.
    /// </summary>
    public bool IsFinished => _playback.IsFinished;

    /// <summary>
    /// Plays <paramref name="clip"/> from its first frame, and draws that frame at once so the
    /// change shows on this step rather than the next. The first frame is then held for exactly
    /// its own ticks, counted from the frame drawn for the step this was called in: a one-tick
    /// frame started from an entity's step is drawn for that step and gone by the next.
    /// <para>
    /// Called after this animator has already stepped — from a scene's late step, or from an
    /// entity or component the scene steps later — the first frame is drawn for one tick longer,
    /// since the step that spends no tick on it is then the following one.
    /// </para>
    /// <para>
    /// Playing the clip already playing is ignored, so a walk cycle asked for every step keeps
    /// running instead of freezing on frame 0. Pass <paramref name="restart"/> to replay it from
    /// the start — the second swing of a two-hit attack.
    /// </para>
    /// </summary>
    /// <param name="clip">The clip to play.</param>
    /// <param name="restart">Whether to restart the clip when it is already the one playing.</param>
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

        // The frame Play chose is the one drawn for the step Play ran in, so that step spends no
        // tick on it — an entity's step runs before its components', and advancing here would
        // retire a one-tick first frame before any frame view was built from it.
        if (_startedSinceStep)
        {
            _startedSinceStep = false;
            return;
        }

        _playback.Step(clip.FrameTicks, clip.Loop);
        _renderer.Sprite = clip.Frames[_playback.FrameIndex];
    }
}
