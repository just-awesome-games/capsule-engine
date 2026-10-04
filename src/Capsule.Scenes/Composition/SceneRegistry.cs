using System.ComponentModel;
using System.Reflection;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;

namespace Capsule.Scenes;

/// <summary>
/// The scenes one assembly declares, indexed by class and by the scene document backing each one,
/// fixed once built.
/// </summary>
/// <remarks>
/// A game passes the registry its source generator emits. Build one by hand only in tests.
/// </remarks>
public sealed class SceneRegistry
{
    private readonly Dictionary<Type, SceneRegistration> _byType = [];
    private readonly Dictionary<string, SceneRegistration> _byDocumentName = new(StringComparer.Ordinal);
    private readonly List<SceneRegistration> _registrations = [];
    private readonly Dictionary<Type, SceneApplier> _appliers = [];
    private readonly EntityRegistry _entities;

    // The registry each logic assembly's scene classes compose with, which reads that assembly's tile map.
    internal IReadOnlyDictionary<Assembly, EntityRegistry> Owned { get; init; } = new Dictionary<Assembly, EntityRegistry>();

    /// <summary>A registry over <paramref name="scenes"/>.</summary>
    /// <param name="entities">The registry saying what each type key in a scene document constructs.</param>
    /// <param name="scenes">Every scene the assembly declares.</param>
    /// <param name="appliers">
    /// The applier of each scene class, or null for none. The first applier of a class is kept, since every logic
    /// assembly declares one for <see cref="Scene"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// A registration names no class and no document, or a class or a document is registered twice.
    /// </exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public SceneRegistry(
        EntityRegistry entities,
        IEnumerable<SceneRegistration> scenes,
        IEnumerable<KeyValuePair<Type, SceneApplier>>? appliers = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(scenes);

        _entities = entities;
        foreach (SceneRegistration registration in scenes)
        {
            if (registration.SceneType is null && registration.DocumentName is null)
            {
                throw new ArgumentException(
                    "A scene registration names neither a class nor a document. Build one through "
                    + "SceneRegistration.Plain, FromDocument or DocumentOnly.", nameof(scenes));
            }

            if (registration.SceneType is { } sceneType)
            {
                if (!_byType.TryAdd(sceneType, registration))
                {
                    throw new ArgumentException($"The scene '{sceneType}' is registered more than once.", nameof(scenes));
                }
            }

            if (registration.DocumentName is { } name && !_byDocumentName.TryAdd(name, registration))
            {
                throw new ArgumentException($"The scene document '{name}' backs more than one scene.", nameof(scenes));
            }

            _registrations.Add(registration);
        }

        foreach ((Type sceneType, SceneApplier apply) in appliers ?? [])
        {
            ArgumentNullException.ThrowIfNull(sceneType, nameof(appliers));
            ArgumentNullException.ThrowIfNull(apply, nameof(appliers));
            _appliers.TryAdd(sceneType, apply);
        }
    }

    internal IReadOnlyList<SceneRegistration> Registrations => _registrations;

    // Returns the scene document backing sceneType, or null when none does.
    internal string? DocumentNameOf(Type sceneType) => Find(sceneType).DocumentName;

    // Returns the registered scene whose class name is className, or null when none matches. Two namespaces
    // carrying that class name also return null, because the name then picks out no single scene.
    private Type? SceneNamed(string className)
    {
        Type? found = null;

        foreach (Type candidate in _byType.Keys)
        {
            if (!string.Equals(candidate.Name, className, StringComparison.Ordinal))
            {
                continue;
            }

            if (found is not null)
            {
                return null;
            }

            found = candidate;
        }

        return found;
    }

    // Resolves a name the command line gave. A registered class of that name wins, found as SceneNamed
    // finds it, and otherwise a registered document of that key. Both comparisons are ordinal. A class
    // Room and a document room are two names. Returns false when neither matches.
    internal bool TryResolveName(string name, out Type? sceneType, out string? documentName)
    {
        sceneType = SceneNamed(name);
        if (sceneType is not null)
        {
            documentName = DocumentNameOf(sceneType);

            return true;
        }

        documentName = _byDocumentName.ContainsKey(name) ? name : null;

        return documentName is not null;
    }

    internal Scene Create(Type sceneType)
    {
        SceneRegistration registration = Find(sceneType);
        if (registration.DocumentName is { } name)
        {
            throw new InvalidOperationException(
                $"The scene '{sceneType}' is composed from scene document '{name}', so it is built through that "
                + $"key, not its class: Create(new SceneKey(\"{name}\"), document).");
        }

        return registration.Create();
    }

    /// <summary>Composes <paramref name="document"/> into the scene registered for <paramref name="scene"/>, exactly as a run does.</summary>
    /// <remarks>
    /// The scene gets its registration's class, scene members and tile types. A key no registration
    /// claims composes a plain <see cref="Scene"/>. The scene is returned unstarted.
    /// </remarks>
    /// <param name="scene">The document's key, a <c>CapsuleAssets.Scenes</c> member.</param>
    /// <param name="document">The parsed document, as <see cref="SceneDocument.Parse(string)"/> returns it.</param>
    /// <returns>The composed scene.</returns>
    /// <exception cref="SceneDocumentFormatException">An entry's or the scene's members do not fit what their class declares.</exception>
    /// <exception cref="SpawnException">The document places a type the registry cannot construct.</exception>
    /// <example>
    /// A test composes a document it builds and steps it:
    /// <code>
    /// SceneDocument document = SceneDocument.Parse(json);
    /// Scene room = CapsuleScenes.Registry.Create(CapsuleAssets.Scenes.RoomScene, document);
    /// using SimulationHost host = new(room);
    /// </code>
    /// </example>
    public Scene Create(SceneKey scene, SceneDocument document)
    {
        string name = scene.Required(nameof(scene));
        ArgumentNullException.ThrowIfNull(document);

        try
        {
            return Compose(name, document);
        }
        catch (SceneDocumentFormatException exception)
        {
            // An entry's members are read as it spawns, and only this layer knows the document's key.
            throw new SceneDocumentFormatException($"scene document '{name}': {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Composes the document the game ships at <paramref name="scene"/> into the scene registered for it, read from
    /// beside the running assembly as a test project receives it.
    /// </summary>
    /// <remarks>Performs file I/O. A run composes through the host's content instead. The scene is returned unstarted.</remarks>
    /// <param name="scene">The document's key, a <c>CapsuleAssets.Scenes</c> member.</param>
    /// <returns>The composed scene.</returns>
    /// <exception cref="SceneDocumentFormatException">The document is malformed, or its members do not fit what their class declares.</exception>
    /// <exception cref="SpawnException">The document places a type the registry cannot construct.</exception>
    /// <exception cref="IOException">No shipped document is beside the assembly. Reference the logic project from the test project.</exception>
    /// <example>
    /// <code>
    /// PlayableScene room = (PlayableScene)CapsuleScenes.Registry.Create(CapsuleAssets.Scenes.RoomScene);
    /// </code>
    /// </example>
    public Scene Create(SceneKey scene) => Create(scene, Shipped(scene.Required(nameof(scene))));

    /// <summary>
    /// Composes every document the registry's assemblies ship, as <see cref="Create(SceneKey)"/> does, and fails
    /// once for all that do not compose.
    /// </summary>
    /// <remarks>
    /// A test calling it fails on any placement, palette entry or scene member that a run would refuse at load.
    /// Performs file I/O.
    /// </remarks>
    /// <exception cref="SceneDocumentFormatException">A document does not compose. The message lists each one with its defect.</exception>
    /// <example>
    /// <code>
    /// [Fact]
    /// public void EveryScenePlacementIsValid() => CapsuleScenes.Registry.ComposeAll();
    /// </code>
    /// </example>
    public void ComposeAll()
    {
        List<Exception> failures = [];
        foreach (string name in _byDocumentName.Keys.Order(StringComparer.Ordinal))
        {
            try
            {
                Create(new SceneKey(name));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                failures.Add(exception is SceneDocumentFormatException
                    ? exception
                    : new SceneDocumentFormatException($"scene document '{name}': {exception.Message}", exception));
            }
        }

        if (failures.Count > 0)
        {
            throw new SceneDocumentFormatException(
                $"{failures.Count} of {_byDocumentName.Count} scene documents do not compose:\n"
                + string.Join("\n", failures.Select(static failure => failure.Message)),
                new AggregateException(failures));
        }
    }

    /// <summary>
    /// The content a run would compose <paramref name="document"/> with for the scene class <typeparamref name="TScene"/>.
    /// </summary>
    /// <remarks>
    /// The content carries the entity registry, the tile types and the applier of <typeparamref name="TScene"/>'s
    /// authorable members, inherited ones included. The caller constructs the scene from it, typically a test
    /// subclass of an abstract scene.
    /// </remarks>
    /// <typeparam name="TScene">The scene class whose authorable members the document's top-level keys set.</typeparam>
    /// <param name="document">The document to compose, parsed or built by hand.</param>
    /// <returns>The content to pass to <typeparamref name="TScene"/>'s constructor.</returns>
    /// <example>
    /// A test subclass of an abstract scene gets the scene properties its document authors:
    /// <code>
    /// SceneContent content = CapsuleScenes.Registry.Content&lt;PlayableRoom&gt;(document);
    /// using SimulationHost host = new(new TestRoom(content));
    /// </code>
    /// </example>
    public SceneContent Content<TScene>(SceneDocument document)
        where TScene : Scene
    {
        ArgumentNullException.ThrowIfNull(document);

        return new SceneContent(document, EntitiesOf(typeof(TScene)), Apply: ApplierOf(typeof(TScene)));
    }

    // The document shipped beside the running assembly at the key name.
    private static SceneDocument Shipped(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, ShippedSceneDocument.Folder, name + ShippedSceneDocument.Extension);
        using FileStream content = File.OpenRead(path);

        try
        {
            return ShippedSceneDocument.Read(content);
        }
        catch (SceneDocumentFormatException exception)
        {
            throw new SceneDocumentFormatException($"scene document '{name}': {exception.Message}", exception);
        }
    }

    private Scene Compose(string name, SceneDocument document)
    {
        SceneContent content = new(document, _entities);

        return _byDocumentName.TryGetValue(name, out SceneRegistration claimed)
            ? claimed.Create(content)
            : new Scene(content with { Apply = ApplierOf(typeof(Scene)) });
    }

    // The applier of the scene class, or of its nearest base class that has one.
    private SceneApplier? ApplierOf(Type sceneType)
    {
        SceneApplier? apply = null;
        for (Type? current = sceneType; apply is null && current is not null; current = current.BaseType)
        {
            _appliers.TryGetValue(current, out apply);
        }

        return apply;
    }

    // The registry of the logic assembly declaring the scene class or its nearest base class, as its applier is found.
    private EntityRegistry EntitiesOf(Type sceneType)
    {
        for (Type? current = sceneType; current is not null; current = current.BaseType)
        {
            if (Owned.TryGetValue(current.Assembly, out EntityRegistry? owned))
            {
                return owned;
            }
        }

        return _entities;
    }

    private SceneRegistration Find(Type sceneType)
    {
        ArgumentNullException.ThrowIfNull(sceneType);

        if (!_byType.TryGetValue(sceneType, out SceneRegistration registration))
        {
            throw new InvalidOperationException(
                $"No scene is registered for '{sceneType}'. A scene registers by being a non-abstract "
                + "Capsule.Scenes.Scene with either a public parameterless constructor, or a public constructor "
                + "taking one Capsule.Scenes.SceneContent, which composes it from the scene document it names, "
                + "the key its namespace names unless [TypeKey(\"key\")] overrides that. "
                + $"Registered: {Registered.Names(_byType.Keys)}.");
        }

        return registration;
    }

    // Every registered class name, for a message naming what the command line could have named.
    internal string RegisteredClassNames() => Registered.Names(_byType.Keys.Select(static type => type.Name));

    // Every registered document key, for a message naming what a caller could ask for by key.
    internal string RegisteredDocumentKeys() => Registered.Names(_byDocumentName.Keys);
}
