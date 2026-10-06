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
    private float _speed = 1f;

    // The fraction of a tick Speed has advanced and no step has spent yet.
    private float _carry;

    // What the animator's most recent step reported, each name once. Reached reads only this set.
    private string[] _readable = [];
    private int _readableCount;

    // What a Play or the removal rewind reported since that step. The animator's next step makes it readable.
    private string[] _pending = [];
    private int _pendingCount;

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

    /// <summary>
    /// Whether the animator removes its entity from the scene when a non-looping clip finishes. False
    /// by default.
    /// </summary>
    /// <remarks>
    /// The animator calls <see cref="Scene.Remove(Entity)"/> on its own entity in the step that finds
    /// <see cref="IsFinished"/>, and the removal lands at that step's end. The last frame draws for its
    /// own ticks and no longer. The entity leaves with its subtree, and its parent stays. A pooled entity
    /// returns to its <see cref="EntityPool{T}"/> as any removal returns it. Removal rewinds the clip,
    /// and the reused entity replays it from its first frame. The setting itself survives removal.
    /// <para>
    /// A looping clip never finishes and never removes its entity. A <see cref="Paused"/> animator keeps
    /// its entity in the scene until it is unpaused. An entity held by <see cref="Scene.Freeze(int)"/>
    /// or <see cref="Scene.Paused"/> does not step its animator. Its clip finishes, and the entity
    /// leaves, as many steps later as the hold lasted.
    /// </para>
    /// </remarks>
    /// <example>
    /// A pooled impact that plays once and leaves:
    /// <code>
    /// _animator = new SpriteAnimator(sprite) { RemovesEntityWhenFinished = true };
    /// Add(_animator);
    /// _animator.Play(CapsuleAssets.Sprites.EffectsSheet.Clips.Impact);
    /// </code>
    /// </example>
    public bool RemovesEntityWhenFinished { get; set; }

    /// <summary>How many clip ticks each step advances. One by default, and zero holds the frame.</summary>
    /// <remarks>
    /// A fraction of a tick carries over to later steps. The carry restarts on every <c>Play</c> and
    /// on removal from the scene. <see cref="Tick"/> and <see cref="FrameTick"/> stay whole ticks. A
    /// frame crossed inside one step is never drawn and never places its boxes. Above one, an attack's
    /// one-tick active frame can be skipped. <see cref="Paused"/> holds the clip whatever the speed.
    /// Removal from the scene keeps the speed, unlike <see cref="Paused"/>. Reverse playback is a
    /// reversed clip.
    /// </remarks>
    public float Speed
    {
        get => _speed;

        set
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Speed is finite and not negative. Play a reversed clip to run an animation backwards.");
            }

            _speed = value;
        }
    }

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
    /// Whether playback reached a frame raising the event <paramref name="name"/> recently enough to
    /// read it on this step.
    /// </summary>
    /// <remarks>
    /// A frame's events are reported when stepping enters it. That is every frame a step crosses, and
    /// frame 0 again on every loop wrap. A step crossing several passes of a loop reports each event
    /// once. Any <c>Play</c> reports the frame it lands on when it lands on that frame's first tick.
    /// Landing part-way through a frame reports nothing. Playing the clip already playing without a
    /// restart reports nothing.
    /// <para>
    /// Every report is readable from the animator step that makes it until the animator's next step.
    /// A <c>Play</c> reports toward the animator's next step, which is this tick's step when the
    /// animator has not stepped yet. A reader that steps after the animator, such as a late step or a
    /// component attached after it, sees the event on that step. A reader that steps before the
    /// animator sees it one step later. No reader misses it or sees it twice. An animator held by a
    /// freeze or pause of its entity does not step, and its reports stay readable until it does.
    /// Removal from the scene clears every report and rewinds to frame 0. The rewind reports that
    /// frame toward the animator's first step after it rejoins a scene.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// if (_animator.Reached(CapsuleAssets.Sprites.Actors.PlayerSheet.Events.Footstep))
    /// {
    ///     _footstep.Play();
    /// }
    /// </code>
    /// </example>
    /// <param name="name">The event's name as the sheet declared it. The sheet's generated <c>Events</c> class lists them.</param>
    /// <returns>Whether the event was reported and is still readable.</returns>
    public bool Reached(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return Holds(_readable, _readableCount, name);
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
    /// finishes. A variant swap landing on a frame's first tick reports that frame's events again.
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
        _carry = 0f;

        if (_playback.TicksElapsed == 0 && !_playback.IsFinished)
        {
            Report(clip.EventsAt(_playback.FrameIndex), ref _pending, ref _pendingCount);
        }

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
        _carry = 0f;
        Array.Clear(_readable, 0, _readableCount);
        _readableCount = 0;
        Array.Clear(_pending, 0, _pendingCount);
        _pendingCount = 0;
        if (Clip is not { } clip)
        {
            return;
        }

        _playback.Restart();
        _pendingStart = true;
        _startedOnTick = null;
        Report(clip.EventsAt(0), ref _pending, ref _pendingCount);
        _renderer.Sprite = clip.Frames[0];
    }

    /// <inheritdoc/>
    protected internal override void OnStep(in StepContext context)
    {
        // A paused or clipless step still rolls the reports over. The buffers swap to stay allocation-free.
        (_readable, _pending) = (_pending, _readable);
        (_readableCount, _pendingCount) = (_pendingCount, _readableCount);
        Array.Clear(_pending, 0, _pendingCount);
        _pendingCount = 0;

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

        float advance = _carry + _speed;
        float whole = MathF.Floor(advance);
        _carry = advance - whole;

        int drawn = _playback.FrameIndex;
        Advance(clip, whole);

        // Only a new frame is written. Writing re-reads the frame's sockets and boxes.
        if (_playback.FrameIndex != drawn)
        {
            _renderer.Sprite = clip.Frames[_playback.FrameIndex];
        }

        if (RemovesEntityWhenFinished && _playback.IsFinished)
        {
            Entity!.Scene.Remove(Entity);
        }
    }

    // Steps the cursor a tick at a time, reporting each frame it enters. Whole passes of a loop are
    // reported once and skipped, which bounds the work to one pass whatever the speed.
    private void Advance(SpriteClip clip, float ticks)
    {
        if (ticks < 1f)
        {
            return;
        }

        ReadOnlySpan<int> frameTicks = clip.FrameTicks;
        long steps = 1;
        if (ticks > 1f)
        {
            long total = AnimationPlayback.ValidatedTotal(frameTicks);
            if (clip.Loop && ticks >= total)
            {
                for (int frame = 0; frame < frameTicks.Length; frame++)
                {
                    Report(clip.EventsAt(frame), ref _readable, ref _readableCount);
                }

                steps = (long)((double)ticks % total);
            }
            else
            {
                steps = ticks >= total ? total : (long)ticks;
            }
        }

        for (long step = 0; step < steps && !_playback.IsFinished; step++)
        {
            _playback.Step(frameTicks, clip.Loop);

            // A step leaves no ticks spent only on the frame it just entered.
            if (_playback.TicksElapsed == 0)
            {
                Report(clip.EventsAt(_playback.FrameIndex), ref _readable, ref _readableCount);
            }
        }
    }

    private static void Report(ReadOnlySpan<string> events, ref string[] names, ref int count)
    {
        foreach (string name in events)
        {
            if (Holds(names, count, name))
            {
                continue;
            }

            if (count == names.Length)
            {
                Array.Resize(ref names, Math.Max(names.Length * 2, count + events.Length));
            }

            names[count++] = name;
        }
    }

    // Generated names are interned, and the ordinal comparison usually matches on reference first.
    private static bool Holds(string[] names, int count, string name)
    {
        for (int index = 0; index < count; index++)
        {
            if (string.Equals(names[index], name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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
        panel.Field("Speed", Speed);
        panel.Command("Restart", () => Play(clip, restart: true));
    }
}
