using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Capsule.Scenes;

/// <summary>
/// One <see cref="FactoryRegistry{T}"/> entry: the class name a command line gives, as <c>--driver</c> or
/// <c>--payload</c>, and the factory that constructs the instance.
/// </summary>
/// <typeparam name="T">What the factory constructs, as an input driver or a scene payload.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct FactoryRegistration<T>
    where T : class
{
    private readonly Func<T>? _factory;

    /// <summary>The name this class is registered under, which is how a command line names it.</summary>
    public string Name { get; }

    /// <param name="name">The class's simple name, which is how a command line names it.</param>
    /// <param name="factory">The factory that constructs it, called once when the name is resolved.</param>
    public FactoryRegistration(string name, Func<T> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        Name = name;
        _factory = factory;
    }

    // Called only through FactoryRegistry, which rejects a registration carrying no factory.
    internal T Create() => _factory!();
}

/// <summary>
/// The classes of one kind a game declares for a command line to name, keyed by class name and fixed once
/// built. A game passes the registries its source generator emits, and hand-building one is for tests.
/// </summary>
/// <typeparam name="T">What each registered factory constructs, as an input driver or a scene payload.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class FactoryRegistry<T>
    where T : class
{
    private readonly Dictionary<string, FactoryRegistration<T>> _byName = new(StringComparer.Ordinal);

    // An empty registry, used by a game that declares no class of this kind.
    internal static FactoryRegistry<T> Empty { get; } = new([]);

    /// <param name="registrations">Every class of this kind the game declares.</param>
    /// <exception cref="ArgumentException">A registration names nothing, or a name is registered twice.</exception>
    public FactoryRegistry(IEnumerable<FactoryRegistration<T>> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        foreach (FactoryRegistration<T> registration in registrations)
        {
            if (string.IsNullOrWhiteSpace(registration.Name))
            {
                throw new ArgumentException($"A {typeof(T).Name} registration names no class. Set its Name.", nameof(registrations));
            }

            if (!_byName.TryAdd(registration.Name, registration))
            {
                throw new ArgumentException(
                    $"The {typeof(T).Name} '{registration.Name}' is registered more than once. Rename one. A command line can name only one class per name.",
                    nameof(registrations));
            }
        }
    }

    // Constructs the class registered under name, or returns false when none is registered.
    internal bool TryCreate(string name, [NotNullWhen(true)] out T? created)
    {
        created = _byName.TryGetValue(name, out FactoryRegistration<T> registration) ? registration.Create() : null;

        return created is not null;
    }

    internal string RegisteredNames() => Registered.Names(_byName.Keys);
}
