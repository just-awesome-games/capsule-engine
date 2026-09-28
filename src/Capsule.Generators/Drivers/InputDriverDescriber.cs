using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Capsule.Generators;

// Reads one declared class as the input driver registry sees it.
internal static class InputDriverDescriber
{
    internal static InputDriverModel? Describe(INamedTypeSymbol type, TypeDeclarationSyntax declaration, Compilation compilation)
    {
        // The generated registry constructs a driver the command line names. A driver that takes
        // arguments is left unregistered and reaches a run through WithInputDriver.
        if (!SymbolShape.IsConcreteClass(type)
            || !SymbolShape.HasPublicParameterlessConstructor(type)
            || !SymbolShape.Implements(type, compilation, MetadataNames.InputDriver))
        {
            return null;
        }

        return new InputDriverModel(
            SymbolShape.QualifiedName(type),
            type.ToDisplayString(),
            type.Name,
            SymbolShape.IsAccessibleFromGeneratedCode(type),
            DeclaredAt.From(declaration.Identifier.GetLocation()));
    }
}
