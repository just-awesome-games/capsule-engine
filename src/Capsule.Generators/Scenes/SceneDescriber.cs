using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Capsule.Generators;

// Reads the four things a scene document resolves against: every Scene class, every Camera class, every
// TileType class, and every document the build shipped.
internal static class SceneDescriber
{
    // A document's baseScene and camera are resolved against every Scene and Camera subclass the
    // assembly declares, so every one is modeled here whether or not a document ever names it.
    internal static SceneModel? Describe(INamedTypeSymbol type, TypeDeclarationSyntax declaration, Compilation compilation)
    {
        if (!SymbolShape.DerivesFrom(type, compilation, MetadataNames.Scene))
        {
            return null;
        }

        bool concreteScene = SymbolShape.IsConcreteClass(type);

        // A derived type in this assembly can call any of these. A baseScene needs exactly one.
        List<IMethodSymbol> derivable = SymbolShape.ConstructorsTaking(type, compilation, MetadataNames.SceneContent);
        List<IMethodSymbol> constructors = concreteScene ? SymbolShape.Public(derivable) : [];
        int contentConstructors = constructors.Count;
        string contentModifier = contentConstructors == 1 ? SymbolShape.Modifier(constructors[0].Parameters[0].RefKind) : string.Empty;
        bool parameterless = concreteScene && SymbolShape.HasPublicParameterlessConstructor(type);
        AttributeData? annotation = SymbolShape.Attribute(type, compilation, MetadataNames.SceneDocumentAttribute);
        bool accessible = SymbolShape.IsAccessibleFromGeneratedCode(type);
        EquatableArray<PropertyModel> properties = PropertySchema.Of(type, compilation, MetadataNames.Scene);

        if (annotation is not null)
        {
            if (!concreteScene || contentConstructors == 0)
            {
                return Model(SceneFault.SceneDocumentRequiresContentConstructor);
            }

            if (contentConstructors > 1 || parameterless)
            {
                return Model(SceneFault.AmbiguousConstructors);
            }

            if (annotation.ConstructorArguments.Length != 1)
            {
                return Model(SceneFault.None, registrable: false);
            }

            string documentName = annotation.ConstructorArguments[0].Value as string ?? string.Empty;

            return Model(Accessibility(), documented: true, documentName);
        }

        if (!concreteScene || (contentConstructors == 0 && !parameterless))
        {
            return Model(SceneFault.None, registrable: false);
        }

        if (contentConstructors > 1 || (contentConstructors == 1 && parameterless))
        {
            return Model(SceneFault.AmbiguousConstructors);
        }

        return Model(Accessibility(), documented: contentConstructors == 1);

        SceneFault Accessibility() => accessible ? SceneFault.None : SceneFault.InaccessibleType;

        SceneModel Model(SceneFault fault, bool documented = false, string? declared = null, bool registrable = true) =>
            new(
                SymbolShape.QualifiedName(type), type.ToDisplayString(), SymbolShape.NamespaceOf(type), type.Name,
                documented, declared, fault, registrable, type.IsAbstract, type.IsGenericType, derivable.Count, accessible,
                DeclaredAt.From(declaration.Identifier.GetLocation()), properties, contentModifier);
    }

    // Every Camera subclass is modeled, valid or not. The resolver then tells a document naming an unusable
    // class from one naming a key no class claims.
    internal static CameraModel? DescribeCamera(INamedTypeSymbol type, TypeDeclarationSyntax declaration, Compilation compilation)
    {
        if (!SymbolShape.DerivesFrom(type, compilation, MetadataNames.Camera))
        {
            return null;
        }

        return new CameraModel(
            SymbolShape.QualifiedName(type),
            type.ToDisplayString(),
            SymbolShape.NamespaceOf(type),
            type.Name,
            SymbolShape.IsConcreteClass(type),
            SymbolShape.IsAccessibleFromGeneratedCode(type) && SymbolShape.HasAccessibleParameterlessConstructor(type),
            DeclaredAt.From(declaration.Identifier.GetLocation()));
    }

    // Every TileType subclass is modeled, valid or not, as a camera is.
    internal static TileTypeModel? DescribeTileType(INamedTypeSymbol type, TypeDeclarationSyntax declaration, Compilation compilation)
    {
        if (!SymbolShape.DerivesFrom(type, compilation, MetadataNames.TileType))
        {
            return null;
        }

        EquatableArray<PropertyModel> properties = PropertySchema.Of(type, compilation, MetadataNames.TileType);
        string required = string.Join(", ", properties.Items
            .Where(static property => property.RequiredKeyword && !property.Authorable)
            .Select(static property => property.Name));
        string? fault =
            !SymbolShape.IsConcreteClass(type) ? "is not a concrete class. Make it non-abstract, non-static and non-generic"
            : !SymbolShape.IsAccessibleFromGeneratedCode(type) || !SymbolShape.HasAccessibleParameterlessConstructor(type)
                ? "has no parameterless constructor generated code can call. Make the class and a parameterless constructor public or internal"
            : required.Length > 0 ? $"has the C# required members {required}, which a palette entry cannot set. Drop required and give each a default"
            : null;

        return new TileTypeModel(
            SymbolShape.QualifiedName(type),
            type.ToDisplayString(),
            SymbolShape.NamespaceOf(type),
            type.Name,
            fault,
            DeclaredAt.From(declaration.Identifier.GetLocation()),
            properties);
    }

    /// <summary>
    /// The document a key member the build marked describes, with the baseScene, camera and own properties the
    /// build's own parser read out of it. Null for a member whose mark names no key.
    /// </summary>
    internal static SceneDocumentModel? DescribeDocument(GeneratorAttributeSyntaxContext marked)
    {
        if (marked.TargetSymbol is not IPropertySymbol member)
        {
            return null;
        }

        string? key = null;
        string? baseScene = null;
        string? camera = null;
        string? source = null;
        string? path = null;
        EquatableArray<(string, object?)> properties = default;
        foreach (KeyValuePair<string, TypedConstant> named in marked.Attributes[0].NamedArguments)
        {
            switch (named.Key)
            {
                case "Key":
                    key = named.Value.Value as string;
                    break;
                case "BaseScene":
                    baseScene = named.Value.Value as string;
                    break;
                case "Camera":
                    camera = named.Value.Value as string;
                    break;
                case "Source":
                    source = named.Value.Value as string;
                    break;
                case "Path":
                    path = named.Value.Value as string;
                    break;
                case "Properties" when named.Value.Kind == TypedConstantKind.Array && !named.Value.IsNull:
                    properties = Pairs(named.Value.Values);
                    break;
            }
        }

        if (key is null)
        {
            return null;
        }

        ImmutableArray<PlacementModel>.Builder placements = ImmutableArray.CreateBuilder<PlacementModel>();
        ImmutableArray<PaletteEntryModel>.Builder palette = ImmutableArray.CreateBuilder<PaletteEntryModel>();
        foreach (AttributeData attribute in marked.TargetSymbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == MetadataNames.PlacementAttributeName && DescribePlacement(attribute) is { } placement)
            {
                placements.Add(placement);
            }
            else if (attribute.AttributeClass?.Name == MetadataNames.TileTypeAttributeName && DescribePaletteEntry(attribute) is { } entry)
            {
                palette.Add(entry);
            }
        }

        string qualified = SymbolShape.QualifiedName(member.ContainingType) + "." + member.Name;

        return new SceneDocumentModel(
            key, qualified, baseScene, camera, source, path, new(placements.ToImmutable()), properties, new(palette.ToImmutable()));
    }

    // One game entry, as the build's placement attribute carries it: id, type, then key and value pairs.
    private static PlacementModel? DescribePlacement(AttributeData attribute)
    {
        ImmutableArray<TypedConstant> arguments = attribute.ConstructorArguments;
        if (arguments.Length != 3 || arguments[0].Value is not int id || arguments[1].Value is not string type || arguments[2].Kind != TypedConstantKind.Array)
        {
            return null;
        }

        (int line, int column) = Start(attribute);

        return new PlacementModel(id, type, Pairs(arguments[2].Values), line, column);
    }

    // One palette entry, as the build's tile type attribute carries it: the tile map's id, the entry's name and
    // type, then key and value pairs.
    private static PaletteEntryModel? DescribePaletteEntry(AttributeData attribute)
    {
        ImmutableArray<TypedConstant> arguments = attribute.ConstructorArguments;
        if (arguments.Length != 4 || arguments[0].Value is not int id || arguments[1].Value is not string name || arguments[3].Kind != TypedConstantKind.Array)
        {
            return null;
        }

        (int line, int column) = Start(attribute);

        return new PaletteEntryModel(id, name, arguments[2].Value as string, Pairs(arguments[3].Values), line, column);
    }

    // Where an entry starts in the document's file, or (0, 0) when the build could not place it.
    private static (int Line, int Column) Start(AttributeData attribute)
    {
        int line = 0;
        int column = 0;
        foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
        {
            switch (named.Key)
            {
                case "Line":
                    line = named.Value.Value as int? ?? 0;
                    break;
                case "Column":
                    column = named.Value.Value as int? ?? 0;
                    break;
            }
        }

        return (line, column);
    }

    // Each property's name and value in turn, as the build writes a placement's or a document's properties.
    private static EquatableArray<(string, object?)> Pairs(ImmutableArray<TypedConstant> pairs)
    {
        ImmutableArray<(string, object?)>.Builder properties = ImmutableArray.CreateBuilder<(string, object?)>();
        for (int i = 0; i + 1 < pairs.Length; i += 2)
        {
            properties.Add(((string)pairs[i].Value!, PlacementValue(pairs[i + 1])));
        }

        return new(properties.ToImmutable());
    }

    private static object? PlacementValue(TypedConstant constant) => constant.Kind switch
    {
        TypedConstantKind.Array => new EquatableArray<object?>(constant.Values.Select(PlacementValue).ToImmutableArray()),
        TypedConstantKind.Type => PlacementModel.JsonObject,
        _ => constant.Value,
    };
}
