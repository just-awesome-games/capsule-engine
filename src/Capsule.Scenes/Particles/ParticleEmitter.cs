using System.Numerics;
using Capsule.Animation;
using Capsule.Diagnostics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Particles;

/// <summary>
/// A <see cref="Renderer"/> that draws a fixed pool of sprite particles simulated on the fixed step, one
/// <see cref="SpriteIntent"/> per live particle.
/// </summary>
/// <remarks>
/// Positions are in world units under a world root and canvas pixels under a screen root. In
/// <see cref="ParticleSpace.World"/> a particle moves on its own once spawned and does not follow the
/// entity. In <see cref="ParticleSpace.Local"/> it rides the entity's position, turn and mirror.
/// Neither space applies the entity's scale to a particle's motion or size.
/// <para>
/// The sign of the entity's world scale, <see cref="FlipX"/> and <see cref="FlipY"/> mirror each spawn: where
/// it starts, which way it launches and spins, and its frame. A particle keeps the mirror it spawned with.
/// Turning the entity round flips no particle already in the world.
/// </para>
/// <para>
/// The simulation is seeded from the run's seed, and two runs of one seed emit identical intents.
/// Particles draw in spawn order with the newest last. A spawn into a full pool replaces the live
/// particle nearest the end of its life and takes its place in that order.
/// </para>
/// </remarks>
/// <example>
/// An effect that outlives what asked for it is its own entity, removed once its last particle dies.
/// <code>
/// public sealed class SparkBurst : Entity
/// {
///     private readonly ParticleEmitter _emitter;
///
///     public SparkBurst(Vector2 position)
///         : base(position)
///     {
///         _emitter = new ParticleEmitter(Sprite.White, capacity: 8)
///         {
///             Lifetime = (0.15f, 0.35f),
///             Speed = (60f, 140f),
///             Spread = 360f,
///             Gravity = new Vector2(0f, 300f),
///             Scale = (1f, 2f),
///             ScaleOverLifetime = Curve.Linear(1f, 0f),
///             Color = Gradient.Linear(ColorRgba.Yellow, new ColorRgba(255, 255, 0, 0)),
///             Blend = BlendMode.Additive,
///             RemovesEntityWhenFinished = true,
///         };
///         Add(_emitter);
///         _emitter.Emit(6);
///     }
/// }
/// </code>
/// </example>
public sealed class ParticleEmitter : Renderer
{
    // The top byte is set. A particle stream then never collides with a stream a game mints from its own
    // small integer domain.
    private const ulong ParticleStreamBase = 0xFF00_0000_0000_0000UL;
    private const float DefaultDeltaSeconds = 1f / StepContext.DefaultStepHertz;

    private readonly Particle[] _particles;
    private readonly SpriteClip _clip;
    private readonly float _boundsRadius;

    // Built with the emitter and seeded as it starts. An emitter built ahead for a pool then starts without allocating.
    private readonly RandomSource _random = new();
    private ulong _particleStream;

    // Live particles fill the front of the pool in spawn order.
    private int _alive;
    private bool _prewarmed;
    private bool _prewarming;
    private bool _started;

    // Set from joining a scene to leaving it. A queued add can give the entity its scene before this.
    private bool _inScene;

    // Whether a particle has spawned in this life. A burst finishes only after it has.
    private bool _spawned;

    // The frame the next particle takes under FrameMode.InOrderAtSpawn, from the first in each life.
    private int _nextFrame;
    private float _lastDeltaSeconds = DefaultDeltaSeconds;
    private float _rateAccumulator;
    private float _distanceAccumulator;

    // An Emit before OnStart has no transform or random source yet, and one out of a scene has no stream
    // for its next life. OnStart or the next join spawns each waiting call in order, at its own point. A
    // call spawns at least one particle, and a pool holds as many calls as particles. A call past that
    // joins the last.
    private readonly PendingEmit[] _pending;
    private int _pendingCalls;

    // The box over every live particle's previous and current position, inflated only when Bounds is
    // read. Each step rebuilds it and every spawn extends it.
    private bool _hasBounds;
    private Vector2 _boundsMin;
    private Vector2 _boundsMax;

    /// <summary>The pool's fixed size, set at construction.</summary>
    public int Capacity { get; }

    /// <summary>Where live particles are kept, <see cref="ParticleSpace.World"/> by default and fixed at construction.</summary>
    public ParticleSpace Space { get; init; }

    /// <summary>The point in the entity's own space the emit shape is centred on, placed as <see cref="SpriteRenderer.Offset"/> is. Zero by default.</summary>
    public Vector2 Offset { get; set; }

    /// <summary>Where a particle starts, about <see cref="Offset"/>. <see cref="EmitShape.Point"/> by default.</summary>
    public EmitShape Shape { get; set; }

    /// <summary>
    /// The launch cone's centre line in the entity's own space, <see cref="Vector2.UnitX"/> by default.
    /// Zero launches uniformly over the full circle.
    /// </summary>
    /// <remarks>
    /// Any non-zero length works. The entity's world rotation and the sign of its world scale turn and
    /// mirror the direction at spawn.
    /// </remarks>
    public Vector2 Direction { get; set; } = Vector2.UnitX;

    /// <summary>The cone's full width about <see cref="Direction"/>, in degrees, drawn uniformly. Zero launches along <see cref="Direction"/>.</summary>
    public float Spread
    {
        get;
        set => field = Guard.InRange(value, 0f, 360f, nameof(value));
    }

    /// <summary>The launch speed, in units per second along the drawn direction. Zero by default.</summary>
    public FloatRange Speed { get; set; }

    /// <summary>
    /// Speed added at launch along the line from the shape's centre to the spawn point, in units per
    /// second with negative inward, zero by default.
    /// </summary>
    /// <remarks>A particle spawned on the shape's centre gets none.</remarks>
    public FloatRange RadialSpeed { get; set; }

    /// <summary>
    /// Seconds a particle lives, one by default. The drawn value rounds up to whole steps at spawn, one
    /// step at least.
    /// </summary>
    /// <remarks>Under <see cref="FrameMode.Clip"/> a non-looping clip ends a particle sooner if it runs out first.</remarks>
    public FloatRange Lifetime { get; set; } = new(1f, 1f);

    /// <summary>
    /// A shape in the entity's own space where a particle dies in the step that moves it inside or onto its edge,
    /// before it draws there. Null by default, which kills none.
    /// </summary>
    /// <remarks>
    /// Each step places it as <see cref="Offset"/> is, in either <see cref="Space"/>: it follows the entity's
    /// position, turn, mirror and scale, and <see cref="FlipX"/> and <see cref="FlipY"/> mirror it, though the
    /// entity's scale still sizes no particle's motion. A particle spawned inside it draws there until its first
    /// step.
    /// </remarks>
    public Shape2D? KillZone
    {
        get;
        set
        {
            if (value is { } shape)
            {
                Shape2D.RequireBuilt(shape, nameof(value));
            }

            field = value;
        }
    }

    /// <summary>Acceleration on every live particle, in units per second squared along world directions in either <see cref="Space"/>, zero by default.</summary>
    public Vector2 Gravity { get; set; }

    /// <summary>Velocity drag per second, applied as <c>velocity *= max(0, 1 - Damping * dt)</c> each step. Zero by default.</summary>
    public float Damping
    {
        get;
        set => field = Guard.NonNegative(value, nameof(value));
    }

    /// <summary>The spawn rotation about the frame's pivot, in degrees. Zero by default.</summary>
    public FloatRange Rotation { get; set; }

    /// <summary>The spin drawn at spawn, in degrees per second. Zero by default.</summary>
    public FloatRange AngularVelocity { get; set; }

    /// <summary>The spawn multiplier on the frame's texel size. One by default.</summary>
    public FloatRange Scale { get; set; } = new(1f, 1f);

    /// <summary>
    /// A factor on the spawn <see cref="Scale"/>, read at the particle's age fraction from 0 to 1.
    /// A constant one by default.
    /// </summary>
    /// <remarks>A default <see cref="Curve"/> reads zero and hides every particle.</remarks>
    public Curve ScaleOverLifetime { get; set; } = Curve.Constant(1f);

    /// <summary>The tint read at the particle's age fraction from 0 to 1. White by default.</summary>
    public Gradient Color { get; set; }

    /// <summary>How a particle plays the constructor's clip, <see cref="Particles.FrameMode.Clip"/> by default.</summary>
    /// <remarks>A new value reaches live particles on their next draw. A particle's life is fixed at its spawn.</remarks>
    public FrameMode FrameMode { get; set; }

    /// <summary>
    /// Whether each new particle is mirrored left to right about the entity's origin, as a negative X scale on
    /// the entity would mirror it: <see cref="Offset"/>, the <see cref="Shape"/>, <see cref="Direction"/>, its spin
    /// and its frame.
    /// </summary>
    /// <remarks><see cref="Gravity"/> is a world direction and never mirrors. A live particle keeps the mirror it spawned with.</remarks>
    public bool FlipX { get; set; }

    /// <summary>Whether each new particle is mirrored top to bottom about the entity's origin, as <see cref="FlipX"/> mirrors it left to right.</summary>
    public bool FlipY { get; set; }

    /// <summary>
    /// The fraction of the emitter's own velocity that a particle spawned by <see cref="Rate"/> or
    /// <see cref="RateOverDistance"/> inherits, zero by default.
    /// </summary>
    /// <remarks>It has no effect in <see cref="ParticleSpace.Local"/>, where particles already move with the entity.</remarks>
    public float InheritVelocity
    {
        get;
        set => field = Guard.Finite(value, nameof(value));
    }

    /// <summary>How every particle blends with what is already drawn. <see cref="BlendMode.Alpha"/> by default.</summary>
    public BlendMode Blend { get; set; }

    /// <summary>
    /// Whether <see cref="Rate"/> and <see cref="RateOverDistance"/> spawn, true by default.
    /// <see cref="Emit(int)"/> spawns either way.
    /// </summary>
    public bool Emitting { get; set; } = true;

    /// <summary>Particles spawned per second while <see cref="Emitting"/>, zero by default. A fractional remainder carries to the next step.</summary>
    public float Rate
    {
        get;
        set => field = Guard.NonNegative(value, nameof(value));
    }

    /// <summary>
    /// Particles spawned per unit the entity travels in the world while <see cref="Emitting"/>, in either
    /// <see cref="Space"/>, zero by default.
    /// </summary>
    /// <remarks>A fractional remainder carries to the next step.</remarks>
    public float RateOverDistance
    {
        get;
        set => field = Guard.NonNegative(value, nameof(value));
    }

    /// <summary>
    /// Seconds of <see cref="Rate"/> emission simulated in the emitter's first step. Zero, the default,
    /// starts empty.
    /// </summary>
    /// <remarks>
    /// Read once, on the first step after the emitter joins a scene, and only while <see cref="Emitting"/>.
    /// The prewarm holds the emitter still. <see cref="RateOverDistance"/> spawns nothing during it.
    /// </remarks>
    public float PrewarmSeconds
    {
        get;
        set => field = Guard.NonNegative(value, nameof(value));
    }

    /// <summary>
    /// Whether the emitter removes its entity from the scene when it finishes. False by default.
    /// </summary>
    /// <remarks>
    /// The emitter calls <see cref="Scene.Remove(Entity)"/> on its own entity in the step that finds
    /// <see cref="IsFinished"/>, and the removal lands at that step's end. A pooled entity returns to its
    /// <see cref="EntityPool{T}"/> as any removal returns it, and starts unfinished when reused. The
    /// setting survives removal.
    /// <para>
    /// An emitter that spawns on its own never removes its entity while <see cref="Emitting"/>. A hold by
    /// <see cref="Scene.Freeze(int)"/> or <see cref="Scene.Paused"/> delays the removal by as many steps as
    /// the hold lasts.
    /// </para>
    /// </remarks>
    public bool RemovesEntityWhenFinished { get; set; }

    /// <summary>Creates an emitter whose particles draw one still frame for their whole life.</summary>
    /// <param name="sprite">The frame every particle draws.</param>
    /// <param name="capacity">The pool's fixed size, the most particles alive at once.</param>
    public ParticleEmitter(Sprite sprite, int capacity)
        : this(new SpriteClip([sprite], [1], loop: true), capacity)
    {
    }

    /// <summary>Creates an emitter whose particles draw <paramref name="clip"/>'s frames as <see cref="FrameMode"/> says.</summary>
    /// <param name="clip">The frames every particle draws, each held for its own steps by default.</param>
    /// <param name="capacity">The pool's fixed size, the most particles alive at once.</param>
    public ParticleEmitter(SpriteClip clip, int capacity)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);

        _clip = clip;
        _boundsRadius = LargestHalfDiagonal(clip.Frames);
        Capacity = capacity;
        _particles = new Particle[capacity];
        _pending = new PendingEmit[capacity];
    }

    /// <summary>How many particles are alive now.</summary>
    public int Alive => _alive;

    /// <summary>
    /// Whether the emitter has spawned a particle, none is alive, and none will spawn without an
    /// <see cref="Emit(int)"/>: <see cref="Emitting"/> is false, or <see cref="Rate"/> and
    /// <see cref="RateOverDistance"/> are zero.
    /// </summary>
    /// <remarks>Removal from the scene clears it. A reused emitter finishes only after it spawns again.</remarks>
    public bool IsFinished => _spawned && _alive == 0 && (!Emitting || (Rate == 0f && RateOverDistance == 0f));

    /// <summary>
    /// The rect covering every live particle's last move, grown by the largest frame's reach at its
    /// largest scale. Empty with nothing alive.
    /// </summary>
    public override Rect Bounds
    {
        get
        {
            if (!_hasBounds)
            {
                return default;
            }

            Vector2 min = _boundsMin;
            Vector2 max = _boundsMax;
            if (Space == ParticleSpace.Local)
            {
                // The local box placed through both frames covers every position a draw interpolates.
                PlaceBox(Frame(PreviousRenderTransform), Frame(RenderTransform), ref min, ref max);
            }

            return Inflate(min, max, _boundsRadius * MathF.Max(-Scale.Min, Scale.Max) * ScaleOverLifetime.Reach);
        }
    }

    /// <summary>
    /// Spawns <paramref name="count"/> particles now, at <see cref="Shape"/> about <see cref="Offset"/>,
    /// placed by the entity's transform at this call.
    /// </summary>
    /// <remarks>
    /// Before the emitter has started, or while it is out of a scene, the spawn waits and is placed when it
    /// starts or joins a scene.
    /// <para>On a ring <see cref="Shape"/> the particles spread evenly round the rim from one drawn start.</para>
    /// </remarks>
    /// <param name="count">How many to spawn. Zero or fewer spawns nothing.</param>
    public void Emit(int count) => Emit(count, Offset);

    /// <summary>
    /// Spawns <paramref name="count"/> particles now, at <see cref="Shape"/> centred on
    /// <paramref name="at"/> in the entity's own space. On an unmoved root entity, <paramref name="at"/>
    /// is a world point.
    /// </summary>
    /// <remarks>
    /// Before the emitter has started, or while it is out of a scene, the spawn waits and is placed at the
    /// transform the entity starts or joins with. Waiting calls spawn in call order, each at its own point,
    /// up to <see cref="Capacity"/> calls. A call past that adds its count to the last waiting call and
    /// spawns at that call's point.
    /// <para>On a ring <see cref="Shape"/> the particles spread evenly round the rim from one drawn start.</para>
    /// </remarks>
    /// <param name="count">How many to spawn. Zero or fewer spawns nothing.</param>
    /// <param name="at">Where the shape is centred, instead of <see cref="Offset"/>.</param>
    public void Emit(int count, Vector2 at)
    {
        if (count <= 0)
        {
            return;
        }

        if (!_started || !_inScene)
        {
            if (_pendingCalls == _pending.Length)
            {
                PendingEmit last = _pending[_pendingCalls - 1];
                _pending[_pendingCalls - 1] = last with { Count = last.Count + count };
            }
            else
            {
                _pending[_pendingCalls++] = new PendingEmit(count, at);
            }

            return;
        }

        SpawnImmediate(count, at);
    }

    /// <summary>
    /// Removes every live particle, drops the fractional <see cref="Rate"/> and
    /// <see cref="RateOverDistance"/> remainders, and cancels every <see cref="Emit(int)"/> call waiting
    /// for the emitter to start or join a scene.
    /// </summary>
    public void Clear()
    {
        _alive = 0;
        _pendingCalls = 0;
        _rateAccumulator = 0f;
        _distanceAccumulator = 0f;
        _hasBounds = false;
    }

    /// <inheritdoc/>
    protected internal override void OnAddedToScene()
    {
        base.OnAddedToScene();

        // Reserved in scene-add order, which does not depend on when the scene starts. Run is not
        // readable yet for an entity a scene document composes.
        _particleStream = Entity!.Scene.NextParticleStream();
        _inScene = true;

        // A rejoining emitter does not start again. It draws its new stream from the start, as a new emitter
        // would, and the spawn it queued while out of the scene is the first draw.
        if (_started)
        {
            _random.Reset(Entity.Scene.RunOrNull?.Random.Seed ?? _random.Seed, ParticleStreamBase ^ _particleStream);
            SpawnPending();
        }
    }

    /// <inheritdoc/>
    protected internal override void OnStart()
    {
        base.OnStart();

        _random.Reset(Run.Random.Seed, ParticleStreamBase ^ _particleStream);
        _started = true;
        SpawnPending();
    }

    /// <summary>Clears every particle and the prewarm. A reused emitter starts its next life as a new one would.</summary>
    /// <inheritdoc/>
    protected internal override void OnRemovedFromScene()
    {
        base.OnRemovedFromScene();

        Clear();
        _inScene = false;
        _spawned = false;
        _nextFrame = 0;
        _prewarmed = false;
        _lastDeltaSeconds = DefaultDeltaSeconds;
    }

    /// <inheritdoc/>
    protected internal override void OnStep(in StepContext context)
    {
        float dt = context.DeltaSeconds;
        _lastDeltaSeconds = dt;

        Transform2D current = RenderTransform;
        Transform2D previous = PreviousRenderTransform;

        if (!_prewarmed)
        {
            _prewarmed = true;

            // Prewarmed ticks hold the emitter at its current transform. The last is the real step below.
            if (PrewarmSeconds > 0f && Emitting && dt > 0f)
            {
                int ticks = Math.Max(1, (int)MathF.Ceiling(PrewarmSeconds / dt));
                _prewarming = true;
                for (int tick = 0; tick < ticks - 1; tick++)
                {
                    Step(dt, current, current);
                }

                _prewarming = false;
            }
        }

        Step(dt, previous, current);

        if (RemovesEntityWhenFinished && IsFinished)
        {
            Entity!.Scene.Remove(Entity);
        }
    }

    /// <inheritdoc/>
    protected internal override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        bool culls = view.TryGetCullRegion(out Rect region);
        Rect bounds = Bounds;
        bool unculled = !culls || Contains(region, bounds);

        if (!unculled && (bounds.IsEmpty || !bounds.Intersects(region)))
        {
            return;
        }

        SpriteClip clip = _clip;
        ReadOnlySpan<Sprite> frames = clip.Frames;
        FrameMode mode = frames.Length == 1 ? FrameMode.RandomAtSpawn : FrameMode;
        Curve scaleOverLifetime = ScaleOverLifetime;
        Gradient color = Color;

        // A held emitter skips its step, and a particle's last motion would otherwise replay on every
        // frame of the hold. Only a particle spawned before this step began draws still.
        bool held = Entity!.Held;
        int step = Entity.Scene.StepsBegun;

        bool local = Space == ParticleSpace.Local;
        Transform2D previousFrame = default;
        Transform2D currentFrame = default;
        float previousTurn = 1f;
        float currentTurn = 1f;
        bool frameFlipX = false;
        bool frameFlipY = false;
        if (local)
        {
            previousFrame = Frame(PreviousRenderTransform);
            currentFrame = Frame(RenderTransform);

            // A mirror conjugates a particle's own turn, as it does a child entity's.
            previousTurn = previousFrame.Mirrored ? -1f : 1f;
            currentTurn = currentFrame.Mirrored ? -1f : 1f;
            frameFlipX = currentFrame.Scale.X < 0f;
            frameFlipY = currentFrame.Scale.Y < 0f;
        }

        for (int index = 0; index < _alive; index++)
        {
            ref readonly Particle particle = ref _particles[index];
            bool still = held && particle.SpawnStep != step;

            float t = (float)particle.Age / particle.Lifetime;
            Sprite frame = frames[mode switch
            {
                FrameMode.Clip => clip.FrameIndexAt(particle.Age),
                FrameMode.OverLife => clip.FrameIndexAt((int)((long)particle.Age * clip.Duration / particle.Lifetime)),
                _ => particle.SpriteIndex,
            }];
            float scale = particle.Scale * scaleOverLifetime.Evaluate(t);
            Vector2 size = new Vector2(frame.Region.Width, frame.Region.Height) * scale;

            Vector2 previousPosition = still ? particle.Position : particle.PreviousPosition;
            Vector2 position = particle.Position;
            float previousRotation = still ? particle.Rotation : particle.PreviousRotation;
            float rotation = particle.Rotation;
            if (local)
            {
                previousPosition = previousFrame.TransformPoint(previousPosition);
                position = currentFrame.TransformPoint(position);
                previousRotation = previousFrame.Rotation + (previousTurn * previousRotation);
                rotation = currentFrame.Rotation + (currentTurn * rotation);
            }

            SpriteIntent intent = new(
                frame,
                previousPosition,
                position,
                previousRotation,
                rotation,
                size,
                particle.FlipX != frameFlipX,
                particle.FlipY != frameFlipY,
                color.Evaluate(t),
                Blend);

            if (unculled)
            {
                view.AddUnculled(in intent);
            }
            else
            {
                view.Add(in intent);
            }
        }
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("Alive", Alive);
        panel.Field("Capacity", Capacity);
        panel.Field("Rate", Rate);
        panel.Toggle("Emitting", Emitting, on => Emitting = on);
        panel.Command("Emit 10", () => Emit(10));

        base.OnDebugPanel(panel);
    }

    // Ages, integrates and spawns one tick, real or prewarmed.
    private void Step(float dt, in Transform2D previous, in Transform2D current)
    {
        _hasBounds = false;
        bool local = Space == ParticleSpace.Local;
        Transform2D turn = Turn(current);

        // Gravity pulls a world direction. A local particle takes it in the entity's current frame.
        Vector2 gravityStep = local ? turn.InverseTransformPoint(Gravity * dt) : Gravity * dt;
        float drag = MathF.Max(0f, 1f - (Damping * dt));

        // The kill zone tests a particle in the entity's own space, through the frame that places Offset.
        bool kills = KillZone.HasValue;
        Shape2D zone = KillZone.GetValueOrDefault();
        Transform2D zoneFrame = default;
        if (kills)
        {
            zoneFrame = local
                ? new Transform2D(Vector2.Zero, 0f, Vector2.Abs(current.Scale) * Flip)
                : current.With(current.Position, current.Scale * Flip);
        }

        // Survivors move down over the dead in order, one run of them at a time. Draw order never
        // changes between steps.
        // A particle spawned this step, such as by the entity's own OnStep, first draws as spawned.
        int step = Entity!.Scene.StepsBegun;
        int live = 0;
        int run = 0;
        for (int index = 0; index < _alive; index++)
        {
            ref Particle particle = ref _particles[index];
            if (particle.SpawnStep == step)
            {
                ExtendBounds(particle.PreviousPosition, particle.Position);
                continue;
            }

            particle.Age++;
            if (particle.Age < particle.Lifetime)
            {
                particle.PreviousPosition = particle.Position;
                particle.PreviousRotation = particle.Rotation;
                particle.Velocity += gravityStep;
                particle.Velocity *= drag;
                particle.Position += particle.Velocity * dt;
                particle.Rotation += particle.AngularVelocity;

                if (!kills || !zone.Contains(zoneFrame.InverseTransformPoint(particle.Position)))
                {
                    ExtendBounds(particle.PreviousPosition, particle.Position);
                    continue;
                }
            }

            // Expired or killed, it leaves the live set the same way.
            Array.Copy(_particles, run, _particles, live, index - run);
            live += index - run;
            run = index + 1;
        }

        Array.Copy(_particles, run, _particles, live, _alive - run);
        _alive = live + _alive - run;

        Vector2 displacement = current.Position - previous.Position;
        Vector2 emitterVelocity = dt > 0f && !local ? displacement / dt : Vector2.Zero;

        int rateCount = 0;
        int distanceCount = 0;
        if (Emitting)
        {
            _rateAccumulator += Rate * dt;
            rateCount = (int)MathF.Floor(MathF.Max(0f, _rateAccumulator));
            _rateAccumulator -= rateCount;

            _distanceAccumulator += RateOverDistance * displacement.Length();
            distanceCount = (int)MathF.Floor(MathF.Max(0f, _distanceAccumulator));
            _distanceAccumulator -= distanceCount;
        }

        int owed = rateCount + distanceCount;
        if (owed > 0)
        {
            // A local particle spawns in the entity's frame, which carries the move.
            Vector2 flip = Flip;
            Vector2 offset = Offset * flip;
            Vector2 previousCentre = local ? LocalCentre(offset, current) : previous.TransformPoint(offset);
            Vector2 currentCentre = local ? previousCentre : current.TransformPoint(offset);
            Transform2D launch = Launch(turn, flip);

            for (int index = 0; index < owed; index++)
            {
                float f = (index + 0.5f) / owed;
                SpawnOne(Shape.Sample(_random), previousCentre, currentCentre, emitterVelocity, f, dt, launch);
            }
        }
    }

    private void SpawnPending()
    {
        int calls = _pendingCalls;
        _pendingCalls = 0;
        for (int call = 0; call < calls; call++)
        {
            SpawnImmediate(_pending[call].Count, _pending[call].At);
        }
    }

    private void SpawnImmediate(int count, Vector2 at)
    {
        if (count <= 0)
        {
            return;
        }

        EmitShape shape = Shape;
        float ringStart = shape.IsRing ? shape.BurstStart(_random) : 0f;

        Transform2D current = RenderTransform;
        Vector2 flip = Flip;
        Vector2 flipped = at * flip;
        Vector2 centre = Space == ParticleSpace.Local ? LocalCentre(flipped, current) : current.TransformPoint(flipped);
        Transform2D launch = Launch(Turn(current), flip);

        for (int index = 0; index < count; index++)
        {
            Vector2 sample = shape.IsRing ? shape.BurstPoint(ringStart, index, count) : shape.Sample(_random);
            SpawnOne(sample, centre, centre, Vector2.Zero, 0.5f, _lastDeltaSeconds, launch);
        }
    }

    // Places one particle at fraction f of the centre's move, offset by its shape sample. The launch is
    // drawn in the entity's unit frame, and launch mirrors it and, in World space, turns it into the world.
    private void SpawnOne(Vector2 sample, Vector2 previousCentre, Vector2 currentCentre, Vector2 emitterVelocity, float f, float dt, in Transform2D launch)
    {
        Vector2 localDirection = Direction == Vector2.Zero
            ? Rotate(Vector2.UnitX, float.DegreesToRadians(_random.Range(0f, 360f)))
            : Rotate(Direction / Direction.Length(), float.DegreesToRadians(_random.Range(-Spread / 2f, Spread / 2f)));
        Vector2 velocity = localDirection * _random.Range(Speed);

        // Drawn only when set. An effect without it keeps its random stream.
        if (RadialSpeed != default)
        {
            float radialSpeed = _random.Range(RadialSpeed);
            if (sample != Vector2.Zero)
            {
                velocity += Vector2.Normalize(sample) * radialSpeed;
            }
        }

        Vector2 origin;
        if (Space == ParticleSpace.Local)
        {
            origin = currentCentre + (sample * launch.Scale);
            velocity *= launch.Scale;
        }
        else
        {
            origin = Vector2.Lerp(previousCentre, currentCentre, f) + launch.TransformPoint(sample);
            velocity = launch.TransformPoint(velocity) + (InheritVelocity * emitterVelocity);
        }

        float lifetimeSeconds = _random.Range(Lifetime);
        int lifetimeTicks = Math.Max(1, (int)MathF.Ceiling(lifetimeSeconds / MathF.Max(dt, 1e-6f)));
        if (FrameMode == FrameMode.Clip && !_clip.Loop)
        {
            lifetimeTicks = Math.Min(lifetimeTicks, _clip.Duration);
        }

        float rotationDegrees = _random.Range(Rotation);
        float angularVelocityDegreesPerSecond = _random.Range(AngularVelocity);
        float scale = _random.Range(Scale);

        int frameCount = _clip.Frames.Length;
        int spriteIndex = 0;
        if (frameCount > 1 && FrameMode == FrameMode.RandomAtSpawn)
        {
            spriteIndex = _random.Range(0, frameCount);
        }
        else if (FrameMode == FrameMode.InOrderAtSpawn)
        {
            spriteIndex = _nextFrame;
            _nextFrame = (_nextFrame + 1) % frameCount;
        }

        // A mirror conjugates the spin, as it does a child entity's turn.
        float spin = launch.Mirrored ? -1f : 1f;

        ref Particle particle = ref _particles[_alive < _particles.Length ? _alive++ : NearestEnd()];
        _spawned = true;

        particle.Velocity = velocity;
        particle.Rotation = spin * float.DegreesToRadians(rotationDegrees);
        particle.PreviousRotation = particle.Rotation;
        particle.AngularVelocity = spin * float.DegreesToRadians(angularVelocityDegreesPerSecond) * dt;
        particle.Age = 0;
        particle.Lifetime = lifetimeTicks;
        particle.Scale = scale;
        particle.SpriteIndex = spriteIndex;
        particle.FlipX = launch.Scale.X < 0f;
        particle.FlipY = launch.Scale.Y < 0f;
        particle.Position = origin + (velocity * (1f - f) * dt);
        particle.PreviousPosition = origin - (velocity * f * dt);
        // A prewarmed particle counts as spawned the step before. Later prewarm ticks then age it.
        particle.SpawnStep = (Entity!.SceneOrNull?.StepsBegun ?? 0) - (_prewarming ? 1 : 0);

        ExtendBounds(particle.PreviousPosition, particle.Position);
    }

    private void ExtendBounds(Vector2 previous, Vector2 current)
    {
        Vector2 lower = Vector2.Min(previous, current);
        Vector2 upper = Vector2.Max(previous, current);

        _boundsMin = _hasBounds ? Vector2.Min(_boundsMin, lower) : lower;
        _boundsMax = _hasBounds ? Vector2.Max(_boundsMax, upper) : upper;
        _hasBounds = true;
    }

    // A spawn into a full pool replaces the particle nearest the end of its life, the greatest
    // Age / Lifetime. It is the least visible loss, and a spawn is never silently swallowed.
    private int NearestEnd()
    {
        int nearestEnd = 0;
        float greatestFraction = -1f;
        for (int index = 0; index < _particles.Length; index++)
        {
            float fraction = (float)_particles[index].Age / _particles[index].Lifetime;
            if (fraction > greatestFraction)
            {
                greatestFraction = fraction;
                nearestEnd = index;
            }
        }

        return nearestEnd;
    }

    private Vector2 Flip => new(FlipX ? -1f : 1f, FlipY ? -1f : 1f);

    // What mirrors and turns a spawn. A local particle takes only the flip, since the entity's frame carries the rest.
    private Transform2D Launch(in Transform2D turn, Vector2 flip) =>
        Space == ParticleSpace.Local ? turn.With(Vector2.Zero, flip) : turn.With(Vector2.Zero, turn.Scale * flip);

    // The transform's position, turn and mirror at unit size: the frame a local particle moves in.
    private static Transform2D Frame(in Transform2D transform) => transform.With(transform.Position, Mirror(transform.Scale));

    // The transform's turn and mirror at unit size about the origin: the frame a launch is drawn in.
    private static Transform2D Turn(in Transform2D transform) => transform.With(Vector2.Zero, Mirror(transform.Scale));

    private static Vector2 Mirror(Vector2 scale) => new(scale.X < 0f ? -1f : 1f, scale.Y < 0f ? -1f : 1f);

    // The shape centre in the unit frame. A local particle there lands where a world spawn would.
    private static Vector2 LocalCentre(Vector2 at, in Transform2D transform) => at * Vector2.Abs(transform.Scale);

    // Replaces min and max with the world box of the local box's corners placed through both frames.
    private static void PlaceBox(in Transform2D previous, in Transform2D current, ref Vector2 min, ref Vector2 max)
    {
        Vector2 lower = min;
        Vector2 upper = max;
        min = new Vector2(float.PositiveInfinity);
        max = new Vector2(float.NegativeInfinity);

        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 point = new((corner & 1) == 0 ? lower.X : upper.X, (corner & 2) == 0 ? lower.Y : upper.Y);
            Vector2 placedPrevious = previous.TransformPoint(point);
            Vector2 placedCurrent = current.TransformPoint(point);
            min = Vector2.Min(min, Vector2.Min(placedPrevious, placedCurrent));
            max = Vector2.Max(max, Vector2.Max(placedPrevious, placedCurrent));
        }
    }

    private static Vector2 Rotate(Vector2 v, float radians)
    {
        float cos = DeterministicMath.Cos(radians);
        float sin = DeterministicMath.Sin(radians);

        return new Vector2((v.X * cos) - (v.Y * sin), (v.X * sin) + (v.Y * cos));
    }

    private static bool Contains(Rect outer, Rect inner) =>
        !inner.IsEmpty &&
        inner.Left >= outer.Left &&
        inner.Top >= outer.Top &&
        inner.Right <= outer.Right &&
        inner.Bottom <= outer.Bottom;

    private static Rect Inflate(Vector2 min, Vector2 max, float radius) =>
        Rect.FromEdges(min.X - radius, min.Y - radius, max.X + radius, max.Y + radius);

    private static float LargestHalfDiagonal(ReadOnlySpan<Sprite> sprites)
    {
        float max = 0f;
        foreach (Sprite sprite in sprites)
        {
            TextureRegion region = sprite.Region;
            max = MathF.Max(max, 0.5f * MathF.Sqrt((region.Width * region.Width) + (region.Height * region.Height)));
        }

        return max;
    }

    private struct Particle
    {
        internal Vector2 Position;
        internal Vector2 PreviousPosition;
        internal Vector2 Velocity;
        internal float Rotation;
        internal float PreviousRotation;
        internal float AngularVelocity;
        internal int Age;
        internal int Lifetime;
        internal float Scale;
        internal int SpriteIndex;
        internal bool FlipX;
        internal bool FlipY;

        // The scene's StepsBegun when this particle spawned.
        internal int SpawnStep;
    }

    // One Emit call waiting for the emitter to start or join a scene.
    private readonly record struct PendingEmit(int Count, Vector2 At);
}
