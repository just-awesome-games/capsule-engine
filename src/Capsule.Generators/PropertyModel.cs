using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

internal enum PropertyKind
{
    // One of PropertySchema.BuiltIns, read by the method its entry names.
    BuiltIn,

    // An enum member or a definition, read as a name the generated switch matches.
    Named,

    // A type declaring [JsonConverter], read through that converter at load.
    Converted,

    // An entity class or an interface, written as the id of another entry and set once every entry is constructed.
    Reference,
}

/// <summary>
/// One field or property of an entity class a document entry's key may name: an authorable member a
/// placement sets, or a member it cannot set with the reason why.
/// </summary>
/// <param name="Key">The member's name camel-cased, a field's leading underscore dropped, which is its JSON key.</param>
/// <param name="Required">
/// Whether every placement must set it: <c>[Authorable(Required = true)]</c>, or C#'s <c>required</c> on an entity reference.
/// </param>
/// <param name="RequiredKeyword">Whether the member carries C#'s <c>required</c>.</param>
/// <param name="Direct">Whether generated code assigns it in plain C#, where otherwise an accessor sets it.</param>
/// <param name="Declaring">
/// The fully qualified type declaring the member, which its accessor takes. A generic type is written with
/// its own type parameters.
/// </param>
/// <param name="TypeParameters">The declaring type's type parameters, outer types' first, or empty.</param>
/// <param name="TypeArguments">What the entity's base closes those parameters with, fully qualified.</param>
/// <param name="Constraints">The <c>where</c> clauses on those parameters, fully qualified, or empty.</param>
/// <param name="Type">The fully qualified type, without the <c>?</c> of a nullable one.</param>
/// <param name="DisplayType">The member's type as a message names it.</param>
/// <param name="Nullable">Whether a JSON null is accepted.</param>
/// <param name="Names">An enum's members or a type's definitions, each with its JSON name.</param>
/// <param name="Converter">The fully qualified converter a converted type declares.</param>
/// <param name="Refusal">Why a placement cannot set the member and the fix, or null when it can.</param>
/// <param name="Clash">An earlier authorable member taking the same key, or null.</param>
internal readonly record struct PropertyModel(
    string Name,
    string Key,
    bool Authorable,
    bool Required,
    bool RequiredKeyword,
    bool Field,
    bool Direct,
    PropertyKind Kind,
    string Declaring,
    string TypeParameters,
    string TypeArguments,
    string Constraints,
    string Type,
    string DisplayType,
    bool Nullable,
    EquatableArray<(string Json, string Member)> Names,
    string? Converter,
    string? Refusal,
    string? Clash,
    DeclaredAt At)
{
    internal bool Settable => Refusal is null && Clash is null;

    // C#'s required on anything but an [Authorable] entity reference, which only code can satisfy.
    internal bool CodeOnly => RequiredKeyword && !(Authorable && Kind == PropertyKind.Reference);
}

/// <summary>An [Authorable] member no placement can set, or the later of two taking one key.</summary>
internal readonly record struct AuthorableFault(DeclaredAt At, string Member, string Key, string? Refusal, string? Clash);

/// <summary>A type a scene document writes in a JSON form of the engine's own.</summary>
/// <param name="Type">The type as the generator displays it fully qualified.</param>
/// <param name="Read">The <c>EntityProperties</c> method the spawner reads it with.</param>
/// <param name="Form">What to write, as a build failure names it.</param>
/// <param name="Accepts">Whether a value, as the build's placement attribute carries it, is in this form.</param>
internal sealed record BuiltInForm(string Type, string Read, string Form, Func<object?, bool> Accepts);

// A placement sets the members its class marks [Authorable]. Every other field and property a key could name
// is kept with the reason a placement cannot set it.
internal static class PropertySchema
{
    // A new built-in type is one entry here and one read method of the same name on EntityProperties.
    internal static readonly BuiltInForm[] BuiltIns =
    [
        new("bool", "Bool", "true or false", static value => value is bool),
        new("int", "Int", "a whole number in int range", static value => value is int),
        new("float", "Float", "a finite number", IsFloat),
        new("string", "String", "a string in quotes", static value => value is string),
        new("global::System.Numerics.Vector2", "Vector2", "[x, y] with both numbers finite", static value =>
            value is EquatableArray<object?> { Items: { Length: 2 } pair } && IsFloat(pair[0]) && IsFloat(pair[1])),
        new("global::Capsule.Rendering.ColorRgba", "Color", "\"#rrggbb\" or \"#rrggbbaa\"", static value =>
            value is string text && text.Length is 7 or 9 && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit)),
    ];

    /// <summary>What an entity reference is written as, as a build failure names it.</summary>
    internal const string ReferenceForm = "an entity id, a whole number";

    private const string ConverterAttribute = "System.Text.Json.Serialization.JsonConverterAttribute";
    private const string Converter = "System.Text.Json.Serialization.JsonConverter`1";
    private const string Flags = "System.FlagsAttribute";

    private static readonly string Supported =
        $"Use {string.Join(", ", BuiltIns.Select(static form => form.Type.Replace("global::", string.Empty)))}, a non-[Flags] enum or a nullable of one of those, an Entity class or an interface, "
        + "declare [JsonConverter(typeof(...))] on the type, or give a readonly struct, or a record class without settable members, "
        + "public static readonly fields of its own type for a document to name";

    internal static BuiltInForm? BuiltIn(string type) => Array.Find(BuiltIns, form => form.Type == type);

    /// <summary>Whether a member of <paramref name="type"/> names another entry: an entity class or any interface.</summary>
    internal static bool IsReference(ITypeSymbol type, Compilation compilation) =>
        type.TypeKind == TypeKind.Interface
        || (type is INamedTypeSymbol { TypeKind: TypeKind.Class } named
            && (named.ToDisplayString() == Symbols.Entity || Symbols.DerivesFrom(named, compilation, Symbols.Entity)));

    /// <summary>
    /// Every type a member taking an entity of <paramref name="type"/> may declare: the class, its base classes and
    /// its interfaces, fully qualified.
    /// </summary>
    internal static EquatableArray<string> AssignableTo(INamedTypeSymbol type)
    {
        IEnumerable<INamedTypeSymbol> types = type.AllInterfaces;
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            types = types.Append(current);
        }

        return new(types.Select(static assignable => assignable.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToImmutableArray());
    }

    /// <summary>
    /// Every field and property of <paramref name="type"/> and its game base classes that a key could name,
    /// base classes first. A member that a derived member the game can see hides is left out, as in C# member
    /// lookup. An override stands in for the member it overrides.
    /// </summary>
    internal static EquatableArray<PropertyModel> Of(INamedTypeSymbol type, Compilation compilation)
    {
        INamedTypeSymbol? engineEntity = compilation.GetTypeByMetadataName(Symbols.Entity);
        INamedTypeSymbol? authorable = compilation.GetTypeByMetadataName(Symbols.AuthorableAttribute);
        HashSet<string> hidden = new(StringComparer.Ordinal);
        List<PropertyModel> properties = [];
        for (INamedTypeSymbol? current = type;
            current is not null && !SymbolEqualityComparer.Default.Equals(current, engineEntity);
            current = current.BaseType)
        {
            ImmutableArray<ISymbol> members = current.GetMembers();
            List<PropertyModel> declared = members
                .Where(static member => member is IPropertySymbol { IsIndexer: false } or IFieldSymbol && member.CanBeReferencedByName)
                .Where(member => !hidden.Contains(member.Name))
                .Select(member => Describe(member, authorable, compilation))
                .OfType<PropertyModel>()
                .ToList();

            hidden.UnionWith(members
                .Where(member => compilation.IsSymbolAccessibleWithin(member, compilation.Assembly))
                .Select(static member => member.Name));
            properties.InsertRange(0, declared);
        }

        // The later of two authorable members taking one key is the compile error.
        for (int i = 0; i < properties.Count; i++)
        {
            PropertyModel later = properties[i];
            int earlier = properties.FindIndex(0, i, other => other.Authorable && other.Key == later.Key);
            if (later.Authorable && earlier >= 0)
            {
                properties[i] = later with { Clash = properties[earlier].Name };
            }
        }

        return new(properties.ToImmutableArray());
    }

    /// <summary>What is wrong with one [Authorable] member where it is declared, or null when nothing is.</summary>
    internal static AuthorableFault? FaultOf(ISymbol member, Compilation compilation)
    {
        DeclaredAt at = DeclaredAt.From(member.Locations.FirstOrDefault() ?? Location.None);
        PropertyModel model = Of(member.ContainingType, compilation).Items.FirstOrDefault(property => property.At == at);
        string name = $"{member.ContainingType.ToDisplayString()}.{member.Name}";

        return model.Refusal is not null || model.Clash is not null
            ? new AuthorableFault(at, name, model.Key, model.Refusal, model.Clash)
            : null;
    }

    // Null for a static member without [Authorable], which no key names. An override is the member it
    // overrides: it carries an [Authorable] mark from anywhere along its chain, and is set through the nearest
    // declaration with a setter. A chain with no setter stays at this declaration, where the refusal reports.
    private static PropertyModel? Describe(ISymbol member, INamedTypeSymbol? authorable, Compilation compilation)
    {
        AttributeData? mark = OverrideChain(member)
            .SelectMany(static overridden => overridden.GetAttributes())
            .FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, authorable));
        member = OverrideChain(member).FirstOrDefault(static overridden => overridden is not IPropertySymbol { SetMethod: null }) ?? member;
        if (member.IsStatic && mark is null)
        {
            return null;
        }

        (ITypeSymbol declared, NullableAnnotation annotation, bool keyword, bool direct) = member switch
        {
            IPropertySymbol property => (
                property.Type,
                property.NullableAnnotation,
                property.IsRequired,
                property.SetMethod is { IsInitOnly: false } setter && compilation.IsSymbolAccessibleWithin(setter, compilation.Assembly)),
            IFieldSymbol field => (
                field.Type,
                field.NullableAnnotation,
                field.IsRequired,
                compilation.IsSymbolAccessibleWithin(field, compilation.Assembly)),
            _ => throw new ArgumentException("Only a field or property is described.", nameof(member)),
        };

        ITypeSymbol type = declared;
        bool nullable = type.IsReferenceType && annotation == NullableAnnotation.Annotated;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } wrapped)
        {
            type = wrapped.TypeArguments[0];
            nullable = true;
        }

        (PropertyKind kind, string? converter, EquatableArray<(string, string)> names, string? unsupported) =
            Classify(type, nullable && type.IsValueType, compilation);
        bool reference = kind == PropertyKind.Reference;
        bool marked = mark?.NamedArguments.Any(static named => named is { Key: "Required", Value.Value: true }) ?? false;
        string? refusal = mark is null
            ? "is not [Authorable]. Mark it [Authorable] for a placement to set it"
            : Misuse(member, keyword && !reference)
                ?? (reference && marked ? "is an entity reference, which Required = true does not mark. Drop Required = true and write C#'s required instead" : null)
                ?? unsupported;

        List<INamedTypeSymbol> owners = [];
        for (INamedTypeSymbol? owner = member.ContainingType; owner is not null; owner = owner.ContainingType)
        {
            owners.Insert(0, owner);
        }

        return new PropertyModel(
            member.Name,
            CamelCase(member is IFieldSymbol && member.Name.StartsWith("_", StringComparison.Ordinal) ? member.Name.Substring(1) : member.Name),
            mark is not null,
            marked || (keyword && reference),
            keyword,
            member is IFieldSymbol,
            direct,
            kind,
            member.ContainingType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            string.Join(", ", owners.SelectMany(static owner => owner.OriginalDefinition.TypeParameters).Select(static parameter => parameter.Name)),
            string.Join(", ", owners.SelectMany(static owner => owner.TypeArguments).Select(static argument => argument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))),
            string.Concat(owners.Select(static owner => Constraints(owner.OriginalDefinition))),
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            declared.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            nullable,
            names,
            converter,
            refusal,
            null,
            DeclaredAt.From(member.Locations.FirstOrDefault() ?? Location.None));
    }

    // The member, then each property it overrides in turn.
    private static IEnumerable<ISymbol> OverrideChain(ISymbol member)
    {
        for (ISymbol? current = member; current is not null; current = (current as IPropertySymbol)?.OverriddenProperty)
        {
            yield return current;
        }
    }

    // What makes an [Authorable] member one no placement can set, whatever its type.
    private static string? Misuse(ISymbol member, bool keyword) => member switch
    {
        { IsStatic: true } => "is static. A placement sets one entity's member. Make it an instance member, or drop [Authorable]",
        IFieldSymbol { IsReadOnly: true } => "is readonly. Drop readonly, or drop [Authorable]",
        IPropertySymbol { SetMethod: null } => "has no setter. Add a set or init accessor of any access, or drop [Authorable]",
        _ when keyword => "is required, which only an entity reference carries. Drop required and write [Authorable(Required = true)]",
        _ => null,
    };

    // A generic type's own where clauses, as its fully qualified display renders them.
    private static string Constraints(INamedTypeSymbol type)
    {
        string display = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(
            SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeTypeConstraints));
        int where = display.IndexOf(" where ", StringComparison.Ordinal);

        return where < 0 ? string.Empty : display.Substring(where);
    }

    // A built-in form first, then an entity reference, then a converter the type declares, then the type's own definitions.
    private static (PropertyKind Kind, string? Converter, EquatableArray<(string, string)> Names, string? Refusal) Classify(
        ITypeSymbol type,
        bool nullableValue,
        Compilation compilation)
    {
        string unsupported = $"has type '{type.ToDisplayString()}', which a scene document cannot carry. {Supported}";
        if (BuiltIn(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) is not null)
        {
            return (PropertyKind.BuiltIn, null, default, null);
        }

        if (IsReference(type, compilation))
        {
            return (PropertyKind.Reference, null, default, null);
        }

        if (type is not INamedTypeSymbol named)
        {
            return (PropertyKind.Converted, null, default, unsupported);
        }

        if (named.TypeKind == TypeKind.Enum && Symbols.Attribute(named, compilation, Flags) is null)
        {
            return (PropertyKind.Named, null, Names(named, static field => field.HasConstantValue), null);
        }

        // A nullable value type wraps only the built-in forms above.
        if (nullableValue)
        {
            return (PropertyKind.Converted, null, default, unsupported);
        }

        if (Symbols.Attribute(named, compilation, ConverterAttribute) is { } declared)
        {
            INamedTypeSymbol? converter = declared.ConstructorArguments.Length == 1 ? declared.ConstructorArguments[0].Value as INamedTypeSymbol : null;

            return converter is not null && IsConverterFor(converter, named, compilation)
                ? (PropertyKind.Converted, converter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), default, null)
                : (PropertyKind.Converted, null, default,
                    $"has type '{named.ToDisplayString()}', whose [JsonConverter] names '{converter?.ToDisplayString() ?? "no type"}'. Name a non-abstract JsonConverter<{named.ToDisplayString()}> this assembly can see, with a public parameterless constructor");
        }

        // Every placement naming a definition shares one instance, so only a readonly struct or a record class
        // without settable members can declare them. What a member's own type allows is not policed.
        bool immutable = named is { TypeKind: TypeKind.Struct, IsReadOnly: true }
            || (named is { TypeKind: TypeKind.Class, IsRecord: true } && !HasSettableMembers(named));
        EquatableArray<(string, string)> definitions = Names(named, field =>
            field is { IsStatic: true, IsReadOnly: true, DeclaredAccessibility: Accessibility.Public }
            && SymbolEqualityComparer.Default.Equals(field.Type, named));

        return !immutable || definitions.Items.IsEmpty
            ? (PropertyKind.Converted, null, default, unsupported)
            : (PropertyKind.Named, null, definitions, null);
    }

    private static bool HasSettableMembers(INamedTypeSymbol record)
    {
        for (INamedTypeSymbol? current = record; current is { IsRecord: true }; current = current.BaseType)
        {
            if (current.GetMembers().Any(static member => member switch
            {
                IPropertySymbol { IsStatic: false, SetMethod.IsInitOnly: false } => true,
                IFieldSymbol { IsStatic: false, IsReadOnly: false, IsConst: false, IsImplicitlyDeclared: false } => true,
                _ => false,
            }))
            {
                return true;
            }
        }

        return false;
    }

    // A converter the generated read can name and construct through a new() constraint, whose Read returns
    // exactly the member's type.
    private static bool IsConverterFor(INamedTypeSymbol converter, ITypeSymbol type, Compilation compilation)
    {
        bool constructible = !converter.IsAbstract
            && !converter.IsUnboundGenericType
            && compilation.IsSymbolAccessibleWithin(converter, compilation.Assembly)
            && converter.InstanceConstructors.Any(static constructor =>
                constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public);

        INamedTypeSymbol? generic = compilation.GetTypeByMetadataName(Converter);
        for (INamedTypeSymbol? current = converter; constructible && current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, generic))
            {
                return SymbolEqualityComparer.Default.Equals(current.TypeArguments[0], type);
            }
        }

        return false;
    }

    // Each name is camel-cased, as the document writes its own enum values. A field whose name is already
    // taken is an alias and is passed over.
    private static EquatableArray<(string Json, string Member)> Names(INamedTypeSymbol type, Func<IFieldSymbol, bool> named)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);

        return new(type.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(field => named(field) && seen.Add(CamelCase(field.Name)))
            .Select(static field => (CamelCase(field.Name), field.Name))
            .ToImmutableArray());
    }

    // The rule System.Text.Json's camel-case naming policy applies to an identifier: the leading capitals are
    // lowered, except one after the first that a lower-case letter follows, which starts the next word.
    // URLValue is urlValue.
    private static string CamelCase(string name)
    {
        char[] characters = name.ToCharArray();
        for (int i = 0; i < characters.Length && char.IsUpper(characters[i]); i++)
        {
            if (i > 0 && i + 1 < characters.Length && !char.IsUpper(characters[i + 1]))
            {
                break;
            }

            characters[i] = char.ToLowerInvariant(characters[i]);
        }

        return new string(characters);
    }

    private static bool IsFloat(object? value) => value is int || (value is double number && !float.IsInfinity((float)number));
}

/// <summary>An array compared by its elements, so a model holding one caches between generator runs.</summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> items) : IEquatable<EquatableArray<T>>
{
    private readonly ImmutableArray<T> _items = items;

    internal ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public bool Equals(EquatableArray<T> other) => Models.SequenceEqual(Items, other.Items);

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode() => Items.Length;
}
