using System.Globalization;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Tiles;

namespace Capsule.Scenes;

/// <summary>
/// An ordered world of entities with a <see cref="Camera"/> that frames it and a
/// <see cref="Collision"/> world it collides through. Mutations requested during a step are
/// deferred until the step ends.
/// </summary>
public partial class Scene
{
    internal const string NoRunYet =
        "Reach the run from OnStart onward, not from a constructor.";

    internal const string NotJoinedYet =
        "Query its collision world from OnStart onward, not from a constructor.";

    private Camera _camera = new();
    private Run? _run;

    private bool _stepping;

    // Set once the step's entity walks are over. A parent write after that lays the list out at once.
    private bool _drained;

    private bool _started;

    private bool _stopped;

    // Set as OnStart is entered. A start that fails before it releases entities without OnStop.
    private bool _startHookRan;

    // Steps a freeze still holds after the current one. BeginStep settles it with Paused into this
    // step's state, which every entity resolves against. The tree resolves again only when that state
    // or a StepMode has changed, and a subtree a parent write moved resolves again by itself.
    private int _freezeTicks;
    private bool _pausedThisStep;
    private bool _frozenThisStep;
    private bool _cameraHeldThisStep;
    private bool _holdsStale;

    // The roots of the subtrees parent writes moved since the last step began, empty while the whole
    // tree resolves again anyway.
    private readonly List<Entity> _movedHolds = [];

    // How many steps have begun, wrapping. A held particle emitter compares it for equality to tell its
    // particles spawned this step from older ones.
    internal int StepsBegun { get; private set; }

    // The current stepping tick, or null if not stepping.
    internal long? SteppingTick { get; private set; }

    internal ScenePhysics Physics { get; } = new();

    internal SceneVisibility Visibility { get; }

    internal ScenePools Pools { get; }

    internal SceneDrawing Drawing { get; }

    // Every texture and sound the document's placements author, which the preload collects. Null for a scene built in code.
    internal AssetCollection? AuthoredAssets { get; }

    // Handed out one at a time to each particle emitter added. Every emitter in a scene draws its own
    // randomness stream, and a new scene instance starts at 0.
    private ulong _nextParticleStream;

    /// <summary>
    /// The camera framing this scene. A scene always has one, and installing another cuts to it.
    /// </summary>
    /// <remarks>
    /// A camera installed before the scene starts becomes the opening camera. One installed later runs its
    /// <see cref="Scenes.Camera.OnStart"/> immediately.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The camera already frames another scene.</exception>
    [Authorable]
    public Camera Camera
    {
        get => _camera;
        protected set
        {
            ArgumentNullException.ThrowIfNull(value);
            RequireUnowned(value);

            Camera outgoing = _camera;
            _camera = value;

            // Installing the current camera is a cut.
            if (ReferenceEquals(outgoing.SceneOrNull, this) && !ReferenceEquals(outgoing, value))
            {
                outgoing.SceneOrNull = null;
                Install(value);
            }

            // A camera installed during a step holds by its own mode against the step's pause and freeze.
            // Reinstalling the same camera keeps the hold its step began with. The current camera is read
            // after the install, whose start hook may have installed another.
            if (!ReferenceEquals(outgoing, _camera))
            {
                _cameraHeldThisStep = _camera.StepMode.Holds(_pausedThisStep, _frozenThisStep);
            }

            _camera.SavePrevious();
        }
    }

    /// <summary>
    /// World units the scene spans from its origin at (0, 0). Zero unless the scene sets it.
    /// </summary>
    /// <remarks>
    /// A scene composed from a document spans its authored size, or else its tile maps' largest
    /// dimensions.
    /// </remarks>
    [Authorable]
    public Vector2 Size { get; protected internal set; }

    /// <summary>The colour behind everything the scene draws, defaulting to black.</summary>
    [Authorable]
    public ColorRgba ClearColor { get; protected set; } = ColorRgba.Black;

    /// <summary>The colour the world is lit by where no light reaches, defaulting to white.</summary>
    /// <remarks>
    /// Every world sprite is multiplied by the light at its pixel, which is this colour where no light reaches and
    /// each light added over it, with no ceiling. The screen layer and an <see cref="Entity.Unlit"/> entity are
    /// never lit. In an <see cref="Unlit"/> scene nothing is multiplied, and this is only the value
    /// <c>SampleLight</c> reads where no light reaches.
    /// </remarks>
    [Authorable]
    public ColorRgba Ambient { get; protected set; } = ColorRgba.White;

    /// <summary>Whether everything the scene draws keeps its own colour untouched by the light, defaulting to false.</summary>
    /// <remarks>
    /// The scene draws as if it were the unlit root of every entity, and its clear colour keeps its own colour too.
    /// <see cref="Entity.Unlit"/> adds nothing beneath an unlit scene. The ambient and every light still make the
    /// light as they do in a lit scene, and a material on any world draw reads it through <c>SampleLight</c>.
    /// </remarks>
    [Authorable]
    public bool Unlit { get; protected set; }

    /// <summary>
    /// The values of the shader parameters this scene sets for every material, which start at zero.
    /// </summary>
    public ShaderGlobals ShaderGlobals { get; } = new();

    /// <summary>
    /// Whether world-layer renderers in the same draw band draw in order of their root entity's Y, lower
    /// first. Off by default.
    /// </summary>
    /// <remarks>
    /// The sort point is the root entity's origin: its simulated <see cref="Entity.Position"/>, not the
    /// interpolated one a frame draws. A root's whole subtree sorts with it, and a character and its
    /// shadow move through the order as one. The band always wins. A child with its own
    /// <see cref="Entity.ZIndex"/> sorts by its root's Y within its own band. Equal Y keeps the order
    /// the scene draws in with sorting off. The screen layer is not sorted.
    /// </remarks>
    /// <exception cref="InvalidOperationException">It is changed inside a <see cref="Renderer.Draw"/>.</exception>
    public bool YSort
    {
        get => Drawing.YSort;
        set => Drawing.YSort = value;
    }

    /// <summary>
    /// Whether the scene holds the entities and the camera whose <see cref="StepMode"/> a pause holds,
    /// from the next step until it is cleared.
    /// </summary>
    /// <remarks>
    /// The scene itself always steps, and <see cref="StepContext.Tick"/> and
    /// <see cref="StepContext.TotalSeconds"/> keep counting. A held camera skips its
    /// <see cref="Scenes.Camera.OnLateStep"/>, and its follow, bounds and shake stand still. A held
    /// entity skips its and its components' steps and late steps, and its colliders and screen notifiers
    /// settle nothing until it resumes. It still draws, collides with others, keeps its sounds and runs
    /// its structural hooks.
    /// Removing it, or disabling or detaching its collider or notifier, still raises the exits it owes.
    /// </remarks>
    public bool Paused { get; set; }

    /// <summary>
    /// Whether a <see cref="Freeze"/> holds the step in progress or a step still to come.
    /// </summary>
    /// <remarks>
    /// It reads true from the call through the last step the freeze holds. It first reads false at the
    /// top of the next step, the one whose entities step again unless <see cref="Paused"/> holds them.
    /// </remarks>
    /// <example>
    /// <code>
    /// Scene.Freeze(30);
    /// await Wait.Until(() =&gt; !Scene.IsFrozen);
    /// </code>
    /// </example>
    public bool IsFrozen => _freezeTicks > 0 || _frozenThisStep;

    /// <summary>
    /// The length in seconds of the step running now, or of the last one, for motion outside a step callback such as a
    /// routine's. Zero before the first step.
    /// </summary>
    /// <example>
    /// <code>
    /// while (!_body.IsOnFloor)
    /// {
    ///     await Wait.Ticks(1);
    ///     _body.Move(new Vector2(0f, FallSpeed * Scene.StepSeconds));
    /// }
    /// </code>
    /// </example>
    public float StepSeconds { get; private set; }

    /// <summary>
    /// The key of the document this scene was composed from, or null for a scene built in code. A restart composes the
    /// same document and keeps the key.
    /// </summary>
    public SceneKey? DocumentKey { get; }

    /// <summary>
    /// State supplied by the transition that opened this scene, readable from <see cref="CollectAssets"/> onward.
    /// </summary>
    /// <remarks>
    /// The constructor runs before the payload is known. A prefetch collects with the payload it was given.
    /// </remarks>
    protected IScenePayload? EntryPayload { get; private set; }

    /// <summary>An empty world, for a scene that builds itself in code.</summary>
    public Scene()
    {
        _tree = new(this);
        Visibility = new(this);
        Pools = new(this);
        Drawing = new(this);
    }

    /// <summary>
    /// The world a scene document describes: one entity per entry, in authored order. The document is construction data and is not retained.
    /// </summary>
    /// <remarks>
    /// The entities are composed into the scene and join it as it starts. None of their hooks has run when
    /// this constructor returns.
    /// </remarks>
    /// <exception cref="SpawnException">
    /// A placement's type key is claimed by no entity, or its class returned no entity.
    /// </exception>
    /// <exception cref="SceneDocumentFormatException">
    /// A placement's or the document's members do not match its class's authorable members, or an entity's
    /// constructor refuses what its placement authors with an <see cref="ArgumentException"/> or an
    /// <see cref="InvalidOperationException"/>.
    /// </exception>
    public Scene(SceneContent content)
        : this()
    {
        DocumentKey = content.DocumentKey;
        AuthoredAssets = SceneComposition.Compose(this, content);
    }

    /// <summary>
    /// Everything in this scene that can be collided with. A <see cref="Collider2D"/> or a
    /// <see cref="TileMapCollider2D"/> registers here when its entity joins the scene.
    /// </summary>
    /// <exception cref="InvalidOperationException">The scene has not started. Query it from <see cref="OnStart"/> onward, not from a constructor.</exception>
    public CollisionWorld2D Collision =>
        _started ? Physics.World : throw new InvalidOperationException($"A {GetType().Name} has not started and nothing has joined it yet. {NotJoinedYet}");

    /// <summary>
    /// The run that owns this scene's lifetime state and requests. It is installed before the scene
    /// starts and is shared by every scene the run opens.
    /// </summary>
    /// <exception cref="InvalidOperationException">The run has not been installed yet.</exception>
    public Run Run
    {
        get => _run ?? throw new InvalidOperationException($"A {GetType().Name} has not started and has no run yet. {NoRunYet}");
        internal set => _run = value;
    }

    // The run or null for components that start before the scene does.
    internal Run? RunOrNull => _run;

    // The ordinal the next particle emitter added to this scene draws its randomness stream from: a
    // static counter is not deterministic across runs, and a draw from Run.Random would shift gameplay
    // whenever an effect is added.
    internal ulong NextParticleStream() => _nextParticleStream++;

    /// <summary>
    /// Every entity held in step order: roots in addition order, each followed by its subtree. The span
    /// is invalid once an entity is added, removed or reparented.
    /// </summary>
    public ReadOnlySpan<Entity> Entities
    {
        get => _tree.Held;
    }

    /// <summary>
    /// Holds the entities and the camera whose <see cref="StepMode"/> a freeze holds for the next
    /// <paramref name="ticks"/> steps, then resumes them by itself.
    /// </summary>
    /// <remarks>
    /// The count starts at the next step. A call made during a step, from a step, a late step or a
    /// contact handler, never holds the step in progress. A freeze already running keeps the longer of
    /// the two remaining counts, and zero changes nothing.
    /// </remarks>
    /// <param name="ticks">Fixed steps to hold, counted whether or not the scene is paused.</param>
    public void Freeze(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);

        _freezeTicks = Math.Max(_freezeTicks, ticks);
    }

    /// <summary>
    /// The first entity in <see cref="Entities"/> assignable to <typeparamref name="T"/>, or null when
    /// there is none.
    /// </summary>
    public T? FindFirst<T>()
        where T : class
    {
        foreach (T found in FindAll<T>())
        {
            return found;
        }

        return null;
    }

    /// <summary>
    /// The first entity in <see cref="Entities"/> assignable to <typeparamref name="T"/> for which
    /// <paramref name="match"/> returns true, or null when there is none.
    /// </summary>
    /// <remarks>It scans the whole scene. Call it at start or on an event, not every step.</remarks>
    public T? FindFirst<T>(Func<T, bool> match)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(match);

        foreach (T found in FindAll<T>())
        {
            if (match(found))
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// The first entity in <see cref="Entities"/> assignable to <typeparamref name="T"/> whose
    /// <see cref="Entity.Name"/>, without the number the scene gave it, is <paramref name="name"/>, or null
    /// when there is none.
    /// </summary>
    /// <remarks>
    /// The comparison is ordinal. An entity with no name set answers to its type name. A numbered entity
    /// answers to the name it asked for: a lone <c>north (1)</c> is found as <c>north</c>, and
    /// <c>north (1)</c> finds nothing. It scans the whole scene. Call it at start or on an event, not every step.
    /// </remarks>
    /// <example>
    /// <code>
    /// TileMap north = FindFirst&lt;TileMap&gt;("north")!;
    /// </code>
    /// </example>
    public T? FindFirst<T>(string name)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach (T found in FindAll<T>())
        {
            if (string.Equals(((Entity)(object)found).RequestedName, name, StringComparison.Ordinal))
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Every entity in <see cref="Entities"/> assignable to <typeparamref name="T"/>, in step order.</summary>
    /// <remarks>
    /// It walks the scene as it stands. An add or remove during a step lands when the step ends. Outside
    /// a step an add lands at once and ends the walk with an <see cref="InvalidOperationException"/>. So does
    /// a removal outside a step once any query compacts the list.
    /// </remarks>
    /// <example>
    /// <code>
    /// foreach (ISwitchable lamp in FindAll&lt;ISwitchable&gt;())
    /// {
    ///     lamp.Toggle();
    /// }
    /// </code>
    /// </example>
    public EntityWalk<T> FindAll<T>()
        where T : class
    {
        _tree.Settle();
        return new(this);
    }

    /// <summary>The only entity in <see cref="Entities"/> assignable to <typeparamref name="T"/>.</summary>
    /// <exception cref="InvalidOperationException">There is not exactly one matching entity.</exception>
    public T FindSingle<T>()
        where T : class
    {
        T? found = null;

        foreach (T candidate in FindAll<T>())
        {
            if (found is not null)
            {
                throw new InvalidOperationException(
                    $"A {GetType().Name} holds more than one entity assignable to {typeof(T).Name}.");
            }

            found = candidate;
        }

        return found ?? throw new InvalidOperationException(
            $"A {GetType().Name} holds no entity assignable to {typeof(T).Name}.");
    }

    /// <summary>
    /// The <see cref="Collider2D"/> a query hit names, or null when it names a tile map's grid, nothing,
    /// or a collider since removed.
    /// </summary>
    /// <example>
    /// An entity finding the trigger entities its box overlaps:
    /// <code>
    /// Span&lt;Contact2D&gt; found = stackalloc Contact2D[8];
    /// int count = Get&lt;BoxCollider2D&gt;().OverlapAll(found);
    /// for (int index = 0; index &lt; count; index++)
    /// {
    ///     if (Scene.ColliderOf(found[index].Target.Collider) is { Entity: CameraTrigger trigger })
    ///     {
    ///         Enter(trigger);
    ///     }
    /// }
    /// </code>
    /// </example>
    public Collider2D? ColliderOf(ColliderHandle handle) => Collision.UserDataOrNull(handle) as Collider2D;

    /// <summary>The tile-map cell a query hit names, or null when it names anything else.</summary>
    /// <example>
    /// Removing the first brick a ray meets:
    /// <code>
    /// if (Scene.Collision.Raycast(Position, direction, reach, Solid, out RayHit2D hit)
    ///     &amp;&amp; Scene.TileOf(hit.Target) is { Type: Brick } brick)
    /// {
    ///     brick.Map.RemoveTile(brick.X, brick.Y);
    /// }
    /// </code>
    /// </example>
    public TileContact2D? TileOf(in CollisionTarget target) =>
        TileContact2D.Of(Collision.UserDataOrNull(target.Collider), target);

    /// <summary>
    /// Runs once as the scene starts, after its entities and its opening camera have started and before
    /// its first frame is built. A scene belongs to a single <see cref="SceneSimulation"/> for its
    /// lifetime and never starts twice.
    /// </summary>
    /// <remarks>
    /// It runs before tick 0. A sequence or timer started here takes its first step on tick 0, and a
    /// wait of n ticks ends on tick n - 1.
    /// </remarks>
    protected virtual void OnStart()
    {
    }

    /// <summary>
    /// Runs once before the scene releases its entities, when it is replaced or its host ends. A start
    /// that failed before <see cref="OnStart"/> ran releases the entities without it.
    /// </summary>
    protected virtual void OnStop()
    {
    }

    /// <summary>Runs after the previous positions are saved and before entities step.</summary>
    protected virtual void OnStep(in StepContext context)
    {
    }

    /// <summary>
    /// Runs after every entity's <see cref="Entity.OnLateStep"/> and before the frame is built.
    /// </summary>
    /// <remarks>
    /// Set the scene's camera policy here. The camera's own <see cref="Scenes.Camera.OnLateStep"/>
    /// and its follow run afterwards and frame the result, unless its <see cref="Scenes.Camera.StepMode"/>
    /// holds the step.
    /// </remarks>
    protected virtual void OnLateStep(in StepContext context)
    {
    }

    /// <summary>
    /// Draws the scene's own debug geometry, as <see cref="Component.OnDebugDraw"/> describes.
    /// Draws nothing by default.
    /// </summary>
    /// <remarks>The camera, the entities and their components draw after it.</remarks>
    protected virtual void OnDebugDraw()
    {
    }

    /// <summary>
    /// Fills the scene's section of the debug overlay's scene page, as
    /// <see cref="Component.OnDebugPanel"/> describes. Writes nothing by default.
    /// </summary>
    /// <remarks>
    /// The run's seed, <see cref="Size"/>, <see cref="ClearColor"/>, <see cref="Ambient"/>, <see cref="Unlit"/> and
    /// <see cref="Paused"/> are written before this call, and the scene's
    /// entities are listed after it. A Camera row opens <see cref="Camera.OnDebugPanel"/>.
    /// </remarks>
    protected virtual void OnDebugPanel(DebugPanel panel)
    {
    }

    /// <summary>
    /// Appends assets this scene declares beyond those its entities and components own and its class's code
    /// names. Collection can run before <see cref="OnStart"/>.
    /// </summary>
    /// <remarks>
    /// Before this hook runs, the collection holds every <c>CapsuleAssets</c> member the code of this class and of
    /// each base class reads, except a class marked <see cref="ManualPreloadsAttribute"/>. Declare here what that
    /// code cannot show from construction-time state and <see cref="EntryPayload"/>, which already holds the
    /// payload the scene opens with. An override appends to <paramref name="assets"/> and changes nothing else.
    /// </remarks>
    /// <example>
    /// A scene showing one of several figures, named by its payload:
    /// <code>
    /// protected override void CollectAssets(AssetCollection assets)
    /// {
    ///     if (EntryPayload is Portrait portrait)
    ///     {
    ///         assets.Add(portrait.Texture);
    ///     }
    /// }
    /// </code>
    /// </example>
    protected internal virtual void CollectAssets(AssetCollection assets)
    {
    }

    internal void RequireUnstarted()
    {
        if (_started)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} has already started. Build a new scene for a second simulation.");
        }
    }

    internal void Start(IScenePayload? entryPayload)
    {
        RequireUnstarted();
        _started = true;

        // A host with nothing to load still collects. Forwarding a pool is recorded before any start
        // hook can take from it. A host's collection already set the same payload.
        if (Pools.PreloadsCollected)
        {
            EntryPayload = entryPayload;
        }
        else
        {
            CollectAssetPreloads(entryPayload);
        }

        JoinComposed();
        StartPending();

        // Install the camera after starts so it finds entities that have started.
        Install(_camera);

        _startHookRan = true;
        OnStart();

        // The frame drawn before the first step shows the subject where every start hook left it.
        Camera.CutToSubject();

        // A scene's first frame never interpolates.
        Camera.SavePrevious();
    }

    internal void Stop()
    {
        if (!_started || _stopped)
        {
            return;
        }

        _stopped = true;
        List<Exception>? failures = null;

        if (_startHookRan)
        {
            try
            {
                OnStop();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        // Release the camera before the entities it framed.
        if (ReferenceEquals(_camera.SceneOrNull, this))
        {
            _camera.SceneOrNull = null;
        }

        Release(failures);
    }

    // Release a composed scene that was rejected before it started. Nothing joined, and no hook runs.
    internal void Abandon()
    {
        if (_started)
        {
            throw new InvalidOperationException($"A started {GetType().Name} must be stopped, not abandoned.");
        }

        if (_stopped)
        {
            return;
        }

        _stopped = true;
        Release(null);
    }

    // Throw a single failure or all aggregated. Cleanup runs to completion before throwing.
    internal static void ThrowCleanupFailures(List<Exception>? failures)
    {
        if (failures is [Exception failure])
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more scene cleanup hooks failed.", failures);
        }
    }

    internal void BeginStep()
    {
        _stepping = true;
        StepsBegun++;

        // Pause and freeze settle once here. No step is then held for only part of the tree.
        bool paused = Paused;
        bool frozen = _freezeTicks > 0;
        if (frozen)
        {
            _freezeTicks--;
        }

        bool resolve = _holdsStale || paused != _pausedThisStep || frozen != _frozenThisStep;
        _holdsStale = false;
        _pausedThisStep = paused;
        _frozenThisStep = frozen;
        _cameraHeldThisStep = Camera.StepMode.Holds(paused, frozen);
        if (!resolve && _movedHolds.Count > 0)
        {
            ResolveMovedHolds(paused, frozen);
        }

        ForgetMovedHolds();

        Camera.SavePrevious();

        foreach (Entity entity in Entities)
        {
            if (resolve)
            {
                entity.ResolveHold(paused, frozen);
            }

            entity.SavePrevious();
        }

        Visibility.SettleArrivals();
    }

    // Makes the next step resolve every entity's hold again.
    internal void InvalidateHolds()
    {
        _holdsStale = true;
        ForgetMovedHolds();
    }

    // Makes the next step resolve the holds of the entity's subtree again, which follow its new parent.
    private void MovedHolds(Entity entity)
    {
        if (!_holdsStale && !entity.AwaitsHold)
        {
            entity.AwaitsHold = true;
            _movedHolds.Add(entity);
        }
    }

    private void ForgetMovedHolds()
    {
        foreach (Entity entity in CollectionsMarshal.AsSpan(_movedHolds))
        {
            entity.AwaitsHold = false;
        }

        _movedHolds.Clear();
    }

    // Resolves each moved subtree that is still held, parent first, in tree order, once where one moved
    // under another. A parent outside them kept its ancestry, and its resolved mode stands.
    private void ResolveMovedHolds(bool paused, bool frozen)
    {
        Span<Entity> moved = CollectionsMarshal.AsSpan(_movedHolds);
        moved.Sort(static (left, right) => left.Order.CompareTo(right.Order));

        // A subtree is the run of the tree order from its root to its last entity. One that left keeps a
        // stale label and is passed by.
        long reached = long.MinValue;
        foreach (Entity top in moved)
        {
            if (top.Order <= reached || !ReferenceEquals(top.SceneOrNull, this))
            {
                continue;
            }

            Entity last = _tree.LastHeldUnder(top);
            for (Entity at = top; ; at = at.OrderNext!)
            {
                at.ResolveHold(paused, frozen);
                if (ReferenceEquals(at, last))
                {
                    break;
                }
            }

            reached = last.Order;
        }
    }

    internal void RunStep(in StepContext context)
    {
        SteppingTick = context.Tick;
        StepSeconds = context.DeltaSeconds;
        OnStep(context);
    }

    internal void StepEntities(in StepContext context)
    {
        foreach (Entity entity in Entities)
        {
            entity.RunStep(context);
        }
    }

    internal void LateStepEntities(in StepContext context)
    {
        foreach (Entity entity in Entities)
        {
            entity.RunLateStep(context);
        }
    }

    internal void RunLateStep(in StepContext context)
    {
        OnLateStep(context);

        if (!_cameraHeldThisStep)
        {
            Camera.OnLateStep(context);
        }

        // The frame's visible region is final. Notifiers settle against it after deferred adds land.
        Camera.Settle(context, _cameraHeldThisStep);
        Visibility.Frame(Camera.VisibleRegion);
    }

    // Draw the scene, camera, and entities in step order after the step settles and deferred adds land.
    internal void RunDebugDraw()
    {
        OnDebugDraw();
        Camera.OnDebugDraw();

        // Each entity's pass owns what it draws, its components' included. The stamp clears on a throw as well.
        // A later step's draws stay unowned.
        try
        {
            foreach (Entity entity in Entities)
            {
                DebugDraw.Own(entity);
                entity.RunDebugDraw();
            }
        }
        finally
        {
            DebugDraw.Own(null);
        }
    }

    // Fill the scene page's section. The hook runs only if the scene has started.
    internal void RunDebugPanel(DebugPanel panel)
    {
        panel.Section("Scene");
        panel.Field("Seed", Run.Random.Seed.ToString(CultureInfo.InvariantCulture));
        panel.Field("Size", Size);
        panel.Field("ClearColor", ClearColor);
        panel.Field("Ambient", Ambient);
        panel.Field("Unlit", Unlit);
        panel.Field("Paused", Paused);

        if (_started)
        {
            OnDebugPanel(panel);
        }
    }

    internal void EndStep()
    {
        try
        {
            _drained = true;
            DrainPending();
            Visibility.Settle();
        }
        finally
        {
            // The step is over however the drain went.
            _stepping = false;
            _drained = false;
            SteppingTick = null;
        }
    }

    private void Install(Camera camera)
    {
        RequireUnowned(camera);
        camera.SceneOrNull = this;
        camera.RunStart();
    }

    private void RequireUnowned(Camera camera)
    {
        if (camera.SceneOrNull is not null && !ReferenceEquals(camera.SceneOrNull, this))
        {
            throw new InvalidOperationException(
                $"A {camera.GetType().Name} is already framing another scene. Install a different camera.");
        }
    }
}
