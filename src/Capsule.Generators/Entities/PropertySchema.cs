using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// A placement sets the members its entity class marks [Authorable], the document's top-level keys those of the scene
// class composing it, and a member's JSON object those of the class it holds or constructs. Each [Authorable] member
// is kept with the reason it cannot be set, if any, and each other member only where it carries C#'s required.
internal static partial class PropertySchema
{
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
    /// could name, base classes first.
    /// </summary>
    /// <remarks>
    /// A member that a derived member the game can see hides is left out, as in C# member lookup. An override stands
    /// in for the member it overrides.
    /// </remarks>
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
}
