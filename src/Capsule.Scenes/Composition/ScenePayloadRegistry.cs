using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Capsule.Scenes;

/// <summary>Constructs one scene payload.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate IScenePayload ScenePayloadFactory();

/// <summary>
/// One <see cref="ScenePayloadRegistry"/> entry: the name <c>--payload</c> accepts and the factory that
/// constructs the payload.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct ScenePayloadRegistration
{
    private readonly ScenePayloadFactory? _factory;

    /// <summary>The name this payload is registered under, which is how a command line names it.</summary>
    public string Name { get; }

    /// <param name="name">The payload's simple class name, which is how a command line names it.</param>
    /// <param name="factory">The factory that constructs it, called once when the name is resolved.</param>
    public ScenePayloadRegistration(string name, ScenePayloadFactory factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        Name = name;
        _factory = factory;
    }

    // Called only through ScenePayloadRegistry, which rejects a registration carrying no factory.
    internal IScenePayload Create() => _factory!();
}

/// <summary>
/// The scene payloads a game declares, keyed by class name and fixed once built. A game passes the registry
/// its source generator emits, and hand-building one is for tests.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ScenePayloadRegistry
{
    private readonly Dictionary<string, ScenePayloadRegistration> _byName = new(StringComparer.Ordinal);

    // An empty registry, used by a game that declares no payloads.
    internal static ScenePayloadRegistry Empty { get; } = new([]);

    /// <param name="payloads">Every payload the game declares.</param>
    /// <exception cref="ArgumentException">A registration names nothing, or a name is registered twice.</exception>
    public ScenePayloadRegistry(IEnumerable<ScenePayloadRegistration> payloads)
    {
        ArgumentNullException.ThrowIfNull(payloads);

        foreach (ScenePayloadRegistration registration in payloads)
        {
            if (string.IsNullOrWhiteSpace(registration.Name))
            {
                throw new ArgumentException("A scene payload registration names no payload. Set its Name.", nameof(payloads));
            }

            if (!_byName.TryAdd(registration.Name, registration))
            {
                throw new ArgumentException(
                    $"The scene payload '{registration.Name}' is registered more than once. Rename one. A command line can name only one payload per class name.",
                    nameof(payloads));
            }
        }
    }

    // Constructs the payload registered under name, or returns false when none is registered.
    internal bool TryCreate(string name, [NotNullWhen(true)] out IScenePayload? payload)
    {
        payload = _byName.TryGetValue(name, out ScenePayloadRegistration registration) ? registration.Create() : null;

        return payload is not null;
    }

    internal string RegisteredNames() => Registered.Names(_byName.Keys);
}
