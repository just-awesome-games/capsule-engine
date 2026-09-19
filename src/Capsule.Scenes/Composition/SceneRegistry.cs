using System.ComponentModel;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;

namespace Capsule.Scenes;

/// <summary>
/// The scenes one assembly declares, indexed by class and by the scene document backing each one. The
/// registry is fixed once built. A game passes the registry its source generator emits, and hand-building
/// one is for tests.
/// </summary>
public sealed class SceneRegistry
{
    private readonly Dictionary<Type, SceneRegistration> _byType = [];
    private readonly Dictionary<string, SceneRegistration> _byDocumentName = new(StringComparer.Ordinal);
    private readonly EntityRegistry _entities;

    /// <param name="entities">The registry saying what each spawn type in a scene document constructs.</param>
    /// <param name="scenes">Every scene the assembly declares.</param>
    /// <exception cref="ArgumentException">A registration names no class, or a class or a document is registered twice.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public SceneRegistry(EntityRegistry entities, IEnumerable<SceneRegistration> scenes)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(scenes);

        _entities = entities;
        foreach (SceneRegistration registration in scenes)
        {
            if (registration.SceneType is null)
            {
                throw new ArgumentException("A scene registration names no class. Set its SceneType.", nameof(scenes));
            }

            if (!_byType.TryAdd(registration.SceneType, registration))
            {
                throw new ArgumentException($"The scene '{registration.SceneType}' is registered more than once.", nameof(scenes));
            }

            if (registration.DocumentName is { } name && !_byDocumentName.TryAdd(name, registration))
            {
                throw new ArgumentException($"The scene document '{name}' backs more than one scene.", nameof(scenes));
            }
        }
    }

    internal Dictionary<Type, SceneRegistration>.ValueCollection Registrations => _byType.Values;

    // Returns the scene document backing sceneType, or null when none does.
    internal string? DocumentNameOf(Type sceneType) => Find(sceneType).DocumentName;

    // Returns the registered scene whose class name is className, or null when none matches. Two namespaces
    // carrying that class name also return null, because the name then picks out no single scene.
    internal Type? SceneNamed(string className)
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

    // Builds the scene name composes into: the class claiming that name, or a plain Scene when none
    // does.
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

    // Every registered scene class, for a message naming what a caller could have asked for.
    internal string RegisteredTypes() => Registered.Names(_byType.Keys);
}
