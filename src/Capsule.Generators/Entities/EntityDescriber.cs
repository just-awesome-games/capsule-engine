using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Capsule.Generators;

// Reads one declared class as the entity registry sees it: the key it claims and the fault it carries.
internal static class EntityDescriber
{
    internal static EntityModel? Describe(INamedTypeSymbol type, TypeDeclarationSyntax declaration, SemanticModel model)
    {
        Compilation compilation = model.Compilation;
        bool concreteEntity = SymbolShape.IsConcreteClass(type) && SymbolShape.DerivesFrom(type, compilation, MetadataNames.Entity);
        List<IMethodSymbol> constructors = concreteEntity
            ? SymbolShape.Public(SymbolShape.ConstructorsTaking(type, compilation, MetadataNames.EntitySpawn))
            : [];
        AttributeData? annotation = SymbolShape.Attribute(type, compilation, MetadataNames.SpawnTypeAttribute);

        if (annotation is null)
        {
            // A class of the wrong shape that claims nothing is an ordinary class, not a mistake.
            if (constructors.Count == 0)
            {
                return null;
            }

            EntityFault discoveredFault = constructors.Count > 1
                ? EntityFault.AmbiguousSpawnConstructors
                : SymbolShape.IsAccessibleFromGeneratedCode(type)
                    ? EntityFault.None
                    : EntityFault.InaccessibleType;

            return SpawnChecked(type, declaration, null, discoveredFault, model, constructors);
        }

        // The attribute has a single form. Any other call is the compiler's error to report.
        if (annotation.ConstructorArguments.Length != 1)
        {
            return null;
        }

        string? spawnType = annotation.ConstructorArguments[0].Value as string;
        if (string.IsNullOrWhiteSpace(spawnType))
        {
            return Model(type, declaration, null, EntityFault.BlankSpawnType);
        }

        EntityFault fault = !concreteEntity ? EntityFault.NotAConcreteEntity
            : constructors.Count == 0 ? EntityFault.MissingSpawnConstructor
            : constructors.Count > 1 ? EntityFault.AmbiguousSpawnConstructors
            : !SymbolShape.IsAccessibleFromGeneratedCode(type) ? EntityFault.InaccessibleType
            : EntityFault.None;

        return SpawnChecked(type, declaration, spawnType!, fault, model, constructors);
    }

    // An engine entity a document places by the key its [SpawnType] names. Its members are described as a game class's
    // are, and it carries no fault.
    internal static EntityModel? DescribeEngine(Compilation compilation, string metadataName)
    {
        if (compilation.GetTypeByMetadataName(metadataName) is not { } type
            || SymbolShape.Attribute(type, compilation, MetadataNames.SpawnTypeAttribute) is not { ConstructorArguments.Length: 1 } annotation
            || annotation.ConstructorArguments[0].Value is not string key)
        {
            return null;
        }

        (EquatableArray<PropertyModel> properties, EquatableArray<ObjectModel> objects) = PropertySchema.WithObjects(type, compilation);

        return new EntityModel(
            SymbolShape.QualifiedName(type),
            type.ToDisplayString(),
            SymbolShape.NamespaceOf(type),
            type.Name,
            key,
            EntityFault.None,
            DeclaredAt.From(Location.None),
            properties,
            objects,
            PropertySchema.AssignableTo(type),
            string.Empty);
    }

    // A sound claim must also pass its spawn to the base constructor, or the authored band and
    // factor the spawn carries never reach the entity. The fault is reported at the constructor.
    private static EntityModel SpawnChecked(
        INamedTypeSymbol type,
        TypeDeclarationSyntax declaration,
        string? declared,
        EntityFault fault,
        SemanticModel model,
        List<IMethodSymbol> constructors)
    {
        if (fault != EntityFault.None)
        {
            return Model(type, declaration, declared, fault);
        }

        return PassesSpawnOn(constructors[0], model, out Location? at)
            ? Model(type, declaration, declared, fault, authoring: PropertySchema.WithObjects(type, model.Compilation), spawn: constructors[0].Parameters[0].RefKind)
            : Model(type, declaration, declared, EntityFault.SpawnNotPassedToBase, at);
    }

    // Whether the constructor's initializer passes an EntitySpawn on: a base(...) or this(...)
    // argument of that type, the spawn itself or one rewritten with { }, or a primary constructor's
    // base argument list carrying one. A this(...) target is trusted, as is a constructor with no
    // syntax here. Otherwise at is the constructor that drops the spawn.
    private static bool PassesSpawnOn(IMethodSymbol constructor, SemanticModel declaring, out Location? at)
    {
        at = null;
        if (constructor.DeclaringSyntaxReferences.Length == 0)
        {
            return true;
        }

        SyntaxNode syntax = constructor.DeclaringSyntaxReferences[0].GetSyntax();
        ArgumentListSyntax? arguments;
        Location location;

        if (syntax is ConstructorDeclarationSyntax declared)
        {
            location = declared.Identifier.GetLocation();
            arguments = declared.Initializer?.ArgumentList;
        }
        else if (syntax is TypeDeclarationSyntax primary)
        {
            location = primary.ParameterList?.GetLocation() ?? primary.Identifier.GetLocation();
            arguments = primary.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>().FirstOrDefault()?.ArgumentList;
        }
        else
        {
            return true;
        }

        if (arguments is not null)
        {
            Compilation compilation = declaring.Compilation;
            INamedTypeSymbol? spawnType = compilation.GetTypeByMetadataName(MetadataNames.EntitySpawn);

            // Binding a fresh model per candidate would cost a game its edit loop, so reuse the
            // candidate's model unless the constructor is declared in another tree.
            SemanticModel model = declaring.SyntaxTree == syntax.SyntaxTree
                ? declaring
                : compilation.GetSemanticModel(syntax.SyntaxTree);
            foreach (ArgumentSyntax argument in arguments.Arguments)
            {
                if (SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(argument.Expression).Type, spawnType))
                {
                    return true;
                }
            }
        }

        at = location;
        return false;
    }

    private static EntityModel Model(
        INamedTypeSymbol type,
        TypeDeclarationSyntax declaration,
        string? declared,
        EntityFault fault,
        Location? at = null,
        (EquatableArray<PropertyModel> Properties, EquatableArray<ObjectModel> Objects) authoring = default,
        RefKind spawn = RefKind.None) =>
        new(
            SymbolShape.QualifiedName(type),
            type.ToDisplayString(),
            SymbolShape.NamespaceOf(type),
            type.Name,
            declared,
            fault,
            DeclaredAt.From(at ?? declaration.Identifier.GetLocation()),
            authoring.Properties,
            authoring.Objects,
            PropertySchema.AssignableTo(type),
            SymbolShape.Modifier(spawn));
}
