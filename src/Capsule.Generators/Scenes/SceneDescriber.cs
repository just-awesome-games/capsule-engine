using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Capsule.Generators;

// Reads the two things a scene document resolves against: every Scene class, and every document the build shipped.
internal static class SceneDescriber
{
    // A document's baseScene is resolved against every Scene subclass the assembly declares, so every one is
    // modeled here whether or not a document ever names it.
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
        (EquatableArray<PropertyModel> properties, EquatableArray<ObjectModel> objects) = PropertySchema.WithObjects(type, compilation);

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
                DeclaredAt.From(declaration.Identifier.GetLocation()), properties, objects, contentModifier);
    }

    // The engine's plain Scene, which a document naming no class and no baseScene composes. Its members are the
    // engine's own, which every scene class inherits.
    internal static SceneModel? DescribeEngineScene(Compilation compilation)
    {
        if (compilation.GetTypeByMetadataName(MetadataNames.Scene) is not { } scene)
        {
            return null;
        }

        (EquatableArray<PropertyModel> properties, EquatableArray<ObjectModel> objects) = PropertySchema.WithObjects(scene, compilation);

        return new SceneModel(
            SymbolShape.QualifiedName(scene), scene.ToDisplayString(), SymbolShape.NamespaceOf(scene), scene.Name,
            Documented: false, Declared: null, SceneFault.None, Registrable: false, Abstract: false, Generic: false,
            DerivableContentConstructors: 1, AccessibleType: true, DeclaredAt.From(Location.None), properties, objects, string.Empty);
    }

    /// <summary>
    /// The document a key member the build marked describes, with the baseScene the build's own parser read out of
    /// it. Null for a member whose mark names no key.
    /// </summary>
    internal static SceneDocumentModel? DescribeDocument(GeneratorAttributeSyntaxContext marked)
    {
        if (marked.TargetSymbol is not IPropertySymbol member)
        {
            return null;
        }

        string? key = null;
        string? baseScene = null;
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
            }
        }

        return key is null
            ? null
            : new SceneDocumentModel(key, SymbolShape.QualifiedName(member.ContainingType) + "." + member.Name, baseScene);
    }
}
