using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Capsule.Generators;

internal static class Symbols
{
    internal const string LogicRole = "build_property.CapsuleGameLogic";
    internal const string ShellRole = "build_property.CapsuleGameShell";
    internal const string RootNamespace = "build_property.RootNamespace";

    internal const string Entity = "Capsule.Scenes.Entity";
    internal const string EntitySpawn = "Capsule.Scenes.Spawning.EntitySpawn";
    internal const string SpawnTypeAttribute = "Capsule.Scenes.Spawning.SpawnTypeAttribute";
    internal const string Scene = "Capsule.Scenes.Scene";
    internal const string SceneContent = "Capsule.Scenes.SceneContent";
    internal const string SceneDocumentAttribute = "Capsule.Scenes.SceneDocumentAttribute";
    internal const string InputDriver = "Capsule.Input.IInputDriver";
    internal const string CapsuleEngine = "Capsule.Runtime.CapsuleEngine";
    internal const string RegistryProviderAttribute = "Capsule.Scenes.Generated.CapsuleGeneratedRegistryProviderAttribute";
    internal const string RegistryClaimAttribute = "Capsule.Scenes.Generated.CapsuleGeneratedRegistryClaimAttribute";
    internal const string TextureHandle = "Capsule.Assets.TextureHandle";

    // MSBuild passes a boolean property through verbatim, so compare it case-insensitively.
    internal static bool Declares(AnalyzerConfigOptions options, string key) =>
        options.TryGetValue(key, out string? value) && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    // Syntax only. This runs on every type declaration at every keystroke, and a semantic lookup
    // here would be paid each time.
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

            // The generated call site passes an lvalue, which binds to any of these ref kinds.
            bool passable = parameter.RefKind is RefKind.None or RefKind.In or RefKind.RefReadOnlyParameter;
            if (passable && SymbolEqualityComparer.Default.Equals(parameter.Type, parameterType))
            {
                count++;
            }
        }

        return count;
    }

    // The single public constructor taking an EntitySpawn, or null when there are none or several.
    internal static IMethodSymbol? SpawnConstructor(INamedTypeSymbol type, Compilation compilation)
    {
        INamedTypeSymbol? parameterType = compilation.GetTypeByMetadataName(EntitySpawn);
        IMethodSymbol? found = null;

        foreach (IMethodSymbol constructor in type.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility == Accessibility.Public
                && constructor.Parameters.Length == 1
                && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, parameterType))
            {
                if (found is not null)
                {
                    return null;
                }

                found = constructor;
            }
        }

        return found;
    }

    // Whether the constructor's initializer passes an EntitySpawn on: a base(...) or this(...)
    // argument of that type, the spawn itself or one rewritten with { }, or a primary constructor's
    // base argument list carrying one. A this(...) target is trusted, as is a constructor with no
    // syntax here. Otherwise at is the constructor that drops the spawn.
    /// <param name="declaring">The model the candidate arrived with, reused when it binds this tree.</param>
    internal static bool PassesSpawnOn(IMethodSymbol constructor, SemanticModel declaring, out Location? at)
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
            arguments = null;
            if (primary.BaseList is not null)
            {
                foreach (BaseTypeSyntax candidate in primary.BaseList.Types)
                {
                    if (candidate is PrimaryConstructorBaseTypeSyntax withArguments)
                    {
                        arguments = withArguments.ArgumentList;
                        break;
                    }
                }
            }
        }
        else
        {
            return true;
        }

        if (arguments is not null)
        {
            Compilation compilation = declaring.Compilation;
            INamedTypeSymbol? spawnType = compilation.GetTypeByMetadataName(EntitySpawn);

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
