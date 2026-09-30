using System.Globalization;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Animation;

/// <summary>
/// Plays a <see cref="SpriteClip"/> on the fixed step and writes its current frame into the
/// <see cref="SpriteRenderer"/> given at construction. It owns that renderer's
/// <see cref="SpriteRenderer.Sprite"/> and nothing else.
/// </summary>
/// <remarks>
/// Offset, scale, flips and colour stay with the renderer. Playback advances on ticks, not on the
/// frame rate, and the frame an entity is on is simulation state.
/// <para>
/// A <see cref="Component"/> steps after its entity. An entity reading its animator's
/// <see cref="Clip"/>, <see cref="FrameIndex"/>, <see cref="FrameTick"/> or <see cref="Tick"/> in
/// <see cref="Entity.OnStep"/> therefore sees the frame the previous step drew. A
/// <see cref="Play(SpriteClip, int)"/> made there at that <see cref="Tick"/> re-enters the previous
/// step's position and costs the clip a tick. Put logic that depends on the frame drawn in a
/// component attached after the animator.
/// </para>
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

    /// <summary>The frame of <see cref="Clip"/> currently drawn, counted from 0. Reads 0 while nothing plays.</summary>
    public int FrameIndex => _playback.FrameIndex;

    /// <summary>The frame written to the renderer. Reads the renderer's own sprite until a clip plays.</summary>
    public Sprite Frame => Clip is { } clip ? clip.Frames[_playback.FrameIndex] : _renderer.Sprite;

    /// <summary>
    /// Whether a non-looping clip has spent its last frame's ticks. A looping clip never finishes, and
    /// neither does an animator with no clip to play.
    /// </summary>
    public bool IsFinished => _playback.IsFinished;

    /// <summary>
    /// How many ticks have elapsed since the current pass through <see cref="Clip"/> began,
    /// counting every earlier frame's ticks plus those spent on the frame drawn. It reaches the
    /// clip's total ticks once a non-looping clip finishes, and reads 0 while nothing plays.
    /// </summary>
    /// <remarks>
    /// Passing this value to <see cref="Play(SpriteClip, int)"/> reproduces this position.
    /// </remarks>
    public int Tick => Clip is { } clip ? _playback.TickOf(clip.FrameTicks) : 0;

    /// <summary>
    /// How many ticks have been spent on the frame drawn. It reads 0 on the step a frame begins, and
    /// 0 while nothing plays.
    /// </summary>
    /// <remarks>
    /// A finished non-looping clip reads its last frame's full ticks. Passing
    /// <see cref="FrameIndex"/> and this value to <see cref="PlayAtFrame"/> reproduces this position.
    /// </remarks>
    public int FrameTick => _playback.TicksElapsed;

    /// <summary>Whether the clip holds on its current frame while the entity keeps stepping.</summary>
    /// <remarks>
    /// A held clip keeps drawing its frame. Its <see cref="Tick"/> does not advance and
    /// <see cref="IsFinished"/> does not change. Every <c>Play</c> method leaves this as it is. A
    /// clip played while held draws its frame and stays held. Removal from the scene clears it.
    /// <para>
    /// After at least one held step, the animator's first step after this clears advances from the
    /// held tick. A held step spends the step a <c>Play</c> holds its first frame for. A
    /// <c>Play</c> followed by a clear with no held step between them keeps that first step.
    /// </para>
    /// <para>
    /// This holds one clip's playback. Hit-stop and a pause menu hold whole entities with
    /// <see cref="Scene.Freeze(int)"/> and <see cref="Scene.Paused"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// Showing one frame of a clip, frozen:
    /// <code>
    /// _animator.Play(clip, atTick: 6);
    /// _animator.Paused = true;
    /// </code>
    /// </example>
    public bool Paused { get; set; }

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
    /// Plays <paramref name="clip"/> from its first frame and draws that frame immediately. The
    /// change shows on this step instead of the next.
    /// </summary>
    /// <remarks>
    /// The first frame then holds for exactly its own ticks, counted from the tick of this call
    /// whatever point in the step it came from. An entity, a component stepped after this animator
    /// and a late step all produce the same frames. Called outside a step, the first frame holds
    /// from the next step.
    /// <para>
    /// Playing the clip that is already playing does nothing unless <paramref name="restart"/> is
    /// true. A finished non-looping clip still counts as the clip playing. Re-triggering it from
    /// unchanged state needs <paramref name="restart"/>.
    /// </para>
    /// </remarks>
    /// <param name="clip">The clip to play.</param>
    /// <param name="restart">Whether to restart the clip when it is already playing.</param>
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
        _startedOnTick = Entity?.SceneOrNull?.SteppingTick;
        _renderer.Sprite = clip.Frames[0];
    }

    /// <summary>
    /// Plays <paramref name="clip"/> positioned as though it had started <paramref name="atTick"/>
    /// ticks ago, and draws that frame immediately. The change shows on this step instead of the
    /// next.
    /// </summary>
    /// <remarks>
    /// The tick lands on the frame the clip would have reached, with that frame's ticks partly
    /// spent, and the frame then holds for the rest of its ticks, counted from the tick of this
    /// call whatever point in the step it came from. An <paramref name="atTick"/> of 0 matches
    /// <see cref="Play(SpriteClip, bool)"/> with a restart.
    /// <para>
    /// A looping clip wraps the tick modulo its total ticks and never finishes. A non-looping clip
    /// clamps a tick at or past its total to the last frame, already finished. This overload always
    /// repositions, even when <paramref name="clip"/> is the clip already playing, which
    /// <see cref="Play(SpriteClip, bool)"/> does not.
    /// </para>
    /// <para>
    /// Played at <see cref="Tick"/>, a clip with the same frame count and per-frame ticks as the
    /// one playing draws the frame that clip stood on and continues from there, and a finished clip
    /// stays finished. A clip of any other shape only seeks, and nothing is validated.
    /// <see cref="PlayAtFrame"/> keeps the frame across clips of any shape.
    /// </para>
    /// </remarks>
    /// <param name="clip">The clip to play.</param>
    /// <param name="atTick">How many ticks have elapsed since the clip would have started. Not negative.</param>
    public void Play(SpriteClip clip, int atTick)
    {
        ArgumentNullException.ThrowIfNull(clip);

        Clip = clip;
        _playback.Seek(clip.FrameTicks, clip.Loop, atTick);
        Reposition(clip);
    }

    /// <summary>
    /// Plays <paramref name="clip"/> on frame <paramref name="frameIndex"/> with
    /// <paramref name="frameTick"/> of its ticks already spent, and draws that frame immediately.
    /// </summary>
    /// <remarks>
    /// The frame then holds for the rest of its ticks, counted from the tick of this call whatever
    /// point in the step it came from. A frame tick of 0 starts the frame fresh. This method always
    /// repositions, even when <paramref name="clip"/> is the clip already playing.
    /// <para>
    /// Played at <see cref="FrameIndex"/> and <see cref="FrameTick"/>, a variant clip keeps the
    /// frame and the ticks spent on it even when its frames hold for different ticks. The variant's
    /// frame then holds for its own remaining ticks. A frame tick equal to a non-looping clip's last
    /// frame ticks lands finished on that frame, as <see cref="FrameTick"/> reads once such a clip
    /// finishes.
    /// </para>
    /// </remarks>
    /// <example>
    /// Swapping a walk for its shooting variant mid-stride:
    /// <code>
    /// _animator.PlayAtFrame(walkShooting, _animator.FrameIndex, _animator.FrameTick);
    /// </code>
    /// </example>
    /// <param name="clip">The clip to play.</param>
    /// <param name="frameIndex">The frame to draw, counted from 0.</param>
    /// <param name="frameTick">Ticks already spent on that frame. Not negative, and less than that frame's ticks except on a non-looping clip's last frame, which also takes its full ticks.</param>
    public void PlayAtFrame(SpriteClip clip, int frameIndex, int frameTick)
    {
        ArgumentNullException.ThrowIfNull(clip);

        ReadOnlySpan<int> frameTicks = clip.FrameTicks;
        if ((uint)frameIndex >= (uint)frameTicks.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameIndex),
                frameIndex,
                $"The clip has {frameTicks.Length} frames. Pass a frame index from 0 to {frameTicks.Length - 1}.");
        }

        int hold = frameTicks[frameIndex];
        bool finished = !clip.Loop && frameIndex == frameTicks.Length - 1 && frameTick == hold;
        if (frameTick < 0 || (frameTick >= hold && !finished))
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameTick),
                frameTick,
                clip.Loop || frameIndex != frameTicks.Length - 1
                    ? $"Frame {frameIndex} holds for {hold} ticks. Pass a frame tick from 0 to {hold - 1}."
                    : $"The last frame holds for {hold} ticks. Pass a frame tick from 0 to {hold}, where {hold} lands finished.");
        }

        Clip = clip;
        _playback.SeekFrame(frameIndex, frameTick, finished);
        Reposition(clip);
    }

    private void Reposition(SpriteClip clip)
    {
        _pendingStart = true;
        _startedOnTick = Entity?.SceneOrNull?.SteppingTick;
        _renderer.Sprite = clip.Frames[_playback.FrameIndex];
    }

    /// <summary>Rewinds to <see cref="Clip"/>'s first frame, keeps the clip and clears <see cref="Paused"/>. A reused entity replays it as a new one would.</summary>
    /// <inheritdoc/>
    protected internal override void OnRemovedFromScene()
    {
        Paused = false;
        if (Clip is not { } clip)
        {
            return;
        }

        _playback.Restart();
        _pendingStart = true;
        _startedOnTick = null;
        _renderer.Sprite = clip.Frames[0];
    }

    /// <inheritdoc/>
    protected internal override void OnStep(in StepContext context)
    {
        if (Clip is not { } clip)
        {
            return;
        }

        // A played frame is on screen for the whole pause. A paused step therefore spends the step
        // Play would otherwise hold back.
        if (Paused)
        {
            _pendingStart = false;
            return;
        }

        // The frame Play chose is drawn for the tick Play ran in, so that tick spends no step on it.
        // Advancing here would retire a one-tick first frame before any frame view saw it. The check
        // is keyed on the tick, not on having stepped since, because a component stepped after this
        // one reaches the animator on the following step, and its Play belongs to the earlier tick.
        // A Play outside a step belongs to no tick and is spent on the next one.
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

    // A clip has no name, so playback is identified by its frames' span of the sheet and the frame's
    // place in that span.
    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("Playing", Clip is not null);
        if (Clip is not { } clip)
        {
            return;
        }

        TextureRegion first = clip.Frames[0].Region;
        TextureRegion last = clip.Frames[^1].Region;
        panel.Field(
            "Clip",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{clip.Frames.Length} frames, ({first.X}, {first.Y}) to ({last.X}, {last.Y})"));
        panel.Field("Frame", string.Create(CultureInfo.InvariantCulture, $"{FrameIndex} of {clip.Frames.Length}"));
        panel.Field("Tick", Tick);
        panel.Field("Loop", clip.Loop);
        panel.Field("IsFinished", IsFinished);
        panel.Field("Paused", Paused);
        panel.Command("Restart", () => Play(clip, restart: true));
    }
}
