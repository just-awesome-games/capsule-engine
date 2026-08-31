using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;

namespace Capsule.Scenes;

/// <summary>
/// The scenes one assembly declares, indexed both by class and by the scene document backing one,
/// fixed once built. A game passes the registry its source generator emits; hand-building one is
/// the test path.
/// </summary>
public sealed class SceneRegistry
{
    private readonly Dictionary<Type, SceneRegistration> _byType = [];
    private readonly Dictionary<string, SceneRegistration> _byDocumentName = new(StringComparer.Ordinal);
    private readonly EntityRegistry _entities;

    /// <param name="entities">What each spawn type in a scene document constructs.</param>
    /// <param name="scenes">Every scene the assembly declares.</param>
    /// <exception cref="ArgumentException">A registration names no class, or a class or a document is registered twice.</exception>
    public SceneRegistry(EntityRegistry entities, IEnumerable<SceneRegistration> scenes)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(scenes);

        _entities = entities;
        foreach (SceneRegistration registration in scenes)
        {
            if (registration.SceneType is null)
            {
                throw new ArgumentException("A scene registration must name the class it registers.", nameof(scenes));
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

    /// <summary>
    /// The scene document backing <paramref name="sceneType"/>, or null when no document does.
    /// </summary>
    /// <exception cref="InvalidOperationException">Nothing registers that class.</exception>
    public string? DocumentNameOf(Type sceneType) => Registered(sceneType).DocumentName;

    /// <summary>Builds the scene registered for <paramref name="sceneType"/>.</summary>
    /// <exception cref="InvalidOperationException">Nothing registers that class, or a document backs it.</exception>
    public Scene Create(Type sceneType)
    {
        SceneRegistration registration = Registered(sceneType);
        if (registration.DocumentName is { } name)
        {
            throw new InvalidOperationException(
                $"The scene '{sceneType}' is composed from scene document '{name}', so it is built through that "
                + $"name rather than its class: CreateFromDocument(\"{name}\", document).");
        }

        return registration.Create();
    }

    /// <summary>
    /// Builds the scene <paramref name="name"/> composes into: the class claiming that name, or a
    /// plain <see cref="Scene"/> when no class claims it.
    /// </summary>
    /// <param name="name">The document's bare name, without the <c>.scene.json</c> suffix.</param>
    /// <param name="document">The parsed document to compose.</param>
    /// <exception cref="SpawnException">A placement's spawn type is claimed by no entity.</exception>
    public Scene CreateFromDocument(string name, SceneDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(document);

        SceneContent content = new(document, _entities);

        return _byDocumentName.TryGetValue(name, out SceneRegistration claimed)
            ? claimed.Create(content)
            : new Scene(content);
    }

    private SceneRegistration Registered(Type sceneType)
    {
        ArgumentNullException.ThrowIfNull(sceneType);

        if (!_byType.TryGetValue(sceneType, out SceneRegistration registration))
        {
            throw new InvalidOperationException(
                $"No scene is registered for '{sceneType}'. A scene registers by being a non-abstract "
                + "Capsule.Scenes.Scene with either a public parameterless constructor, or a public constructor "
                + "taking one Capsule.Scenes.SceneContent — which composes it from the scene document it names, "
                + "its class name kebab-cased unless [SceneDocument(\"name\")] overrides that. "
                + $"Registered: {RegisteredTypes()}.");
        }

        return registration;
    }

    // Sorted so the message reads the same whatever order the registry was built in.
    private string RegisteredTypes()
    {
        if (_byType.Count == 0)
        {
            return "nothing";
        }

        string[] names = new string[_byType.Count];
        int next = 0;
        foreach (Type sceneType in _byType.Keys)
        {
            names[next++] = sceneType.ToString();
        }

        Array.Sort(names, StringComparer.Ordinal);

        return string.Join(", ", names);
    }
}
