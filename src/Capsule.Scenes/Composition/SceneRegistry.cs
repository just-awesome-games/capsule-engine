using System.ComponentModel;
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
    private readonly EntityRegistry _entities;

    /// <summary>A registry over <paramref name="scenes"/>.</summary>
    /// <param name="entities">The registry saying what each spawn type in a scene document constructs.</param>
    /// <param name="scenes">Every scene the assembly declares.</param>
    /// <exception cref="ArgumentException">
    /// A registration names no class and no document, or a class or a document is registered twice.
    /// </exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public SceneRegistry(EntityRegistry entities, IEnumerable<SceneRegistration> scenes)
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
                + $"name, not its class: CreateFromDocument(\"{name}\", document).");
        }

        return registration.Create();
    }

    // Builds the scene the document composes into: the registration claiming that key, or a plain
    // Scene when none does. A generated registry always registers every shipped document, so the
    // fallback is reached only by a hand-built registry that names no registration for it.
    internal Scene CreateFromDocument(string name, SceneDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(document);

        SceneContent content = new(document, _entities);

        if (!_byDocumentName.TryGetValue(name, out SceneRegistration claimed))
        {
            return new Scene(content);
        }

        return claimed.Create(content);
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
                + $"Registered: {RegisteredTypes()}.");
        }

        return registration;
    }

    // Every registered class, for a message naming what a caller could ask for by class.
    private string RegisteredTypes() => Registered.Names(_byType.Keys);

    // Every registered class name, for a message naming what the command line could have named.
    internal string RegisteredClassNames()
    {
        List<string> names = new(_byType.Count);
        foreach (Type sceneType in _byType.Keys)
        {
            names.Add(sceneType.Name);
        }

        return Registered.Names(names);
    }

    // Every registered document key, for a message naming what a caller could ask for by key.
    internal string RegisteredDocumentKeys() => Registered.Names(_byDocumentName.Keys);
}
