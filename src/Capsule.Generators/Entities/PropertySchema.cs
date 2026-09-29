using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// A placement sets the members its entity class marks [Authorable], and a document's own properties those of the
// scene class composing it. Every other field and property a key could name is kept with the reason it cannot be set.
internal static class PropertySchema
{

    /// <summary>Whether a member of <paramref name="type"/> names another entry: an entity class or any interface.</summary>
    internal static bool IsReference(ITypeSymbol type, Compilation compilation) =>
        type.TypeKind == TypeKind.Interface
        || (type is INamedTypeSymbol { TypeKind: TypeKind.Class } named
            && (named.ToDisplayString() == MetadataNames.Entity || SymbolShape.DerivesFrom(named, compilation, MetadataNames.Entity)));

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
    /// <param name="engineType">The metadata name of the engine class the walk stops at: the entity's or the scene's.</param>
    internal static EquatableArray<PropertyModel> Of(INamedTypeSymbol type, Compilation compilation, string engineType)
    {
        INamedTypeSymbol? engine = compilation.GetTypeByMetadataName(engineType);
        INamedTypeSymbol? authorable = compilation.GetTypeByMetadataName(MetadataNames.AuthorableAttribute);
        HashSet<string> hidden = new(StringComparer.Ordinal);
        List<PropertyModel> properties = [];
        for (INamedTypeSymbol? current = type;
            current is not null && !SymbolEqualityComparer.Default.Equals(current, engine);
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
        string engineType = SymbolShape.DerivesFrom(member.ContainingType, compilation, MetadataNames.Scene) ? MetadataNames.Scene : MetadataNames.Entity;
        PropertyModel model = Of(member.ContainingType, compilation, engineType).Items.FirstOrDefault(property => property.At == at);
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

        // An array is its element type written as a JSON array. Only the array itself may be null.
        string? elementRefusal = null;
        IArrayTypeSymbol? array = type as IArrayTypeSymbol;
        if (array is not null)
        {
            type = array.ElementType;
            if (array.Rank != 1 || type is IArrayTypeSymbol)
            {
                elementRefusal = $"has type '{declared.ToDisplayString()}'. A scene document carries a one-dimensional array of single values. Declare T[] of a type a single member accepts";
            }
            else if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
                || (type.IsReferenceType && array.ElementNullableAnnotation == NullableAnnotation.Annotated))
            {
                elementRefusal = $"has type '{declared.ToDisplayString()}', whose elements may be null. Make the elements non-nullable, or make the array itself nullable";
            }
        }

        (PropertyKind kind, string? converter, EquatableArray<(string, string)> names, string? unsupported) =
            Classify(type, array is null && nullable && type.IsValueType, compilation, (array is null ? type : declared).ToDisplayString());
        unsupported = elementRefusal ?? unsupported;
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
            array is not null,
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

    // A built-in form or an asset first, then an entity reference, then an enum, then a converter the type declares,
    // then the refusal of any other collection, then the type's own definitions.
    private static (PropertyKind Kind, string? Converter, EquatableArray<(string, string)> Names, string? Refusal) Classify(
        ITypeSymbol type,
        bool nullableValue,
        Compilation compilation,
        string display)
    {
        string unsupported = $"has type '{display}', which a scene document cannot carry. {PropertyForms.Supported}";
        string qualified = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (PropertyForms.BuiltIn(qualified) is not null)
        {
            return (PropertyKind.BuiltIn, null, default, null);
        }

        if (PropertyForms.Asset(qualified) is not null)
        {
            return (PropertyKind.Asset, null, default, null);
        }

        ITypeSymbol? element = type.SpecialType == SpecialType.System_String ? null : Enumerated(type);
        string collection = $"has type '{display}', which a scene document cannot carry. An array is the one collection a document writes. Declare '{element?.ToDisplayString()}[]'";

        // A collection interface .NET declares is a list to write as an array, and any other interface names an entity.
        if (IsReference(type, compilation) && !(element is not null && type.TypeKind == TypeKind.Interface && IsSystem(type)))
        {
            return (PropertyKind.Reference, null, default, null);
        }

        if (type is not INamedTypeSymbol named)
        {
            return (PropertyKind.Converted, null, default, unsupported);
        }

        if (named.TypeKind == TypeKind.Enum)
        {
            PropertyKind kind = SymbolShape.Attribute(named, compilation, MetadataNames.FlagsAttribute) is null ? PropertyKind.Named : PropertyKind.Flags;

            return (kind, null, Names(named, static field => field.HasConstantValue), null);
        }

        // A nullable value type wraps only the built-in forms above.
        if (nullableValue)
        {
            return (PropertyKind.Converted, null, default, unsupported);
        }

        if (SymbolShape.Attribute(named, compilation, MetadataNames.JsonConverterAttribute) is { } declared)
        {
            INamedTypeSymbol? converter = declared.ConstructorArguments.Length == 1 ? declared.ConstructorArguments[0].Value as INamedTypeSymbol : null;

            return converter is not null && IsConverterFor(converter, named, compilation)
                ? (PropertyKind.Converted, converter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), default, null)
                : (PropertyKind.Converted, null, default,
                    $"has type '{named.ToDisplayString()}', whose [JsonConverter] names '{converter?.ToDisplayString() ?? "no type"}'. Name a non-abstract JsonConverter<{named.ToDisplayString()}> this assembly can see, with a public parameterless constructor");
        }

        if (element is not null)
        {
            return (PropertyKind.Converted, null, default, collection);
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

    private static bool IsSystem(ITypeSymbol type)
    {
        string space = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        return space == "System" || space.StartsWith("System.", StringComparison.Ordinal);
    }

    // The element type of a collection other than an array, or null for a type that is none.
    private static ITypeSymbol? Enumerated(ITypeSymbol type) =>
        (type is INamedTypeSymbol named ? type.AllInterfaces.Prepend(named) : (IEnumerable<INamedTypeSymbol>)type.AllInterfaces)
            .FirstOrDefault(static candidate => candidate.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            ?.TypeArguments[0];

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

        INamedTypeSymbol? generic = compilation.GetTypeByMetadataName(MetadataNames.JsonConverter);
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
}
