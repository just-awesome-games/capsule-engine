using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// The questions every describer asks of a declared type.
internal static class SymbolShape
{
    internal static string QualifiedName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>The type's namespace as written, or empty for the global namespace.</summary>
    internal static string NamespaceOf(INamedTypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false } containing ? containing.ToDisplayString() : string.Empty;

    internal static bool IsConcreteClass(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Class && !type.IsAbstract && !type.IsStatic && !type.IsGenericType;

    internal static bool DerivesFrom(INamedTypeSymbol type, Compilation compilation, string baseTypeName)
    {
        INamedTypeSymbol? baseType = compilation.GetTypeByMetadataName(baseTypeName);
        if (baseType is null)
        {
            return false;
        }

        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool Implements(INamedTypeSymbol type, Compilation compilation, string interfaceName)
    {
        INamedTypeSymbol? contract = compilation.GetTypeByMetadataName(interfaceName);
        if (contract is null)
        {
            return false;
        }

        foreach (INamedTypeSymbol implemented in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implemented, contract))
            {
                return true;
            }
        }

        return false;
    }

    // Non-private constructors taking one parameter of the named type. The generated call site passes an
    // lvalue, which binds to any of these ref kinds.
    internal static List<IMethodSymbol> ConstructorsTaking(INamedTypeSymbol type, Compilation compilation, string parameterTypeName)
    {
        INamedTypeSymbol? parameterType = compilation.GetTypeByMetadataName(parameterTypeName);

        return type.InstanceConstructors
            .Where(constructor => constructor.DeclaredAccessibility != Accessibility.Private
                && constructor.Parameters.Length == 1
                && constructor.Parameters[0].RefKind is RefKind.None or RefKind.In or RefKind.RefReadOnlyParameter
                && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, parameterType))
            .ToList();
    }

    internal static List<IMethodSymbol> Public(List<IMethodSymbol> constructors) =>
        constructors.Where(static constructor => constructor.DeclaredAccessibility == Accessibility.Public).ToList();

    /// <summary>How the generated call passes the parameter: empty, <c>in </c> or <c>ref readonly </c>.</summary>
    internal static string Modifier(RefKind kind) => kind switch
    {
        RefKind.In => "in ",
        RefKind.RefReadOnlyParameter => "ref readonly ",
        _ => string.Empty,
    };

    internal static bool HasPublicParameterlessConstructor(INamedTypeSymbol type) =>
        type.InstanceConstructors.Any(static constructor => constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public);

    // Generated code sits in the type's assembly but does not derive from it, so a protected constructor is out of reach.
    internal static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol type) =>
        type.InstanceConstructors.Any(static constructor => constructor.Parameters.Length == 0
            && constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal);

    internal static bool IsAccessibleFromGeneratedCode(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal
                || current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    internal static AttributeData? Attribute(INamedTypeSymbol type, Compilation compilation, string attributeTypeName)
    {
        INamedTypeSymbol? marker = compilation.GetTypeByMetadataName(attributeTypeName);
        if (marker is null)
        {
            return null;
        }

        foreach (AttributeData attribute in type.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker))
            {
                return attribute;
            }
        }

        return null;
    }
}
