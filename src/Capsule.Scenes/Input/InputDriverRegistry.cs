using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Capsule.Scenes.Input;

/// <summary>Constructs one input driver.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate IInputDriver InputDriverFactory();

/// <summary>
/// One input driver as an <see cref="InputDriverRegistry"/> entry: the name <c>--driver</c> takes,
/// and what constructs it.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct InputDriverRegistration
{
    private readonly InputDriverFactory? _factory;

    /// <param name="name">The driver's simple class name, which is what names it on a command line.</param>
    /// <param name="factory">What constructs it; called once, when the name is resolved.</param>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    /// <exception cref="ArgumentNullException">The factory is null.</exception>
    public InputDriverRegistration(string name, InputDriverFactory factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        Name = name;
        _factory = factory;
    }

    /// <summary>The name this driver is registered under.</summary>
    public string Name { get; }

    // Reached only through InputDriverRegistry, which rejects a registration carrying no factory.
    internal IInputDriver Create() => _factory!();
}

/// <summary>
/// The input drivers a game declares, keyed by class name, fixed once built. A game passes the
/// registry its source generator emits; hand-building one is the test path.
/// </summary>
public sealed class InputDriverRegistry
{
    private readonly Dictionary<string, InputDriverRegistration> _byName = new(StringComparer.Ordinal);

    /// <param name="drivers">Every driver the game declares.</param>
    /// <exception cref="ArgumentException">A registration names nothing, or a name is registered twice.</exception>
    /// <exception cref="ArgumentNullException">The sequence is null.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public InputDriverRegistry(IEnumerable<InputDriverRegistration> drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);

        foreach (InputDriverRegistration registration in drivers)
        {
            if (string.IsNullOrWhiteSpace(registration.Name))
            {
                throw new ArgumentException("An input driver registration must name the driver it registers.", nameof(drivers));
            }

            if (!_byName.TryAdd(registration.Name, registration))
            {
                throw new ArgumentException(
                    $"The input driver '{registration.Name}' is registered more than once; two drivers of one class name cannot both be named on a command line.",
                    nameof(drivers));
            }
        }
    }

    /// <summary>A registry holding no driver.</summary>
    public static InputDriverRegistry Empty { get; } = new([]);

    // Constructs the driver registered under name, or reports that none is.
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

    // Sorted so the message reads the same whatever order the registry was built in.
    internal string RegisteredNames()
    {
        if (_byName.Count == 0)
        {
            return "nothing";
        }

        string[] names = new string[_byName.Count];
        _byName.Keys.CopyTo(names, 0);
        Array.Sort(names, StringComparer.Ordinal);

        return string.Join(", ", names);
    }
}
