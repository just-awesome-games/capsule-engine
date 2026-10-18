using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// How one member a key could name is described: its type's form, and why a placement cannot set it.
internal static partial class PropertySchema
{
    // Null for a member without [Authorable] and without C#'s required, which no key names and nothing checks. An
    // override is the member it overrides: it carries an [Authorable] mark from anywhere along its chain, and is set
    // through the nearest declaration with a setter. A chain with no setter stays at this declaration.
    private static PropertyModel? Describe(ISymbol member, INamedTypeSymbol? authorable, Compilation compilation, Role role, Dictionary<string, ObjectModel>? objects)
    {
        AttributeData? mark = OverrideChain(member)
            .SelectMany(static overridden => overridden.GetAttributes())
            .FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, authorable));
        member = OverrideChain(member).FirstOrDefault(static overridden => overridden is not IPropertySymbol { SetMethod: null }) ?? member;
        bool keyword = member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true };
        if (mark is null && (member.IsStatic || !keyword))
        {
            return null;
        }

        (ITypeSymbol declared, NullableAnnotation annotation, bool direct, bool readable, bool held) = member switch
        {
            IPropertySymbol property => (
                property.Type,
                property.NullableAnnotation,
                property.SetMethod is { IsInitOnly: false } setter && compilation.IsSymbolAccessibleWithin(setter, compilation.Assembly),
                property.GetMethod is { } getter && compilation.IsSymbolAccessibleWithin(getter, compilation.Assembly),
                property.SetMethod is null),
            IFieldSymbol field => (
                field.Type,
                field.NullableAnnotation,
                compilation.IsSymbolAccessibleWithin(field, compilation.Assembly),
                compilation.IsSymbolAccessibleWithin(field, compilation.Assembly),
                field.IsReadOnly),
            _ => throw new ArgumentException("Only a field or property is described.", nameof(member)),
        };

        ITypeSymbol type = declared;
        bool nullable = type.IsReferenceType && annotation == NullableAnnotation.Annotated;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } wrapped)
        {
            type = wrapped.TypeArguments[0];
            nullable = true;
        }

        // An array is its element type written as a JSON array. Only the array itself may be null. A
        // ReadOnlyMemory<T> is written the same way, and every composition of one document shares its elements.
        string? elementRefusal = null;
        IArrayTypeSymbol? array = type as IArrayTypeSymbol;
        INamedTypeSymbol? memory = type is INamedTypeSymbol { IsGenericType: true } generic
            && SymbolEqualityComparer.Default.Equals(generic.OriginalDefinition, compilation.GetTypeByMetadataName(MetadataNames.ReadOnlyMemory))
                ? generic
                : null;
        if (memory is not null)
        {
            type = memory.TypeArguments[0];
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
                || (type.IsReferenceType && memory.TypeArgumentNullableAnnotations[0] == NullableAnnotation.Annotated))
            {
                elementRefusal = $"has type '{declared.ToDisplayString()}', whose elements may be null. Make the elements non-nullable, or make the memory itself nullable";
            }
        }
        else if (array is not null)
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

        bool many = array is not null || memory is not null;
        (PropertyKind kind, string? converter, EquatableArray<(string, string)> names, string? unsupported) =
            Classify(type, !many && nullable && type.IsValueType, compilation, (many ? declared : type).ToDisplayString());
        if (memory is not null && unsupported is null && kind is not (PropertyKind.BuiltIn or PropertyKind.Named or PropertyKind.Flags))
        {
            unsupported = $"has type '{declared.ToDisplayString()}'. Every scene composed from one document shares a ReadOnlyMemory member's elements, which must be a built-in type, an enum or a definition. Declare '{type.ToDisplayString()}[]'";
        }

        unsupported = elementRefusal ?? unsupported;
        bool reference = kind == PropertyKind.Reference;

        // An object member without a setter is filled in place. An array of objects is always built anew.
        bool fills = kind == PropertyKind.Object && array is null;
        bool marked = mark?.NamedArguments.Any(static named => named is { Key: "Required", Value.Value: true }) ?? false;
        string key = CamelCase(member is IFieldSymbol && member.Name.StartsWith("_", StringComparison.Ordinal) ? member.Name.Substring(1) : member.Name);
        string reserved = role switch
        {
            Role.Scene => MetadataNames.DocumentKeys,
            Role.Object => MetadataNames.MemberObjectKeys,
            _ => MetadataNames.EntryKeys,
        };
        string? refusal = mark is null
            ? null
            : Misuse(member, fills)
                ?? RoleMisuse(role, keyword, marked, reference)
                ?? unsupported
                ?? (Reserved(reserved, compilation).Contains(key) ? $"takes the key '{key}', which the scene document reserves for its own field there. Rename the member" : null);

        if (refusal is null && kind == PropertyKind.Object && objects is not null && type is INamedTypeSymbol objectType)
        {
            DescribeObject(objectType, compilation, objects);
        }

        List<INamedTypeSymbol> owners = [];
        for (INamedTypeSymbol? owner = member.ContainingType; owner is not null; owner = owner.ContainingType)
        {
            owners.Insert(0, owner);
        }

        return new PropertyModel(
            member.Name,
            key,
            mark is not null,
            marked || (keyword && reference),
            keyword,
            member is IFieldSymbol,
            direct,
            readable,
            held && fills,
            kind,
            member.ContainingType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            string.Join(", ", owners.SelectMany(static owner => owner.OriginalDefinition.TypeParameters).Select(static parameter => parameter.Name)),
            string.Join(", ", owners.SelectMany(static owner => owner.TypeArguments).Select(static argument => argument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))),
            string.Concat(owners.Select(static owner => Constraints(owner.OriginalDefinition))),
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            nullable,
            many,
            memory is not null,
            names,
            converter,
            refusal,
            null,
            DeclaredAt.From(member.Locations.FirstOrDefault() ?? Location.None));
    }

    // The keys a scene document reads itself at one level, which no authorable member there may take: the string
    // constants of the engine's SceneDocumentKeys class for that level.
    private static IEnumerable<string> Reserved(string keys, Compilation compilation) =>
        compilation.GetTypeByMetadataName(keys)?.GetMembers().OfType<IFieldSymbol>().Select(static field => field.ConstantValue).OfType<string>() ?? [];

    // The member, then each property it overrides in turn.
    private static IEnumerable<ISymbol> OverrideChain(ISymbol member)
    {
        for (ISymbol? current = member; current is not null; current = (current as IPropertySymbol)?.OverriddenProperty)
        {
            yield return current;
        }
    }

    // What makes an [Authorable] member one no placement can set, whatever its type. A member holding an object
    // needs no setter, since the document fills the object it holds.
    private static string? Misuse(ISymbol member, bool fills) => member switch
    {
        { IsStatic: true } => "is static. A placement sets one entity's member. Make it an instance member, or drop [Authorable]",
        IFieldSymbol { IsReadOnly: true } when !fills => "is readonly. Drop readonly, or drop [Authorable]",
        IPropertySymbol { SetMethod: null } when !fills => "has no setter. Add a set or init accessor of any access, or drop [Authorable]",
        _ => null,
    };

    // What the object holding the member refuses. An entry or the scene takes C#'s required only on an entity
    // reference, which the document sets past the compiler's check. Generated code constructs a nested object, and
    // cannot satisfy required there.
    private static string? RoleMisuse(Role role, bool keyword, bool marked, bool reference) => role switch
    {
        Role.Object => keyword
            ? "is required, which an object the document constructs cannot satisfy. Drop required and write [Authorable(Required = true)], on a nullable member for an entity reference"
            : null,
        _ => keyword && !reference ? "is required, which only an entity reference carries. Drop required and write [Authorable(Required = true)]"
            : reference && marked ? "is an entity reference, which Required = true does not mark. Drop Required = true and write C#'s required instead"
            : null,
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
    // then the refusal of any other collection, then a class of authorable members, then the type's own definitions.
    private static Classification Classify(
        ITypeSymbol type,
        bool nullableValue,
        Compilation compilation,
        string display)
    {
        string unsupported = $"has type '{display}', which a scene document cannot carry. {PropertyForms.Supported}";
        string qualified = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (PropertyForms.BuiltIn(qualified) is not null)
        {
            return new Classification(PropertyKind.BuiltIn, null, default, null);
        }

        if (PropertyForms.Asset(qualified) is not null)
        {
            return new Classification(PropertyKind.Asset, null, default, null);
        }

        ITypeSymbol? element = type.SpecialType == SpecialType.System_String ? null : Enumerated(type);
        string collection = $"has type '{display}', which a scene document cannot carry. An array is the one collection a document writes. Declare '{element?.ToDisplayString()}[]'";

        // A collection interface .NET declares is a list to write as an array, and any other interface names an entity.
        if (IsReference(type, compilation) && !(element is not null && type.TypeKind == TypeKind.Interface && IsSystem(type)))
        {
            return new Classification(PropertyKind.Reference, null, default, null);
        }

        if (type is not INamedTypeSymbol named)
        {
            return new Classification(PropertyKind.Converted, null, default, unsupported);
        }

        if (named.TypeKind == TypeKind.Enum)
        {
            PropertyKind kind = SymbolShape.Attribute(named, compilation, MetadataNames.FlagsAttribute) is null ? PropertyKind.Named : PropertyKind.Flags;

            return new Classification(kind, null, Names(named, static field => field.HasConstantValue), null);
        }

        // A nullable value type wraps only the built-in forms above.
        if (nullableValue)
        {
            return new Classification(PropertyKind.Converted, null, default, unsupported);
        }

        if (SymbolShape.Attribute(named, compilation, MetadataNames.JsonConverterAttribute) is { } declared)
        {
            INamedTypeSymbol? converter = declared.ConstructorArguments.Length == 1 ? declared.ConstructorArguments[0].Value as INamedTypeSymbol : null;

            return converter is not null && IsConverterFor(converter, named, compilation)
                ? new Classification(PropertyKind.Converted, converter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), default, null)
                : new Classification(PropertyKind.Converted, null, default,
                    $"has type '{named.ToDisplayString()}', whose [JsonConverter] names '{converter?.ToDisplayString() ?? "no type"}'. Name a non-abstract JsonConverter<{named.ToDisplayString()}> this assembly can see, with a public parameterless constructor");
        }

        if (element is not null)
        {
            return new Classification(PropertyKind.Converted, null, default, collection);
        }

        // A class that declares authorable members, or whose subclasses do, is written as a JSON object of them.
        if (named.TypeKind == TypeKind.Class && Authors(named, compilation))
        {
            return !named.IsGenericType && Named(named, compilation)
                ? new Classification(PropertyKind.Object, null, default, null)
                : new Classification(PropertyKind.Object, null, default,
                    $"has type '{display}', which generated code cannot name. Make the class non-generic and visible to this assembly");
        }

        // Every placement naming a definition shares one instance. Only a readonly struct or a record class
        // without settable members can declare them. What a member's own type allows is not policed.
        bool immutable = named is { TypeKind: TypeKind.Struct, IsReadOnly: true }
            || (named is { TypeKind: TypeKind.Class, IsRecord: true } && !HasSettableMembers(named));
        EquatableArray<(string, string)> definitions = Names(named, field =>
            field is { IsStatic: true, IsReadOnly: true, DeclaredAccessibility: Accessibility.Public }
            && SymbolEqualityComparer.Default.Equals(field.Type, named));

        return !immutable || definitions.Items.IsEmpty
            ? new Classification(PropertyKind.Converted, null, default, unsupported)
            : new Classification(PropertyKind.Named, null, definitions, null);
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

    // What a member's type is to a document: its form, the converter or names it reads through, and why a
    // placement cannot set it, or null.
    private readonly record struct Classification(PropertyKind Kind, string? Converter, EquatableArray<(string, string)> Names, string? Refusal);
}
