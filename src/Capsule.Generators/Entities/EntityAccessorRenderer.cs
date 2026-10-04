namespace Capsule.Generators;

// The [UnsafeAccessor] members CapsuleEntities and CapsuleScenes declare, for what generated code cannot reach in
// plain C#: a member the game cannot assign or read, and a constructor it cannot call or that C#'s required members guard.
internal static class EntityAccessorRenderer
{
    private const string UnsafeAccessor = "global::System.Runtime.CompilerServices.UnsafeAccessor";

    // C#'s required members are checked at a new expression. Generated code sets a class's required references
    // past construction, so it calls the constructor through an accessor the check does not reach.
    internal static string Constructor(string qualifiedName, string parameter) => $$"""

                [{{UnsafeAccessor}}({{UnsafeAccessor}}Kind.Constructor)]
                private static extern {{qualifiedName}} {{ConstructorName(qualifiedName)}}({{parameter}});

        """;

    // The accessors the authored members need, one per member however many classes inherit it: a setter where
    // generated code cannot assign the member, and a getter where it cannot read an object the member holds. A member
    // of a generic class is reached through a generic class of accessors whose type parameters match that class's.
    internal static string Accessors(IEnumerable<PropertyModel> authored) => string.Concat(authored
        .Where(static property => Sets(property) || Gets(property))
        .Select(static property => property with { TypeArguments = string.Empty })
        .Distinct()
        .GroupBy(static property => AccessorClass(property, property.TypeParameters))
        .Select(static owner => owner.Key.Length == 0
            ? $"\n{string.Join("\n\n", owner.Select(static property => Accessor(property, "        ", "private")))}\n"
            : $$"""

                    private static class {{owner.Key}}{{owner.First().Constraints}}
                    {
            {{string.Join("\n\n", owner.Select(static property => Accessor(property, "            ", "internal")))}}
                    }

            """));

    /// <summary>How an assignment names the accessor setting <paramref name="property"/>, through its class of accessors if it has one.</summary>
    internal static string SetterReference(PropertyModel property)
    {
        string owner = AccessorClass(property, property.TypeArguments);

        return owner.Length == 0 ? SetterName(property) : $"{owner}.{SetterName(property)}";
    }

    /// <summary>How a read names the accessor getting <paramref name="property"/>, through its class of accessors if it has one.</summary>
    internal static string GetterReference(PropertyModel property)
    {
        string owner = AccessorClass(property, property.TypeArguments);

        return owner.Length == 0 ? GetterName(property) : $"{owner}.{GetterName(property)}";
    }

    // Game.Door's constructor accessor is New_Game_Door.
    internal static string ConstructorName(string qualifiedName) => "New_" + CodeText.TypeIdentifier(qualifiedName);

    // A held field that generated code cannot read is read through the reference its setter accessor returns.
    private static bool Sets(PropertyModel property) => !property.Direct && (!property.Held || property is { Field: true, Readable: false });

    // A property holding an object whose getter generated code cannot call. A field's setter accessor reads it too.
    private static bool Gets(PropertyModel property) => property is { Kind: PropertyKind.Object, Array: false, Readable: false, Field: false };

    // The setter, the getter or both that one member needs.
    private static string Accessor(PropertyModel property, string indent, string access)
    {
        string setter = Sets(property) ? Setter(property, indent, access) : string.Empty;
        string getter = Gets(property)
            ? $"{indent}[{UnsafeAccessor}({UnsafeAccessor}Kind.Method, Name = \"get_{property.Name}\")]\n"
                + $"{indent}{access} static extern {property.Declared} {GetterName(property)}({property.Declaring} owner);"
            : string.Empty;

        return setter.Length > 0 && getter.Length > 0 ? setter + "\n\n" + getter : setter + getter;
    }

    // The accessor that sets one member: a field's returns a reference to it, a property's calls its setter.
    private static string Setter(PropertyModel property, string indent, string access) => property.Field
        ? $"{indent}[{UnsafeAccessor}({UnsafeAccessor}Kind.Field, Name = \"{property.Name}\")]\n"
            + $"{indent}{access} static extern ref {property.Declared} {SetterName(property)}({property.Declaring} owner);"
        : $"{indent}[{UnsafeAccessor}({UnsafeAccessor}Kind.Method, Name = \"set_{property.Name}\")]\n"
            + $"{indent}{access} static extern void {SetterName(property)}({property.Declaring} owner, {property.Declared} value);";

    // Set or Get and the member's key capitalized: _tuning is set by SetTuning. A class's keys are unique and never
    // start with a capital, so its accessor names are unique too.
    private static string SetterName(PropertyModel property) => "Set" + Capitalized(property.Key);

    private static string GetterName(PropertyModel property) => "Get" + Capitalized(property.Key);

    private static string Capitalized(string key) => key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key.Substring(1);

    // The class of accessors for a member of a generic type, named for that type and taking the given type
    // arguments: a member of Game.Held<T> is set through Game_HeldAccessors<T>. Empty for any other member.
    private static string AccessorClass(PropertyModel property, string arguments) => property.TypeParameters.Length == 0
        ? string.Empty
        : $"{CodeText.TypeIdentifier(property.Declaring.Substring(0, property.Declaring.IndexOf('<')))}Accessors<{arguments}>";
}
