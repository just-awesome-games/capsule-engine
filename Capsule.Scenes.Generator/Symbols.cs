using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Capsule.Scenes.Generator;

/// <summary>
/// The engine types the generators recognise, the roles they branch on, and the shape tests they
/// judge a class by.
/// </summary>
internal static class Symbols
{
    internal const string LogicRole = "build_property.CapsuleGameLogic";
    internal const string ShellRole = "build_property.CapsuleGameShell";

    internal const string Entity = "Capsule.Scenes.Entity";
    internal const string EntitySpawn = "Capsule.Scenes.Spawning.EntitySpawn";
    internal const string SpawnTypeAttribute = "Capsule.Scenes.Spawning.SpawnTypeAttribute";
    internal const string Scene = "Capsule.Scenes.Scene";
    internal const string MapSceneContext = "Capsule.Scenes.MapSceneContext";
    internal const string MapNameAttribute = "Capsule.Scenes.MapNameAttribute";
    internal const string CapsuleEngine = "Capsule.Runtime.CapsuleEngine";
    internal const string RegistryProviderAttribute = "Capsule.Scenes.Generated.CapsuleGeneratedRegistryProviderAttribute";
    internal const string RegistryClaimAttribute = "Capsule.Scenes.Generated.CapsuleGeneratedRegistryClaimAttribute";
    internal const string TextureHandle = "Capsule.Assets.TextureHandle";

    // MSBuild passes a boolean property through verbatim and compares it case-insensitively itself.
    internal static bool Declares(AnalyzerConfigOptions options, string key) =>
        options.TryGetValue(key, out string? value) && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    // Syntax only: this runs on every type declaration in the assembly at every keystroke, so a
    // semantic lookup here would be paid over and over. A registered class states its base type,
    // and an annotation is the other way a declaration asks to be judged rather than ignored.
    internal static bool MayBeRegistered(SyntaxNode node) =>
        node is TypeDeclarationSyntax declaration
        && (declaration.BaseList is not null || declaration.AttributeLists.Count > 0);

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

    internal static int PublicConstructorsTaking(INamedTypeSymbol type, Compilation compilation, string parameterTypeName)
    {
        INamedTypeSymbol? parameterType = compilation.GetTypeByMetadataName(parameterTypeName);
        if (parameterType is null)
        {
            return 0;
        }

        int count = 0;

        foreach (IMethodSymbol constructor in type.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public || constructor.Parameters.Length != 1)
            {
                continue;
            }

            IParameterSymbol parameter = constructor.Parameters[0];

            // Taken by value or by readonly reference: the generated call site passes an
            // lvalue, which binds to either.
            bool passable = parameter.RefKind is RefKind.None or RefKind.In or RefKind.RefReadOnlyParameter;
            if (passable && SymbolEqualityComparer.Default.Equals(parameter.Type, parameterType))
            {
                count++;
            }
        }

        return count;
    }

    internal static bool HasPublicParameterlessConstructor(INamedTypeSymbol type)
    {
        foreach (IMethodSymbol constructor in type.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility == Accessibility.Public && constructor.Parameters.Length == 0)
            {
                return true;
            }
        }

        return false;
    }

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
