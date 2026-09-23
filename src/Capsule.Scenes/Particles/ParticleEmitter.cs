using System.Numerics;
using Capsule.Animation;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Particles;

/// <summary>
/// Draws a fixed pool of sprite particles simulated on the fixed step, one
/// <see cref="SpriteIntent"/> per live particle. A particle moves on its own once spawned and does
/// not follow the entity.
/// </summary>
/// <remarks>
/// Positions are in world units under a world root and canvas pixels under a screen root.
/// <para>
/// The simulation is engine state seeded from the run's seed. A headless run emits exactly the
/// intents a windowed run draws, and two runs of one seed are identical. A spawn into a full pool
/// replaces the live particle nearest the end of its life.
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

    private readonly Particle[] _particles;
    private readonly Sprite _defaultSprite;

    private RandomSource _random = null!;
    private ulong _particleStream;
    private ReadOnlyMemory<Sprite> _sprites;
    private float _boundsRadius;

    private int _cursor;
    private int _alive;
    private bool _prewarmed;
    private bool _started;
    private float _lastDeltaSeconds = 1f / StepContext.DefaultStepHertz;
    private float _rateAccumulator;
    private float _distanceAccumulator;

    // An Emit call before OnStart has run, which has no transform and no random source yet: kept
    // here and placed in OnStart instead.
    private int _pendingCount;
    private Vector2 _pendingAt;

    // The AABB over every live particle's previous and current position, uninflated: inflated by the
    // radius once, at read, in Bounds. The walk rebuilds this from scratch each step, and every spawn
    // after it (in-step or Emit called outside one) extends it, so a particle spawned after the walk is
    // still covered the same frame.
    private bool _hasBounds;
    private Vector2 _boundsMin;
    private Vector2 _boundsMax;

    /// <param name="sprite">The frame a particle draws when <see cref="Sprites"/> is empty.</param>
    /// <param name="capacity">The pool's fixed size, the most particles alive at once.</param>
    public ParticleEmitter(Sprite sprite, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);

        _defaultSprite = sprite;
        _boundsRadius = HalfDiagonal(sprite);
        Capacity = capacity;
        _particles = new Particle[capacity];
    }

    /// <summary>The pool's fixed size, set at construction.</summary>
    public int Capacity { get; }

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
    public float Spread { get; set; }

    /// <summary>The launch speed, in units per second along the drawn direction. Zero by default.</summary>
    public FloatRange Speed { get; set; }

    /// <summary>
    /// Seconds a particle lives, one by default. The drawn value rounds up to whole steps at spawn, one
    /// step at least.
    /// </summary>
    public FloatRange Lifetime { get; set; } = new(1f, 1f);

    /// <summary>Acceleration on every live particle, in units per second squared. Zero by default.</summary>
    public Vector2 Gravity { get; set; }

    /// <summary>Velocity drag per second, applied as <c>velocity *= max(0, 1 - Damping * dt)</c> each step. Zero by default.</summary>
    public float Damping { get; set; }

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
    public ReadOnlyMemory<Sprite> Sprites
    {
        get => _sprites;

        set
        {
            _sprites = value;
            _boundsRadius = value.IsEmpty ? HalfDiagonal(_defaultSprite) : LargestHalfDiagonal(value.Span);
        }
    }

    /// <summary>How a particle picks which of <see cref="Sprites"/> to draw. <see cref="Particles.SpriteMode.RandomAtSpawn"/> by default.</summary>
    public SpriteMode SpriteMode { get; set; }

    /// <summary>The fraction of the emitter's own velocity a particle spawned by <see cref="Rate"/> or <see cref="RateOverDistance"/> inherits. Zero by default.</summary>
    public float InheritVelocity { get; set; }

    /// <summary>How every particle blends with what is already drawn. <see cref="BlendMode.Alpha"/> by default.</summary>
    public BlendMode Blend { get; set; }

    /// <summary>
    /// Whether <see cref="Rate"/> and <see cref="RateOverDistance"/> spawn, true by default.
    /// <see cref="Emit(int)"/> spawns either way.
    /// </summary>
    public bool Emitting { get; set; } = true;

    /// <summary>Particles spawned per second while <see cref="Emitting"/>, zero by default. A fractional remainder carries to the next step.</summary>
    public float Rate { get; set; }

    /// <summary>Particles spawned per unit the emitter moves while <see cref="Emitting"/>, zero by default. A fractional remainder carries to the next step.</summary>
    public float RateOverDistance { get; set; }

    /// <summary>
    /// Seconds of <see cref="Rate"/> emission simulated in the emitter's first step. Zero, the default,
    /// starts empty.
    /// </summary>
    /// <remarks>
    /// Read once, on the first step after the emitter joins a scene, and only while <see cref="Emitting"/>.
    /// The prewarm holds the emitter still. <see cref="RateOverDistance"/> spawns nothing during it.
    /// </remarks>
    public float PrewarmSeconds { get; set; }

    /// <summary>
    /// The rect covering every live particle's last move, grown by the largest frame's reach at its
    /// largest scale. Empty with nothing alive.
    /// </summary>
    public override Rect Bounds => _hasBounds
        ? Inflate(_boundsMin, _boundsMax, _boundsRadius * Scale.Max * ScaleOverLifetime.Max)
        : default;

    /// <summary>
    /// Spawns <paramref name="count"/> particles now, at <see cref="Shape"/> about <see cref="Offset"/>,
    /// placed by the entity's transform at this call. Before the emitter has started, the spawn waits
    /// and is placed when it starts.
    /// </summary>
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
        Array.Clear(_particles);
        _alive = 0;
        _pendingCount = 0;
        _rateAccumulator = 0f;
        _distanceAccumulator = 0f;
        _hasBounds = false;
        _cursor = 0;
    }

    /// <inheritdoc/>
    protected internal override void OnAddedToScene()
    {
        base.OnAddedToScene();

        // Reserved here, in scene-add order, so the stream an emitter draws never depends on when its
        // scene happens to start. Run is not readable yet: a document-composed entity reaches this from
        // the scene's own constructor, before Run is installed.
        _particleStream = Entity!.Scene.NextParticleStream();
    }

    /// <inheritdoc/>
    protected internal override void OnStart()
    {
        base.OnStart();

        _random = new RandomSource(Run.Random.Seed, ParticleStreamBase ^ _particleStream);
        _started = true;

        if (_pendingCount > 0)
        {
            int count = _pendingCount;
            Vector2 at = _pendingAt;
            _pendingCount = 0;
            SpawnImmediate(count, at);
        }
    }

    /// <summary>Clears every particle and the prewarm. A reused emitter starts its next life as a new one would.</summary>
    /// <inheritdoc/>
    protected internal override void OnRemovedFromScene()
    {
        base.OnRemovedFromScene();

        Clear();
        _prewarmed = false;
        _lastDeltaSeconds = 1f / StepContext.DefaultStepHertz;
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

            // Prewarmed ticks hold the emitter at its current transform, so Rate spawns but
            // RateOverDistance does not; the last tick falls through to the real transform below, one
            // tick of interpolation behind current, which is correct.
            if (PrewarmSeconds > 0f && Emitting && dt > 0f)
            {
                int ticks = Math.Max(1, (int)MathF.Ceiling(PrewarmSeconds / dt));
                for (int tick = 0; tick < ticks - 1; tick++)
                {
                    Step(dt, current, current);
                }
            }
        }

        Step(dt, previous, current);
    }

    // A held emitter skips its step, so each particle's last motion would otherwise interpolate again
    // on every frame the hold lasts. The bounds already cover the current positions.
    internal override void SavePrevious()
    {
        if (Entity is not { Held: true })
        {
            return;
        }

        for (int index = 0; index < _particles.Length; index++)
        {
            ref Particle particle = ref _particles[index];
            particle.PreviousPosition = particle.Position;
            particle.PreviousRotation = particle.Rotation;
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

        int frameCount = FrameCount;

        for (int index = 0; index < _particles.Length; index++)
        {
            ref readonly Particle particle = ref _particles[index];
            if (particle.Lifetime == 0)
            {
                continue;
            }

            float t = (float)particle.Age / particle.Lifetime;
            Sprite frame = FrameAt(SpriteMode == SpriteMode.OverLife ? OverLifeIndex(t, frameCount) : particle.SpriteIndex);
            float scale = particle.Scale * ScaleOverLifetime.Evaluate(t);
            Vector2 size = new Vector2(frame.Region.Width, frame.Region.Height) * scale;

            SpriteIntent intent = new(
                frame,
                particle.PreviousPosition,
                particle.Position,
                particle.PreviousRotation,
                particle.Rotation,
                size,
                false,
                false,
                Color.Evaluate(t),
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

    // Ages, integrates and spawns one tick: the shared body OnStep's real tick and every prewarmed
    // tick run through.
    private void Step(float dt, in Transform2D previous, in Transform2D current)
    {
        _hasBounds = false;

        for (int index = 0; index < _particles.Length; index++)
        {
            ref Particle particle = ref _particles[index];
            if (particle.Lifetime == 0)
            {
                continue;
            }

            particle.Age++;
            if (particle.Age >= particle.Lifetime)
            {
                particle.Lifetime = 0;
                particle.Age = 0;
                _alive--;
                continue;
            }

            particle.PreviousPosition = particle.Position;
            particle.PreviousRotation = particle.Rotation;
            particle.Velocity += Gravity * dt;
            particle.Velocity *= MathF.Max(0f, 1f - (Damping * dt));
            particle.Position += particle.Velocity * dt;
            particle.Rotation += particle.AngularVelocity;

            ExtendBounds(particle.PreviousPosition, particle.Position);
        }

        Vector2 displacement = current.Position - previous.Position;
        Vector2 emitterVelocity = dt > 0f ? displacement / dt : Vector2.Zero;

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
            Vector2 prevWorld = previous.TransformPoint(Offset);
            Vector2 curWorld = current.TransformPoint(Offset);

            for (int index = 0; index < owed; index++)
            {
                float f = (index + 0.5f) / owed;
                SpawnOne(prevWorld, curWorld, emitterVelocity, f, dt, current);
            }
        }
    }

    private void SpawnImmediate(int count, Vector2 local)
    {
        Transform2D current = RenderTransform;
        Vector2 origin = current.TransformPoint(local);

        for (int index = 0; index < count; index++)
        {
            SpawnOne(origin, origin, Vector2.Zero, 0.5f, _lastDeltaSeconds, current);
        }
    }

    // Places one particle: origin is the emitter position at spawn fraction f plus the shape sample,
    // rotated and mirrored the way Direction is; velocity is the drawn direction at Speed plus the
    // inherited fraction of emitterVelocity. Every trig call is DeterministicMath, at spawn only.
    private void SpawnOne(Vector2 prevWorld, Vector2 curWorld, Vector2 emitterVelocity, float f, float dt, in Transform2D spawnTransform)
    {
        Vector2 origin = Vector2.Lerp(prevWorld, curWorld, f) + RotateMirror(Shape.Sample(_random), spawnTransform);

        Vector2 localDirection = Direction == Vector2.Zero
            ? Rotate(Vector2.UnitX, float.DegreesToRadians(_random.Range(0f, 360f)))
            : Rotate(Direction / Direction.Length(), float.DegreesToRadians(_random.Range(-Spread / 2f, Spread / 2f)));
        Vector2 velocity = (RotateMirror(localDirection, spawnTransform) * _random.Range(Speed)) + (InheritVelocity * emitterVelocity);

        float lifetimeSeconds = _random.Range(Lifetime);
        int lifetimeTicks = Math.Max(1, (int)MathF.Ceiling(lifetimeSeconds / MathF.Max(dt, 1e-6f)));

        float rotationDegrees = _random.Range(Rotation);
        float angularVelocityDegreesPerSecond = _random.Range(AngularVelocity);
        float scale = _random.Range(Scale);

        int frameCount = FrameCount;
        int spriteIndex = SpriteMode == SpriteMode.RandomAtSpawn && frameCount > 1
            ? _random.Range(0, frameCount)
            : 0;

        int slot = FindSlot();
        ref Particle particle = ref _particles[slot];
        bool wasFree = particle.Lifetime == 0;

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

        ExtendBounds(particle.PreviousPosition, particle.Position);

        if (wasFree)
        {
            _alive++;
        }
    }

    private void ExtendBounds(Vector2 previous, Vector2 current)
    {
        Vector2 lower = Vector2.Min(previous, current);
        Vector2 upper = Vector2.Max(previous, current);

        _boundsMin = _hasBounds ? Vector2.Min(_boundsMin, lower) : lower;
        _boundsMax = _hasBounds ? Vector2.Max(_boundsMax, upper) : upper;
        _hasBounds = true;
    }

    private int FindSlot()
    {
        for (int offset = 0; offset < _particles.Length; offset++)
        {
            int index = (_cursor + offset) % _particles.Length;
            if (_particles[index].Lifetime == 0)
            {
                _cursor = (index + 1) % _particles.Length;
                return index;
            }
        }

        // The slot nearest the end of its life, the greatest Age / Lifetime, is the least visible
        // loss, so a spawn is never silently swallowed.
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

        _cursor = (nearestEnd + 1) % _particles.Length;
        return nearestEnd;
    }

    private Sprite FrameAt(int index)
    {
        ReadOnlySpan<Sprite> span = _sprites.Span;
        return span.Length == 0 ? _defaultSprite : span[Math.Clamp(index, 0, span.Length - 1)];
    }

    private int FrameCount => _sprites.Length == 0 ? 1 : _sprites.Length;

    private static int OverLifeIndex(float t, int frameCount) =>
        t >= 1f ? frameCount - 1 : Math.Clamp((int)(t * frameCount), 0, frameCount - 1);

    private static Vector2 RotateMirror(Vector2 local, in Transform2D transform)
    {
        Vector2 mirrored = new(
            transform.Scale.X < 0f ? -local.X : local.X,
            transform.Scale.Y < 0f ? -local.Y : local.Y);

        return Rotate(mirrored, transform.Rotation);
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
        for (int index = 0; index < sprites.Length; index++)
        {
            float diagonal = HalfDiagonal(sprites[index]);
            if (diagonal > max)
            {
                max = diagonal;
            }
        }

        return max;
    }

    private static float HalfDiagonal(Sprite sprite)
    {
        TextureRegion region = sprite.Region;
        return 0.5f * MathF.Sqrt((region.Width * region.Width) + (region.Height * region.Height));
    }

    // One pooled particle. A slot with Lifetime == 0 is free.
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
    }
}
