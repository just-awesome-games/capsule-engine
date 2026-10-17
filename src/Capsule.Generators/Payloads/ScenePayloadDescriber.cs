using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Capsule.Generators;

// Reads one declared class as the scene payload registry sees it.
internal static class ScenePayloadDescriber
{
    internal static ScenePayloadModel? Describe(INamedTypeSymbol type, TypeDeclarationSyntax declaration, Compilation compilation)
    {
        // The generated registry constructs a payload the command line names. A payload that takes
        // arguments is left unregistered, which keeps a base payload out while its presets register.
        if (!SymbolShape.IsConcreteClass(type)
            || !SymbolShape.HasPublicParameterlessConstructor(type)
            || !SymbolShape.Implements(type, compilation, MetadataNames.ScenePayload))
        {
            return null;
        }

        return new ScenePayloadModel(
            SymbolShape.QualifiedName(type),
            type.ToDisplayString(),
            type.Name,
            SymbolShape.IsAccessibleFromGeneratedCode(type),
            DeclaredAt.From(declaration.Identifier.GetLocation()));
    }
}
