using System.Globalization;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Animation;

/// <summary>
/// Plays a <see cref="ValueClip{T}"/> on the fixed step and hands its current value to the setter
/// given at construction.
/// </summary>
/// <remarks>
/// Playback advances on ticks, and the value an entity is on is simulation state. The setter runs on
/// every <c>Play</c> and then whenever the value changes. Code setting the same property elsewhere is
/// overwritten on the setter's next run.
/// <para>
/// The animator never runs the setter on its own while its entity is out of a scene. Removal, including a
/// scene stopping, rewinds the clip without running it, and the first value reaches the setter as the
/// entity next joins a scene. Only a <c>Play</c> made out of a scene runs it there.
/// </para>
/// <para>
/// A component steps after its entity. An entity reading the animator in <see cref="Entity.OnStep"/>
/// sees the value the previous step set. Put logic that depends on the value set in a late step or in
/// a component attached after the animator.
/// </para>
/// </remarks>
/// <example>
/// A glow pulsing a material parameter until the pulse ends:
/// <code>
/// ValueAnimator&lt;float&gt; glow = new(row => material.Set("Row", row));
/// Add(glow);
/// glow.Play(CapsuleAssets.Clips.PulseClip);
/// yield return Wait.Until(() => glow.IsFinished);
/// </code>
/// </example>
/// <typeparam name="T">The type of value the clips it plays hold.</typeparam>
public sealed class ValueAnimator<T> : Component
{
    private readonly Action<T> _setter;

    private AnimationPlayback _playback;
    private bool _pendingStart;
    private long? _startedOnTick;
    private float _speed = 1f;

    // The fraction of a tick Speed has advanced and no step has spent yet.
    private float _carry;

    // The value class of the value last set, which a stepping clip's keys are compared by.
    private int _valueId;

    // Whether a rewind out of a scene still owes the setter the clip's first value, which it hears on joining one.
    private bool _rewound;

    /// <summary>The clip playing, or null until one is played.</summary>
    public ValueClip<T>? Clip { get; private set; }

    /// <summary>
    /// The value last handed to the setter. Reads the type's default until a clip plays, and the value before
    /// a removal until the entity joins a scene again.
    /// </summary>
    public T Value { get; private set; } = default!;

    /// <summary>
    /// Whether a non-looping clip has spent its last key's ticks. A looping clip never finishes, and
    /// neither does an animator with no clip to play.
    /// </summary>
    public bool IsFinished => _playback.IsFinished;

    /// <summary>Whether the clip holds its current value while the entity keeps stepping.</summary>
    /// <remarks>
    /// A held clip neither moves nor finishes. A held step spends the step a <c>Play</c> holds its first
    /// value for. <c>Play</c> leaves this as it is. Removal from the scene clears it. A pause menu holds
    /// whole entities with <see cref="Scene.Paused"/>.
    /// </remarks>
    public bool Paused { get; set; }

    /// <summary>Creates an animator handing each value it plays to <paramref name="setter"/>.</summary>
    /// <param name="setter">Receives the value on every <c>Play</c> and then whenever the value changes. A lambda capturing its target allocates once, here, and never per step.</param>
    public ValueAnimator(Action<T> setter)
    {
        ArgumentNullException.ThrowIfNull(setter);

        _setter = setter;
    }

    /// <summary>How many clip ticks each step advances. One by default, and zero holds the value.</summary>
    /// <remarks>
    /// A fraction of a tick carries over to later steps. The carry restarts on every <c>Play</c> and on
    /// removal from the scene. A key crossed inside one step is never handed to the setter.
    /// <see cref="Paused"/> holds the clip whatever the speed. Removal from the scene keeps the speed,
    /// unlike <see cref="Paused"/>. Reverse playback is a reversed clip.
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

    /// <summary>
    /// Plays <paramref name="clip"/> from its first key and hands that value to the setter
    /// immediately. The change shows on this step instead of the next.
    /// </summary>
    /// <remarks>
    /// The first key holds for exactly its own ticks, counted from the tick of this call wherever in the
    /// step it came from. Called outside a step, it holds from the next step. Called on an animator in
    /// no scene, it still hands the value over at once, and counts from the tick its entity joins one.
    /// <para>
    /// Playing the clip already playing does nothing unless <paramref name="restart"/> is true. A
    /// finished non-looping clip still counts as playing.
    /// </para>
    /// </remarks>
    /// <param name="clip">The clip to play.</param>
    /// <param name="restart">Whether to restart the clip when it is already playing.</param>
    public void Play(ValueClip<T> clip, bool restart = false)
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
        _carry = 0f;
        _rewound = false;
        Write(clip.Values[0], clip.ValueId(0));
    }

    /// <summary>
    /// Counts a clip played out of a scene from the tick the entity joins this one, and hands a clip rewound
    /// by a removal its first value.
    /// </summary>
    /// <inheritdoc/>
    protected internal override void OnAddedToScene()
    {
        if (_pendingStart && _startedOnTick is null)
        {
            _startedOnTick = Entity!.SceneOrNull!.SteppingTick;
        }

        if (_rewound && Clip is { } clip)
        {
            _rewound = false;
            Write(clip.Values[0], clip.ValueId(0));
        }
    }

    /// <summary>
    /// Rewinds to <see cref="Clip"/>'s first key, keeps the clip and clears <see cref="Paused"/>, without
    /// running the setter: it hears the first key as the entity joins a scene again. A reused entity replays
    /// the clip as a new one would.
    /// </summary>
    /// <inheritdoc/>
    protected internal override void OnRemovedFromScene()
    {
        Paused = false;
        _carry = 0f;
        if (Clip is null)
        {
            return;
        }

        _playback.Restart();
        _pendingStart = true;
        _startedOnTick = null;
        _rewound = true;
    }

    /// <inheritdoc/>
    protected internal override void OnStep(in StepContext context)
    {
        if (Clip is not { } clip)
        {
            return;
        }

        // A played value is set for the whole pause. A paused step spends the step Play holds back.
        if (Paused)
        {
            _pendingStart = false;
            return;
        }

        // The tick Play ran in spends no step on its key. A component stepped after this one plays
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
        if (whole < 1f || _playback.IsFinished)
        {
            return;
        }

        // Whole passes of a loop land where they began. Skipping them bounds the work to one pass
        // whatever the speed.
        long steps = whole == 1f ? 1
            : clip.Loop ? (long)((double)whole % clip.Duration)
            : (long)Math.Min(whole, clip.Duration);
        for (long step = 0; step < steps && !_playback.IsFinished; step++)
        {
            _playback.Step(clip.KeyTicks, clip.Loop);
        }

        // Nothing compares a T per step except a blending type, which compares without boxing.
        int key = _playback.FrameIndex;
        if (clip.IsLinear)
        {
            T value = clip.ValueAt(key, _playback.TicksElapsed);
            if (!clip.Same(value, Value))
            {
                Write(value);
            }
        }
        else if (clip.ValueId(key) != _valueId)
        {
            Write(clip.Values[key], clip.ValueId(key));
        }
    }

    private void Write(T value, int valueId = 0)
    {
        _valueId = valueId;
        Value = value;
        _setter(value);
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("Playing", Clip is not null);
        if (Clip is not { } clip)
        {
            return;
        }

        panel.Field("Clip", string.Create(CultureInfo.InvariantCulture, $"{clip.Values.Length} keys, {clip.Duration} ticks, {(clip.IsLinear ? "linear" : "stepped")}"));
        panel.Field("Key", string.Create(CultureInfo.InvariantCulture, $"{_playback.FrameIndex} of {clip.Values.Length}"));
        panel.Field("Value", Value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : Value?.ToString());
        panel.Field("Loop", clip.Loop);
        panel.Field("IsFinished", IsFinished);
        panel.Field("Paused", Paused);
        panel.Field("Speed", Speed);
        panel.Command("Restart", () => Play(clip, restart: true));
    }
}
