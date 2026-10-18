using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// The classes a member's JSON object fills or constructs: each one described once, with the subclasses a type key
// names and how generated code constructs it.
internal static partial class PropertySchema
{
    // Every class the compilation declares in source, which a member's subclasses are found among.
    private static readonly ConditionalWeakTable<Compilation, List<INamedTypeSymbol>> SourceClasses = new();

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

    // Only code can satisfy C#'s required. A class carrying it is filled in place and never constructed.
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
}
