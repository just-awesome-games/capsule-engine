using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Capsule.Assets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

internal static class SceneRegistrySource
{
    private const string FileName = "CapsuleScenes.g.cs";

    // Scene keys drop this namespace segment, since it repeats the domain.
    private const string DomainSegment = "Scenes";

    internal static SceneModel? Describe(INamedTypeSymbol type, TypeDeclarationSyntax declaration, Compilation compilation)
    {
        Location location = declaration.Identifier.GetLocation();
        string qualifiedName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string displayName = type.ToDisplayString();
        bool concreteScene = Symbols.IsConcreteClass(type) && Symbols.DerivesFrom(type, compilation, Symbols.Scene);
        int contentConstructors = concreteScene
            ? Symbols.PublicConstructorsTaking(type, compilation, Symbols.SceneContent)
            : 0;
        bool parameterless = concreteScene && Symbols.HasPublicParameterlessConstructor(type);
        AttributeData? annotation = Symbols.Attribute(type, compilation, Symbols.SceneDocumentAttribute);

        string space = type.ContainingNamespace is { IsGlobalNamespace: false } containing
            ? containing.ToDisplayString()
            : string.Empty;

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
                return null;
            }

            string documentName = annotation.ConstructorArguments[0].Value as string ?? string.Empty;

            return Model(Accessibility(), documented: true, documentName);
        }

        if (!concreteScene || (contentConstructors == 0 && !parameterless))
        {
            return null;
        }

        if (contentConstructors > 1 || (contentConstructors == 1 && parameterless))
        {
            return Model(SceneFault.AmbiguousConstructors);
        }

        return Model(Accessibility(), documented: contentConstructors == 1);

        SceneFault Accessibility() =>
            Symbols.IsAccessibleFromGeneratedCode(type) ? SceneFault.None : SceneFault.InaccessibleType;

        SceneModel Model(SceneFault fault, bool documented = false, string? declared = null) =>
            new(qualifiedName, displayName, space, type.Name, documented, declared, fault, DeclaredAt.From(location));
    }

    internal static void Emit(
        SourceProductionContext context,
        ImmutableArray<SceneModel> models,
        bool enginePresent,
        string rootNamespace)
    {
        if (!enginePresent)
        {
            return;
        }

        List<Registration> sound = [];
        RegistryPass.Sound(
            context,
            models,
            static model => model.QualifiedName,
            static model => model.DisplayName,
            static model => model.At,
            static model => Reported(model.Fault),
            model => Resolve(context, sound, model, rootNamespace));

        // Scenes with no document sort first and never collide.
        List<Registration> registered = RegistryPass.Claimed(
            context,
            sound,
            static (left, right) =>
            {
                int byDocument = string.CompareOrdinal(left.DocumentName ?? string.Empty, right.DocumentName ?? string.Empty);

                return byDocument != 0 ? byDocument : string.CompareOrdinal(left.Model.QualifiedName, right.Model.QualifiedName);
            },
            static entry => entry.DocumentName,
            static entry => entry.Model.DisplayName,
            static entry => entry.Model.At,
            RegistryDiagnostics.DuplicateSceneDocumentName);

        context.AddSource(FileName, SourceText.From(Render(registered), Encoding.UTF8));
    }

    private static DiagnosticDescriptor? Reported(SceneFault fault) => fault switch
    {
        SceneFault.SceneDocumentRequiresContentConstructor => RegistryDiagnostics.SceneDocumentRequiresContentConstructor,
        SceneFault.InaccessibleType => RegistryDiagnostics.InaccessibleRegisteredType,
        SceneFault.AmbiguousConstructors => RegistryDiagnostics.AmbiguousSceneConstructors,
        _ => null,
    };

    // The key comes from where the type is declared, so it is not settled until the assembly's
    // root namespace is known.
    private static void Resolve(
        SourceProductionContext context,
        List<Registration> sound,
        SceneModel model,
        string rootNamespace)
    {
        if (!model.Documented)
        {
            sound.Add(new Registration(null, model));

            return;
        }

        // An explicit claim is normalized like the document's path, so any spelling of the claim
        // meets the document at the key it ships under.
        string? documentName = model.Declared is { } declared
            ? Normalized(context, model, declared)
            : TypeNaming.KeyFor(model.ContainingNamespace, model.TypeName, rootNamespace, DomainSegment);

        if (documentName is null)
        {
            return;
        }

        if (AssetPaths.IsKey(documentName))
        {
            sound.Add(new Registration(documentName, model));

            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RegistryDiagnostics.UnsafeSceneDocumentName, model.At.Location(), model.DisplayName, documentName));
    }

    private static string? Normalized(SourceProductionContext context, SceneModel model, string declared)
    {
        if (TypeNaming.NormalizeKey(declared, out string? rejected) is { } key)
        {
            return key;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RegistryDiagnostics.UnnameableSceneDocumentSegment, model.At.Location(), model.DisplayName, rejected));

        return null;
    }

    private static string Render(List<Registration> registered)
    {
        StringBuilder claims = new();
        StringBuilder registrations = new();

        foreach (Registration entry in registered)
        {
            registrations.Append("                ");

            if (entry.DocumentName is null)
            {
                registrations.Append("global::Capsule.Scenes.SceneRegistration.Plain(typeof(")
                    .Append(entry.Model.QualifiedName).Append("), static _ => new ")
                    .Append(entry.Model.QualifiedName).AppendLine("()),");
                continue;
            }

            claims.Append("[assembly: global::Capsule.Scenes.Generated.CapsuleGeneratedRegistryClaimAttribute(1, ")
                .Append(SymbolDisplay.FormatLiteral(entry.DocumentName, quote: true))
                .Append(", typeof(").Append(entry.Model.QualifiedName).AppendLine("))]");

            registrations.Append("global::Capsule.Scenes.SceneRegistration.FromDocument(typeof(")
                .Append(entry.Model.QualifiedName).Append("), ")
                .Append(SymbolDisplay.FormatLiteral(entry.DocumentName, quote: true))
                .Append(", static content => new ").Append(entry.Model.QualifiedName)
                .AppendLine("(content!.Value)),");
        }

        if (claims.Length > 0)
        {
            claims.AppendLine();
        }

        return $$"""
            // <auto-generated/>
            #nullable enable

            {{claims}}namespace Capsule.Scenes.Generated
            {
                /// <summary>Every scene this assembly declares. Generated code. Do not edit.</summary>
                [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
                public static class CapsuleScenes
                {
                    internal static global::Capsule.Scenes.SceneRegistration[] Registrations { get; } =
                        new global::Capsule.Scenes.SceneRegistration[]
                        {
            {{registrations}}            };

                    /// <summary>The registry the engine composes every scene through.</summary>
                    public static global::Capsule.Scenes.SceneRegistry Registry { get; } =
                        new global::Capsule.Scenes.SceneRegistry(
                            global::Capsule.Scenes.Generated.CapsuleEntities.Registry,
                            Registrations);
                }
            }

            """;
    }

    private readonly struct Registration(string? documentName, SceneModel model)
    {
        internal string? DocumentName { get; } = documentName;

        internal SceneModel Model { get; } = model;
    }
}
