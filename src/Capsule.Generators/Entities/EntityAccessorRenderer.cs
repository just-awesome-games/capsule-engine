namespace Capsule.Generators;

// The [UnsafeAccessor] members CapsuleEntities declares, for what generated code cannot reach in plain C#:
// a member the game cannot assign, and the constructor of a class with C#'s required members.
internal static class EntityAccessorRenderer
{
    private const string UnsafeAccessor = "global::System.Runtime.CompilerServices.UnsafeAccessor";

    // C#'s required members are checked at a new expression. The scene sets a class's required references
    // after construction, so its spawner calls the constructor through an accessor the check does not reach.
    internal static string Constructors(IEnumerable<EntityModel> required) => string.Concat(required.Select(static model => $$"""

                [{{UnsafeAccessor}}({{UnsafeAccessor}}Kind.Constructor)]
                private static extern {{model.QualifiedName}} {{ConstructorName(model)}}({{model.SpawnModifier}}global::Capsule.Scenes.Spawning.EntitySpawn spawn);

        """));

    // One accessor per member, however many classes inherit it. A member of a generic class is set through a
    // generic class of accessors whose type parameters match that class's.
    internal static string Setters(IEnumerable<PropertyModel> accessed) => string.Concat(accessed
        .Select(static property => property with { TypeArguments = string.Empty })
        .Distinct()
        .GroupBy(static property => AccessorClass(property, property.TypeParameters))
        .Select(static owner => owner.Key.Length == 0
            ? $"\n{string.Join("\n\n", owner.Select(static property => Setter(property, "        ", "private")))}\n"
            : $$"""

                    private static class {{owner.Key}}{{owner.First().Constraints}}
                    {
            {{string.Join("\n\n", owner.Select(static property => Setter(property, "            ", "internal")))}}
                    }

            """));

    /// <summary>How an assignment names the accessor setting <paramref name="property"/>, through its class of accessors if it has one.</summary>
    internal static string SetterReference(PropertyModel property)
    {
        string owner = AccessorClass(property, property.TypeArguments);

        return owner.Length == 0 ? SetterName(property) : $"{owner}.{SetterName(property)}";
    }

    // New and the class's qualified name with every other character an underscore: Game.Door is New_Game_Door.
    internal static string ConstructorName(EntityModel model) =>
        "New_" + CodeText.Underscored(model.QualifiedName.Substring("global::".Length));

    // The accessor that sets one member: a field's returns a reference to it, a property's calls its setter.
    private static string Setter(PropertyModel property, string indent, string access) => property.Field
        ? $"{indent}[{UnsafeAccessor}({UnsafeAccessor}Kind.Field, Name = \"{property.Name}\")]\n"
            + $"{indent}{access} static extern ref {property.Declared} {SetterName(property)}({property.Declaring} entity);"
        : $"{indent}[{UnsafeAccessor}({UnsafeAccessor}Kind.Method, Name = \"set_{property.Name}\")]\n"
            + $"{indent}{access} static extern void {SetterName(property)}({property.Declaring} entity, {property.Declared} value);";

    // Set and the member's key capitalized: _tuning is set by SetTuning. A class's keys are unique and never
    // start with a capital, so its accessor names are unique too.
    private static string SetterName(PropertyModel property) =>
        property.Key.Length == 0 ? "Set" : "Set" + char.ToUpperInvariant(property.Key[0]) + property.Key.Substring(1);

    // The class of accessors for a member of a generic type, named for that type and taking the given type
    // arguments: a member of Game.Held<T> is set through Game_HeldAccessors<T>. Empty for any other member.
    private static string AccessorClass(PropertyModel property, string arguments)
    {
        if (property.TypeParameters.Length == 0)
        {
            return string.Empty;
        }

        string declaring = property.Declaring.Substring("global::".Length);

        return $"{CodeText.Underscored(declaring.Substring(0, declaring.IndexOf('<')))}Accessors<{arguments}>";
    }
}
