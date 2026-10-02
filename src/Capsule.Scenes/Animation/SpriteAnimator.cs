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
/// Playback advances on ticks, and the frame an entity is on is simulation state.
/// <para>
/// A component steps after its entity. An entity reading the animator in <see cref="Entity.OnStep"/>
/// sees the frame the previous step drew. Put logic that depends on the frame drawn in a component
/// attached after the animator.
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
    /// A held clip keeps drawing its frame, and <see cref="Tick"/> and <see cref="IsFinished"/> do
    /// not change. A held step spends the step a <c>Play</c> holds its first frame for. <c>Play</c>
    /// leaves this as it is. Removal from the scene clears it. Hit-stop and a pause menu hold whole
    /// entities with <see cref="Scene.Freeze(int)"/> and <see cref="Scene.Paused"/>.
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
    /// The first frame holds for exactly its own ticks, counted from the tick of this call wherever
    /// in the step it came from. Called outside a step, it holds from the next step. Called on an
    /// animator in no scene, it counts from the tick its entity joins one, as a pooled entity played
    /// and then added mid-step needs.
    /// <para>
    /// Playing the clip already playing does nothing unless <paramref name="restart"/> is true. A
    /// finished non-looping clip still counts as playing.
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
        Reposition(clip);
    }

    /// <summary>
    /// Plays <paramref name="clip"/> positioned as though it had started <paramref name="atTick"/>
    /// ticks ago, and draws that frame immediately. The change shows on this step instead of the
    /// next.
    /// </summary>
    /// <remarks>
    /// The frame reached holds for the rest of its ticks, counted from the tick of this call. A
    /// looping clip wraps the tick. A non-looping clip clamps a tick at or past its total to the last
    /// frame, finished. This overload always repositions, even onto the clip already playing.
    /// <para>
    /// Played at <see cref="Tick"/>, a variant with the same per-frame ticks continues from the
    /// frame the outgoing clip stood on. <see cref="PlayAtFrame"/> keeps the frame across clips of
    /// any shape.
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
    /// The frame holds for the rest of its own ticks, counted from the tick of this call. This method
    /// always repositions, even onto the clip already playing. A frame tick equal to a non-looping
    /// clip's last frame ticks lands finished, as <see cref="FrameTick"/> reads once such a clip
    /// finishes.
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

    /// <summary>Counts a clip played or rewound out of a scene from the tick the entity joins this one.</summary>
    /// <inheritdoc/>
    protected internal override void OnAddedToScene()
    {
        if (_pendingStart && _startedOnTick is null)
        {
            _startedOnTick = Entity!.SceneOrNull!.SteppingTick;
        }
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

        // A played frame is on screen for the whole pause. A paused step spends the step Play holds back.
        if (Paused)
        {
            _pendingStart = false;
            return;
        }

        // The tick Play ran in spends no step on its frame. A component stepped after this one plays
        // during an earlier tick than this step's. A Play outside a step belongs to the next tick.
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

    // A clip has no name. Its frames' span of the sheet identifies it.
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
