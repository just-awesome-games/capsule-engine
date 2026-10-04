using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// A placement sets the members its entity class marks [Authorable], the document's top-level keys those of the scene
// class composing it, and a member's JSON object those of the class it holds or constructs. Each [Authorable] member is kept with the reason it cannot be set, if any, and each other
// member only where it carries C#'s required.
internal static class PropertySchema
{
    // Every class the compilation declares in source, which a member's subclasses are found among.
    private static readonly ConditionalWeakTable<Compilation, List<INamedTypeSymbol>> SourceClasses = new();

    // What a document object holding the member is: an entry, the scene, or an object a member holds.
    private enum Role
    {
        Entity,
        Scene,
        Object,
    }

    /// <summary>Whether a member of <paramref name="type"/> names another entry: an entity class or any interface.</summary>
    internal static bool IsReference(ITypeSymbol type, Compilation compilation) =>
        type.TypeKind == TypeKind.Interface
        || (type is INamedTypeSymbol { TypeKind: TypeKind.Class } named
            && (SymbolEqualityComparer.Default.Equals(named, compilation.GetTypeByMetadataName(MetadataNames.Entity)) || SymbolShape.DerivesFrom(named, compilation, MetadataNames.Entity)));

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
    /// Every field and property of <paramref name="type"/> and all its base classes, engine ones included, that a key
    /// could name, base classes first. A member that a derived member the game can see hides is left out, as in C#
    /// member lookup. An override stands in for the member it overrides.
    /// </summary>
    internal static EquatableArray<PropertyModel> Of(INamedTypeSymbol type, Compilation compilation) =>
        Of(type, compilation, null);

    /// <summary>
    /// What <see cref="Of(INamedTypeSymbol, Compilation)"/> finds, with every class a member's JSON object fills or
    /// constructs at any depth, each subclass a type key can name among them.
    /// </summary>
    internal static (EquatableArray<PropertyModel> Properties, EquatableArray<ObjectModel> Objects) WithObjects(INamedTypeSymbol type, Compilation compilation)
    {
        Dictionary<string, ObjectModel> objects = new(StringComparer.Ordinal);
        EquatableArray<PropertyModel> properties = Of(type, compilation, objects);

        return (properties, new([.. objects.Values.OrderBy(static model => model.QualifiedName, StringComparer.Ordinal)]));
    }

    // With objects, each object class a member reaches is described into it as well.
    private static EquatableArray<PropertyModel> Of(INamedTypeSymbol type, Compilation compilation, Dictionary<string, ObjectModel>? objects)
    {
        INamedTypeSymbol? authorable = compilation.GetTypeByMetadataName(MetadataNames.AuthorableAttribute);
        HashSet<string> hidden = new(StringComparer.Ordinal);
        List<PropertyModel> properties = [];
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            ImmutableArray<ISymbol> members = current.GetMembers();
            Role role = RoleOf(current, compilation);
            List<PropertyModel> declared = members
                .Where(static member => member is IPropertySymbol { IsIndexer: false } or IFieldSymbol && member.CanBeReferencedByName)
                .Where(member => !hidden.Contains(member.Name))
                .Select(member => Describe(member, authorable, compilation, role, objects))
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

    private static Role RoleOf(INamedTypeSymbol type, Compilation compilation) =>
        IsOrDerives(type, compilation, MetadataNames.Scene) ? Role.Scene
        : IsOrDerives(type, compilation, MetadataNames.Entity) ? Role.Entity
        : Role.Object;

    private static bool IsOrDerives(INamedTypeSymbol type, Compilation compilation, string engineType) =>
        SymbolEqualityComparer.Default.Equals(type, compilation.GetTypeByMetadataName(engineType)) || SymbolShape.DerivesFrom(type, compilation, engineType);

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
            array is not null,
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

    // Describes the class and every subclass a type key can name, each once. The placeholder ends a cycle of
    // classes holding one another.
    private static void DescribeObject(INamedTypeSymbol type, Compilation compilation, Dictionary<string, ObjectModel> objects)
    {
        string name = SymbolShape.QualifiedName(type);
        if (objects.ContainsKey(name))
        {
            return;
        }

        objects[name] = default;
        List<INamedTypeSymbol> subclasses = Subclasses(type, compilation);
        objects[name] = new ObjectModel(
            name,
            type.ToDisplayString(),
            SymbolShape.NamespaceOf(type),
            type.Name,
            SymbolShape.Attribute(type, compilation, MetadataNames.TypeKeyAttribute) is { ConstructorArguments.Length: 1 } annotation
                ? annotation.ConstructorArguments[0].Value as string ?? string.Empty
                : null,
            DeclaredAt.From(type.Locations.FirstOrDefault() ?? Location.None),
            Construction(type, compilation),
            Of(type, compilation, objects),
            new([.. subclasses.Select(SymbolShape.QualifiedName)]));

        foreach (INamedTypeSymbol subclass in subclasses)
        {
            DescribeObject(subclass, compilation, objects);
        }
    }

    // Every class the game declares deriving from type that generated code can name, deepest first.
    private static List<INamedTypeSymbol> Subclasses(INamedTypeSymbol type, Compilation compilation) =>
        [.. SourceClasses.GetValue(compilation, static declaring => [.. Declared(declaring.Assembly.GlobalNamespace)])
            .Where(candidate => !candidate.IsGenericType && Named(candidate, compilation) && Depth(candidate, type) > 0)
            .OrderByDescending(candidate => Depth(candidate, type))
            .ThenBy(static candidate => SymbolShape.QualifiedName(candidate), StringComparer.Ordinal)];

    private static IEnumerable<INamedTypeSymbol> Declared(INamespaceOrTypeSymbol container) =>
        container.GetMembers().SelectMany(static member => member switch
        {
            INamespaceSymbol space => Declared(space),
            INamedTypeSymbol { TypeKind: TypeKind.Class } type => Declared(type).Prepend(type),
            INamedTypeSymbol type => Declared(type),
            _ => [],
        });

    // How many classes down from ancestor the type is, or 0 when it does not derive from it.
    private static int Depth(INamedTypeSymbol type, INamedTypeSymbol ancestor)
    {
        int depth = 1;
        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType, depth++)
        {
            if (SymbolEqualityComparer.Default.Equals(current, ancestor))
            {
                return depth;
            }
        }

        return 0;
    }

    // Only code can satisfy C#'s required, so a class carrying it is filled in place and never constructed.
    private static ObjectConstruction Construction(INamedTypeSymbol type, Compilation compilation)
    {
        IMethodSymbol? constructor = type.InstanceConstructors.FirstOrDefault(static constructor => constructor.Parameters.Length == 0);
        bool required = false;
        for (INamedTypeSymbol? current = type; current is not null && !required; current = current.BaseType)
        {
            required = current.GetMembers().Any(static member => member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true });
        }

        return type.IsAbstract || type.IsStatic || constructor is null || required ? ObjectConstruction.None
            : compilation.IsSymbolAccessibleWithin(constructor, compilation.Assembly) ? ObjectConstruction.New
            : ObjectConstruction.Accessor;
    }

    // Whether generated code can name the class.
    private static bool Named(INamedTypeSymbol type, Compilation compilation) =>
        !type.IsFileLocal && compilation.IsSymbolAccessibleWithin(type, compilation.Assembly);

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

        // A class that declares authorable members, or whose subclasses do, is written as a JSON object of them.
        if (named.TypeKind == TypeKind.Class && Authors(named, compilation))
        {
            return !named.IsGenericType && Named(named, compilation)
                ? (PropertyKind.Object, null, default, null)
                : (PropertyKind.Object, null, default,
                    $"has type '{display}', which generated code cannot name. Make the class non-generic and visible to this assembly");
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

    // Whether the class or a base declares an [Authorable] member, or a subclass the game declares does.
    private static bool Authors(INamedTypeSymbol type, Compilation compilation)
    {
        INamedTypeSymbol? authorable = compilation.GetTypeByMetadataName(MetadataNames.AuthorableAttribute);

        bool Declares(INamedTypeSymbol declaring)
        {
            for (INamedTypeSymbol? current = declaring; current is not null; current = current.BaseType)
            {
                if (current.GetMembers().Any(member => member.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, authorable))))
                {
                    return true;
                }
            }

            return false;
        }

        return Declares(type) || Subclasses(type, compilation).Any(Declares);
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
