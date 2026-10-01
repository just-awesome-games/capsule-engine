using System.ComponentModel;
using System.Text.Json;
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
    private readonly TileTypeComposer? _tileTypes;

    /// <summary>A registry over <paramref name="scenes"/>.</summary>
    /// <param name="entities">The registry saying what each spawn type in a scene document constructs.</param>
    /// <param name="scenes">Every scene the assembly declares.</param>
    /// <param name="tileTypes">The composer of every tile type the assembly declares, or null when it declares none.</param>
    /// <param name="appliers">The applier of each scene class declaring authorable members, or null when none does.</param>
    /// <exception cref="ArgumentException">
    /// A registration names no class and no document, or a class, a document or an applier's class is registered twice.
    /// </exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public SceneRegistry(
        EntityRegistry entities,
        IEnumerable<SceneRegistration> scenes,
        TileTypeComposer? tileTypes = null,
        IEnumerable<KeyValuePair<Type, SceneApplier>>? appliers = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(scenes);

        _entities = entities;
        _tileTypes = tileTypes;
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
            if (!_appliers.TryAdd(sceneType, apply))
            {
                throw new ArgumentException($"The scene '{sceneType}' has more than one applier.", nameof(appliers));
            }
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
    /// The scene gets its registration's class, camera, scene properties and tile types. A key no registration
    /// claims composes a plain <see cref="Scene"/>. The scene is returned unstarted.
    /// </remarks>
    /// <param name="scene">The document's key, a <c>CapsuleAssets.Scenes</c> member.</param>
    /// <param name="document">The parsed document, as <see cref="SceneDocumentFile.Parse(string)"/> returns it.</param>
    /// <returns>The composed scene.</returns>
    /// <exception cref="SceneDocumentFormatException">An entry's or the scene's properties do not fit what their class declares.</exception>
    /// <exception cref="SpawnException">The document places a type the registry cannot construct.</exception>
    /// <example>
    /// A test composes the room its game ships and steps it:
    /// <code>
    /// SceneDocument document = SceneDocumentFile.Load("room.scene.json");
    /// Scene room = CapsuleScenes.Registry.Create(CapsuleAssets.Scenes.RoomScene, document);
    /// using SimulationHost host = new(room);
    /// </code>
    /// </example>
    public Scene Create(SceneKey scene, SceneDocument document)
    {
        string name = scene.Required(nameof(scene));
        ArgumentNullException.ThrowIfNull(document);

        SceneContent content = new(document, _entities, TileTypes: _tileTypes);

        try
        {
            return _byDocumentName.TryGetValue(name, out SceneRegistration claimed)
                ? claimed.Create(content)
                : new Scene(content);
        }
        catch (SceneDocumentFormatException exception)
        {
            // An entry's properties are read as it spawns, and only this layer knows the document's key.
            string derived = document.Source is { } source ? $" (from {source.Path})" : string.Empty;

            throw new SceneDocumentFormatException($"scene document '{name}'{derived}: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// The content a run would compose <paramref name="document"/> with for the scene class <typeparamref name="TScene"/>.
    /// </summary>
    /// <remarks>
    /// The content carries the entity registry, the tile types and the applier of <typeparamref name="TScene"/>'s
    /// authorable members, inherited ones included. Its camera is null. Only a shipped document fixes a scene's
    /// camera. The caller constructs the scene from it, typically a test subclass of an abstract scene.
    /// </remarks>
    /// <typeparam name="TScene">The scene class whose authorable members the document's <c>properties</c> set.</typeparam>
    /// <param name="document">The document to compose, parsed or built by hand.</param>
    /// <returns>The content to pass to <typeparamref name="TScene"/>'s constructor.</returns>
    /// <exception cref="SceneDocumentFormatException">
    /// The document authors properties, but <typeparamref name="TScene"/> declares no authorable member and inherits none.
    /// </exception>
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

        SceneApplier? apply = null;
        for (Type? current = typeof(TScene); apply is null && current is not null && current != typeof(Scene); current = current.BaseType)
        {
            _appliers.TryGetValue(current, out apply);
        }

        if (apply is null && document.Settings.Properties is { ValueKind: JsonValueKind.Object } properties
            && properties.EnumerateObject().Any())
        {
            throw new SceneDocumentFormatException(
                $"The document authors properties, but '{typeof(TScene)}' declares no authorable member and inherits none. "
                + "Mark the members the properties set [Authorable] in a scene class of the game's logic assembly, "
                + "or remove the document's properties.");
        }

        return new SceneContent(document, _entities, Apply: apply, TileTypes: _tileTypes);
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
                + "the key its namespace names unless [SceneDocument(\"key\")] overrides that. "
                + $"Registered: {Registered.Names(_byType.Keys)}.");
        }

        return registration;
    }

    // Every registered class name, for a message naming what the command line could have named.
    internal string RegisteredClassNames() => Registered.Names(_byType.Keys.Select(static type => type.Name));

    // Every registered document key, for a message naming what a caller could ask for by key.
    internal string RegisteredDocumentKeys() => Registered.Names(_byDocumentName.Keys);
}
