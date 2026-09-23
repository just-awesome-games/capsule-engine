using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Capsule.Scenes;

namespace Capsule.Input;

/// <summary>Constructs one input driver.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate IInputDriver InputDriverFactory();

/// <summary>
/// One <see cref="InputDriverRegistry"/> entry: the name <c>--driver</c> accepts and the factory that
/// constructs the driver.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct InputDriverRegistration
{
    private readonly InputDriverFactory? _factory;

    /// <param name="name">The driver's simple class name, which is how a command line names it.</param>
    /// <param name="factory">The factory that constructs it, called once when the name is resolved.</param>
    public InputDriverRegistration(string name, InputDriverFactory factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        Name = name;
        _factory = factory;
    }

    /// <summary>The name this driver is registered under, which is how a command line names it.</summary>
    public string Name { get; }

    // Called only through InputDriverRegistry, which rejects a registration carrying no factory.
    internal IInputDriver Create() => _factory!();
}

/// <summary>
/// The input drivers a game declares, keyed by class name and fixed once built. A game passes the registry
/// its source generator emits, and hand-building one is for tests.
/// </summary>
public sealed class InputDriverRegistry
{
    private readonly Dictionary<string, InputDriverRegistration> _byName = new(StringComparer.Ordinal);

    /// <param name="drivers">Every driver the game declares.</param>
    /// <exception cref="ArgumentException">A registration names nothing, or a name is registered twice.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public InputDriverRegistry(IEnumerable<InputDriverRegistration> drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);

        foreach (InputDriverRegistration registration in drivers)
        {
            if (string.IsNullOrWhiteSpace(registration.Name))
            {
                throw new ArgumentException("An input driver registration names no driver. Set its Name.", nameof(drivers));
            }

            if (!_byName.TryAdd(registration.Name, registration))
            {
                throw new ArgumentException(
                    $"The input driver '{registration.Name}' is registered more than once. Rename one. A command line can name only one driver per class name.",
                    nameof(drivers));
            }
        }
    }

    // An empty registry, used by a game that declares no drivers.
    internal static InputDriverRegistry Empty { get; } = new([]);

    // Constructs the driver registered under name, or returns false when none is registered.
    internal bool TryCreate(string name, [NotNullWhen(true)] out IInputDriver? driver)
    {
        if (!_byName.TryGetValue(name, out InputDriverRegistration registration))
        {
            driver = null;

            return false;
        }

        driver = registration.Create();

        return true;
    }

    internal string RegisteredNames() => Registered.Names(_byName.Keys);
}
