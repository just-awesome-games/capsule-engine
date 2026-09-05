using System.ComponentModel;
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
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
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

    // The scene document backing sceneType, or null when none does.
    internal string? DocumentNameOf(Type sceneType) => Registered(sceneType).DocumentName;

    internal Scene Create(Type sceneType)
    {
        SceneRegistration registration = Registered(sceneType);
        if (registration.DocumentName is { } name)
        {
            throw new InvalidOperationException(
                $"The scene '{sceneType}' is composed from scene document '{name}', so it is built through that "
                + $"name rather than its class: CreateFromDocument(\"{name}\", document).");
        }

        Scene scene = registration.Create();
        scene.DeclareTextures(registration.Textures);

        return scene;
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

        Scene scene = claimed.Create(content);
        scene.DeclareTextures(claimed.Textures);

        return scene;
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
                + "the key its namespace names unless [SceneDocument(\"key\")] overrides that. "
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
