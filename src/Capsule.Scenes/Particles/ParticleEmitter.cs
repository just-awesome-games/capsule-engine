using System.Numerics;
using Capsule.Animation;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Particles;

/// <summary>
/// Draws a fixed pool of sprite particles simulated on the fixed step, one
/// <see cref="SpriteIntent"/> per live particle.
/// </summary>
/// <remarks>
/// Positions are in world units under a world root and canvas pixels under a screen root. In
/// <see cref="ParticleSpace.World"/> a particle moves on its own once spawned and does not follow the
/// entity. In <see cref="ParticleSpace.Local"/> it rides the entity's position, turn and mirror.
/// Neither space applies the entity's scale to a particle's motion or size.
/// <para>
/// The simulation is engine state seeded from the run's seed. A headless run emits exactly the
/// intents a windowed run draws, and two runs of one seed are identical. A spawn into a full pool
/// replaces the live particle nearest the end of its life.
/// </para>
/// <para>
/// Particles draw in spawn order with the newest last. A replacing spawn takes the replaced
/// particle's place in that order.
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
///         };
///         Add(_emitter);
///         _emitter.Emit(6);
///     }
///
///     protected override void OnStep(in StepContext context)
///     {
///         if (_emitter.Alive == 0)
///         {
///             Scene.Remove(this);
///         }
///     }
/// }
/// </code>
/// </example>
public sealed class ParticleEmitter : Renderer
{
    // The top byte set, so a particle stream never collides with a stream a game mints from its own
    // small integer domain.
    private const ulong ParticleStreamBase = 0xFF00_0000_0000_0000UL;
    private const float DefaultDeltaSeconds = 1f / StepContext.DefaultStepHertz;

    private readonly Particle[] _particles;
    private readonly Sprite _defaultSprite;

    private RandomSource _random = null!;
    private ulong _particleStream;
    private ReadOnlyMemory<Sprite> _sprites;
    private float _boundsRadius;
    private float _spread;
    private float _damping;
    private float _inheritVelocity;
    private float _rate;
    private float _rateOverDistance;
    private float _prewarmSeconds;

    // Live particles fill the front of the pool in spawn order.
    private int _alive;
    private bool _prewarmed;
    private bool _prewarming;
    private bool _started;
    private float _lastDeltaSeconds = DefaultDeltaSeconds;
    private float _rateAccumulator;
    private float _distanceAccumulator;

    // An Emit before OnStart has no transform or random source yet. OnStart places it.
    private int _pendingCount;
    private Vector2 _pendingAt;

    // The box over every live particle's previous and current position, inflated only when Bounds is
    // read. Each step rebuilds it and every spawn extends it.
    private bool _hasBounds;
    private Vector2 _boundsMin;
    private Vector2 _boundsMax;

    /// <param name="sprite">The frame a particle draws when <see cref="Sprites"/> is empty.</param>
    /// <param name="capacity">The pool's fixed size, the most particles alive at once.</param>
    public ParticleEmitter(Sprite sprite, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);

        _defaultSprite = sprite;
        _boundsRadius = LargestHalfDiagonal(Frames);
        Capacity = capacity;
        _particles = new Particle[capacity];
    }

    /// <summary>The pool's fixed size, set at construction.</summary>
    public int Capacity { get; }

    /// <summary>Where live particles are kept, <see cref="ParticleSpace.World"/> by default and fixed at construction.</summary>
    public ParticleSpace Space { get; init; }

    /// <summary>How many particles are alive now.</summary>
    public int Alive => _alive;

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
        get => _spread;

        set
        {
            Guard.InRange(value, 0f, 360f, nameof(value));
            _spread = value;
        }
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
    public FloatRange Lifetime { get; set; } = new(1f, 1f);

    /// <summary>Acceleration on every live particle, in units per second squared along world directions in either <see cref="Space"/>, zero by default.</summary>
    public Vector2 Gravity { get; set; }

    /// <summary>Velocity drag per second, applied as <c>velocity *= max(0, 1 - Damping * dt)</c> each step. Zero by default.</summary>
    public float Damping
    {
        get => _damping;

        set
        {
            Guard.NonNegative(value, nameof(value));
            _damping = value;
        }
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

    /// <summary>
    /// The frames a particle draws from. Empty, the default, draws the constructor's sprite.
    /// </summary>
    /// <remarks>A new value reaches live particles on their next draw.</remarks>
    public ReadOnlyMemory<Sprite> Sprites
    {
        get => _sprites;

        set
        {
            _sprites = value;
            _boundsRadius = LargestHalfDiagonal(Frames);
        }
    }

    /// <summary>How a particle picks which of <see cref="Sprites"/> to draw. <see cref="Particles.SpriteMode.RandomAtSpawn"/> by default.</summary>
    public SpriteMode SpriteMode { get; set; }

    /// <summary>
    /// The fraction of the emitter's own velocity that a particle spawned by <see cref="Rate"/> or
    /// <see cref="RateOverDistance"/> inherits, zero by default.
    /// </summary>
    /// <remarks>It has no effect in <see cref="ParticleSpace.Local"/>, where particles already move with the entity.</remarks>
    public float InheritVelocity
    {
        get => _inheritVelocity;

        set
        {
            Guard.Finite(value, nameof(value));
            _inheritVelocity = value;
        }
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
        get => _rate;

        set
        {
            Guard.NonNegative(value, nameof(value));
            _rate = value;
        }
    }

    /// <summary>
    /// Particles spawned per unit the entity travels in the world while <see cref="Emitting"/>, in either
    /// <see cref="Space"/>, zero by default.
    /// </summary>
    /// <remarks>A fractional remainder carries to the next step.</remarks>
    public float RateOverDistance
    {
        get => _rateOverDistance;

        set
        {
            Guard.NonNegative(value, nameof(value));
            _rateOverDistance = value;
        }
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
        get => _prewarmSeconds;

        set
        {
            Guard.NonNegative(value, nameof(value));
            _prewarmSeconds = value;
        }
    }

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
    /// Before the emitter has started, the spawn waits and is placed when it starts.
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
    /// Before the emitter has started, the spawn waits and is placed at the transform the entity starts
    /// with. Counts from several early calls add up and spawn at the last call's point.
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

        if (!_started)
        {
            _pendingCount += count;
            _pendingAt = at;

            return;
        }

        SpawnImmediate(count, at);
    }

    /// <summary>
    /// Removes every live particle, drops the fractional <see cref="Rate"/> and
    /// <see cref="RateOverDistance"/> remainders, and cancels any <see cref="Emit(int)"/> count waiting
    /// for the emitter to start.
    /// </summary>
    public void Clear()
    {
        _alive = 0;
        _pendingCount = 0;
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

        // A rejoining emitter does not start again. It draws its new stream from the start, as a new emitter would.
        if (_started)
        {
            _random.Reset(Entity.Scene.RunOrNull?.Random.Seed ?? _random.Seed, ParticleStreamBase ^ _particleStream);
        }
    }

    /// <inheritdoc/>
    protected internal override void OnStart()
    {
        base.OnStart();

        _random = new RandomSource(Run.Random.Seed, ParticleStreamBase ^ _particleStream);
        _started = true;

        int count = _pendingCount;
        _pendingCount = 0;
        SpawnImmediate(count, _pendingAt);
    }

    /// <summary>Clears every particle and the prewarm. A reused emitter starts its next life as a new one would.</summary>
    /// <inheritdoc/>
    protected internal override void OnRemovedFromScene()
    {
        base.OnRemovedFromScene();

        Clear();
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

        int frameCount = Frames.Length;
        bool overLife = SpriteMode == SpriteMode.OverLife;
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
        bool flipX = false;
        bool flipY = false;
        if (local)
        {
            previousFrame = Frame(PreviousRenderTransform);
            currentFrame = Frame(RenderTransform);

            // A mirror conjugates a particle's own turn, as it does a child entity's.
            previousTurn = previousFrame.Mirrored ? -1f : 1f;
            currentTurn = currentFrame.Mirrored ? -1f : 1f;
            flipX = currentFrame.Scale.X < 0f;
            flipY = currentFrame.Scale.Y < 0f;
        }

        for (int index = 0; index < _alive; index++)
        {
            ref readonly Particle particle = ref _particles[index];
            bool still = held && particle.SpawnStep != step;

            float t = (float)particle.Age / particle.Lifetime;
            Sprite frame = FrameAt(overLife ? (int)(t * frameCount) : particle.SpriteIndex);
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
                flipX,
                flipY,
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
            if (particle.Age >= particle.Lifetime)
            {
                Array.Copy(_particles, run, _particles, live, index - run);
                live += index - run;
                run = index + 1;
                continue;
            }

            particle.PreviousPosition = particle.Position;
            particle.PreviousRotation = particle.Rotation;
            particle.Velocity += gravityStep;
            particle.Velocity *= drag;
            particle.Position += particle.Velocity * dt;
            particle.Rotation += particle.AngularVelocity;

            ExtendBounds(particle.PreviousPosition, particle.Position);
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
            Vector2 previousCentre = local ? LocalCentre(Offset, current) : previous.TransformPoint(Offset);
            Vector2 currentCentre = local ? previousCentre : current.TransformPoint(Offset);

            for (int index = 0; index < owed; index++)
            {
                float f = (index + 0.5f) / owed;
                SpawnOne(Shape.Sample(_random), previousCentre, currentCentre, emitterVelocity, f, dt, turn);
            }
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
        Transform2D turn = Turn(current);
        Vector2 centre = Space == ParticleSpace.Local ? LocalCentre(at, current) : current.TransformPoint(at);

        for (int index = 0; index < count; index++)
        {
            Vector2 sample = shape.IsRing ? shape.BurstPoint(ringStart, index, count) : shape.Sample(_random);
            SpawnOne(sample, centre, centre, Vector2.Zero, 0.5f, _lastDeltaSeconds, turn);
        }
    }

    // Places one particle at fraction f of the centre's move, offset by its shape sample. The launch is
    // drawn in the entity's unit frame, and in World space turn maps it into the world.
    private void SpawnOne(Vector2 sample, Vector2 previousCentre, Vector2 currentCentre, Vector2 emitterVelocity, float f, float dt, in Transform2D turn)
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
            origin = currentCentre + sample;
        }
        else
        {
            origin = Vector2.Lerp(previousCentre, currentCentre, f) + turn.TransformPoint(sample);
            velocity = turn.TransformPoint(velocity) + (InheritVelocity * emitterVelocity);
        }

        float lifetimeSeconds = _random.Range(Lifetime);
        int lifetimeTicks = Math.Max(1, (int)MathF.Ceiling(lifetimeSeconds / MathF.Max(dt, 1e-6f)));

        float rotationDegrees = _random.Range(Rotation);
        float angularVelocityDegreesPerSecond = _random.Range(AngularVelocity);
        float scale = _random.Range(Scale);

        int frameCount = Frames.Length;
        int spriteIndex = SpriteMode == SpriteMode.RandomAtSpawn && frameCount > 1
            ? _random.Range(0, frameCount)
            : 0;

        ref Particle particle = ref _particles[_alive < _particles.Length ? _alive++ : NearestEnd()];

        particle.Velocity = velocity;
        particle.Rotation = float.DegreesToRadians(rotationDegrees);
        particle.PreviousRotation = particle.Rotation;
        particle.AngularVelocity = float.DegreesToRadians(angularVelocityDegreesPerSecond) * dt;
        particle.Age = 0;
        particle.Lifetime = lifetimeTicks;
        particle.Scale = scale;
        particle.SpriteIndex = spriteIndex;
        particle.Position = origin + (velocity * (1f - f) * dt);
        particle.PreviousPosition = origin - (velocity * f * dt);
        // A prewarmed particle counts as spawned the step before, so later prewarm ticks age it.
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

    private ReadOnlySpan<Sprite> Frames => _sprites.IsEmpty ? new ReadOnlySpan<Sprite>(in _defaultSprite) : _sprites.Span;

    // Clamped because a particle's index outlives a shorter Sprites assigned later.
    private Sprite FrameAt(int index)
    {
        ReadOnlySpan<Sprite> frames = Frames;
        return frames[Math.Min(index, frames.Length - 1)];
    }

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
        new(min.X - radius, min.Y - radius, max.X + radius, max.Y + radius);

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

        // The scene's StepsBegun when this particle spawned.
        internal int SpawnStep;
    }
}
