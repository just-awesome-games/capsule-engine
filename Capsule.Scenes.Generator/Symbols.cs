using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Capsule.Scenes.Generator;

/// <summary>The engine types the generator recognises, and the shape tests it judges a class by.</summary>
internal static class Symbols
{
    internal const string Entity = "Capsule.Scenes.Entity";
    internal const string EntitySpawn = "Capsule.Scenes.Spawning.EntitySpawn";
    internal const string SpawnTypeAttribute = "Capsule.Scenes.Spawning.SpawnTypeAttribute";
    internal const string Scene = "Capsule.Scenes.Scene";
    internal const string MapSceneContext = "Capsule.Scenes.MapSceneContext";
    internal const string CapsuleEngine = "Capsule.Runtime.CapsuleEngine";

    /// <summary>The scene registry as the logic half emits it, and the shell reads it back.</summary>
    internal const string GameScenes = "Capsule.Scenes.Generated.GameScenes";

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

    internal static bool HasPublicConstructorTaking(INamedTypeSymbol type, Compilation compilation, string parameterTypeName)
    {
        INamedTypeSymbol? parameterType = compilation.GetTypeByMetadataName(parameterTypeName);
        if (parameterType is null)
        {
            return false;
        }

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
                return true;
            }
        }

        return false;
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
}
