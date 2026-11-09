using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Capsule.Generators;

// Reads one declared class as a named registry sees it.
internal static class NamedClassDescriber
{
    // The generated registry constructs the class a command line names with no arguments. A class that takes
    // arguments is left unregistered.
    internal static NamedClassModel? Describe(INamedTypeSymbol type, TypeDeclarationSyntax declaration, Compilation compilation, NamedRegistry registry)
    {
        if (!SymbolShape.IsConcreteClass(type)
            || !SymbolShape.HasPublicParameterlessConstructor(type)
            || !SymbolShape.Implements(type, compilation, registry.Interface))
        {
            return null;
        }

        return new NamedClassModel(
            SymbolShape.QualifiedName(type),
            type.ToDisplayString(),
            type.Name,
            SymbolShape.IsAccessibleFromGeneratedCode(type),
            DeclaredAt.From(declaration.Identifier.GetLocation()));
    }
}
