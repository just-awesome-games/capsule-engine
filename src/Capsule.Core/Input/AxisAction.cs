namespace Capsule.Input;

/// <summary>
/// A named continuous action, distinct from digital <see cref="InputAction"/>. Constructing one
/// resolves the name to an index the bindings read by, so declare a game's actions once as static
/// fields instead of building them per step.
/// </summary>
public readonly record struct AxisAction
{
    /// <summary>Names an action.</summary>
    /// <param name="name">The action's name, which binding requires to be more than whitespace.</param>
    public AxisAction(string name)
    {
        Name = name;
        Index = ActionIndex.Of(name);
    }

    /// <summary>The action's name.</summary>
    public string Name { get; }

    internal int Index { get; }
}
