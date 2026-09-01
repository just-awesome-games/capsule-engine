using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

internal static class SceneRegistrySource
{
    private const string FileName = "CapsuleGameScenes.g.cs";

    internal static SceneModel? Describe(GeneratorSyntaxContext context, CancellationToken cancellation)
    {
        TypeDeclarationSyntax declaration = (TypeDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration, cancellation) is not INamedTypeSymbol type)
        {
            return null;
        }

        Compilation compilation = context.SemanticModel.Compilation;
        Location location = declaration.Identifier.GetLocation();
        string qualifiedName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string displayName = type.ToDisplayString();
        bool concreteScene = Symbols.IsConcreteClass(type) && Symbols.DerivesFrom(type, compilation, Symbols.Scene);
        int contentConstructors = concreteScene
            ? Symbols.PublicConstructorsTaking(type, compilation, Symbols.SceneContent)
            : 0;
        bool parameterless = concreteScene && Symbols.HasPublicParameterlessConstructor(type);
        AttributeData? annotation = Symbols.Attribute(type, compilation, Symbols.SceneDocumentAttribute);

        if (annotation is not null)
        {
            if (!concreteScene || contentConstructors == 0)
            {
                return new SceneModel(qualifiedName, displayName, null, SceneFault.SceneDocumentRequiresContentConstructor, location);
            }

            if (contentConstructors > 1 || parameterless)
            {
                return new SceneModel(qualifiedName, displayName, null, SceneFault.AmbiguousConstructors, location);
            }

            if (annotation.ConstructorArguments.Length != 1)
            {
                return null;
            }

            string documentName = annotation.ConstructorArguments[0].Value as string ?? string.Empty;
            SceneFault fault = !TypeNaming.IsSafeDocumentName(documentName)
                ? SceneFault.UnsafeDocumentName
                : Symbols.IsAccessibleFromGeneratedCode(type)
                    ? SceneFault.None
                    : SceneFault.InaccessibleType;

            return new SceneModel(qualifiedName, displayName, documentName, fault, location);
        }

        if (!concreteScene || (contentConstructors == 0 && !parameterless))
        {
            return null;
        }

        if (contentConstructors > 1 || (contentConstructors == 1 && parameterless))
        {
            return new SceneModel(qualifiedName, displayName, null, SceneFault.AmbiguousConstructors, location);
        }

        string? conventionalName = contentConstructors == 1 ? TypeNaming.FromTypeName(type.Name) : null;
        SceneFault discoveredFault = conventionalName is not null && !TypeNaming.IsSafeDocumentName(conventionalName)
            ? SceneFault.UnsafeDocumentName
            : Symbols.IsAccessibleFromGeneratedCode(type)
                ? SceneFault.None
                : SceneFault.InaccessibleType;

        return new SceneModel(qualifiedName, displayName, conventionalName, discoveredFault, location);
    }

    internal static void Emit(SourceProductionContext context, ImmutableArray<SceneModel> models, bool enginePresent)
    {
        if (!enginePresent)
        {
            return;
        }

        // Partial declarations are visited in path order so the fault's location is stable.
        List<SceneModel> ordered = new(models);
        ordered.Sort(static (left, right) =>
        {
            int byName = string.CompareOrdinal(left.QualifiedName, right.QualifiedName);
            if (byName != 0)
            {
                return byName;
            }

            string? leftPath = left.Location.SourceTree?.FilePath;
            string? rightPath = right.Location.SourceTree?.FilePath;
            int byPath = string.CompareOrdinal(leftPath ?? string.Empty, rightPath ?? string.Empty);
            if (byPath != 0)
            {
                return byPath;
            }

            if ((leftPath is null) != (rightPath is null))
            {
                return leftPath is null ? 1 : -1;
            }

            return left.Location.SourceSpan.Start.CompareTo(right.Location.SourceSpan.Start);
        });

        List<SceneModel> sound = new(ordered.Count);
        HashSet<string> described = new(StringComparer.Ordinal);
        foreach (SceneModel model in ordered)
        {
            // The parts of a partial class are separate declarations of one type.
            if (described.Add(model.QualifiedName))
            {
                switch (model.Fault)
                {
                    case SceneFault.SceneDocumentRequiresContentConstructor:
                        context.ReportDiagnostic(Diagnostic.Create(
                            RegistryDiagnostics.SceneDocumentRequiresContentConstructor, model.Location, model.DisplayName));
                        break;
                    case SceneFault.UnsafeDocumentName:
                        context.ReportDiagnostic(Diagnostic.Create(
                            RegistryDiagnostics.UnsafeSceneDocumentName, model.Location, model.DisplayName, model.DocumentName ?? string.Empty));
                        break;
                    case SceneFault.InaccessibleType:
                        context.ReportDiagnostic(Diagnostic.Create(
                            RegistryDiagnostics.InaccessibleRegisteredType, model.Location, model.DisplayName));
                        break;
                    case SceneFault.AmbiguousConstructors:
                        context.ReportDiagnostic(Diagnostic.Create(
                            RegistryDiagnostics.AmbiguousSceneConstructors, model.Location, model.DisplayName));
                        break;
                    default:
                        sound.Add(model);
                        break;
                }
            }
        }

        // Sorted before the duplicate check reads off it, for the same reason the entities are:
        // the collected order is whichever syntax trees the compiler handed over. Scenes no
        // document backs sort first, under the empty name, and never collide.
        sound.Sort(static (left, right) =>
        {
            int byDocument = string.CompareOrdinal(left.DocumentName ?? string.Empty, right.DocumentName ?? string.Empty);

            return byDocument != 0 ? byDocument : string.CompareOrdinal(left.QualifiedName, right.QualifiedName);
        });

        List<SceneModel> registered = new(sound.Count);
        foreach (SceneModel model in sound)
        {
            if (model.DocumentName is not null
                && registered.Count > 0
                && string.Equals(registered[registered.Count - 1].DocumentName, model.DocumentName, StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.DuplicateSceneDocumentName,
                    model.Location,
                    registered[registered.Count - 1].DisplayName,
                    model.DisplayName,
                    model.DocumentName));
                continue;
            }

            registered.Add(model);
        }

        context.AddSource(FileName, SourceText.From(Render(registered), Encoding.UTF8));
    }

    private static string Render(List<SceneModel> registered)
    {
        StringBuilder source = new();

        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();

        foreach (SceneModel model in registered)
        {
            if (model.DocumentName is null)
            {
                continue;
            }

            source.Append("[assembly: global::Capsule.Scenes.Generated.CapsuleGeneratedRegistryClaimAttribute(1, ");
            source.Append(SymbolDisplay.FormatLiteral(model.DocumentName, quote: true));
            source.Append(", typeof(");
            source.Append(model.QualifiedName);
            source.AppendLine("))]");
        }

        if (registered.Exists(static model => model.DocumentName is not null))
        {
            source.AppendLine();
        }

        source.AppendLine("namespace Capsule.Scenes.Generated");
        source.AppendLine("{");
        source.AppendLine("    /// <summary>Every scene this assembly declares. Generated; do not edit.</summary>");
        source.AppendLine("    [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
        source.AppendLine("    public static class GameScenes");
        source.AppendLine("    {");
        source.AppendLine("        internal static global::Capsule.Scenes.SceneRegistration[] Registrations { get; } =");
        source.AppendLine("            new global::Capsule.Scenes.SceneRegistration[]");
        source.AppendLine("            {");

        AppendRegistrations(source, registered, "                ");

        source.AppendLine("            };");
        source.AppendLine();
        source.AppendLine("        /// <summary>The registry the engine composes every scene through.</summary>");
        source.AppendLine("        public static global::Capsule.Scenes.SceneRegistry Registry { get; } =");
        source.AppendLine("            new global::Capsule.Scenes.SceneRegistry(");
        source.AppendLine("                global::Capsule.Scenes.Generated.GameEntities.Registry,");
        source.AppendLine("                Registrations);");
        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }

    private static void AppendRegistrations(StringBuilder source, List<SceneModel> registered, string indent)
    {
        foreach (SceneModel model in registered)
        {
            source.Append(indent);

            if (model.DocumentName is null)
            {
                source.Append("global::Capsule.Scenes.SceneRegistration.Plain(typeof(");
                source.Append(model.QualifiedName);
                source.Append("), static () => new ");
                source.Append(model.QualifiedName);
                source.AppendLine("()),");
                continue;
            }

            source.Append("global::Capsule.Scenes.SceneRegistration.FromDocument(typeof(");
            source.Append(model.QualifiedName);
            source.Append("), ");
            source.Append(SymbolDisplay.FormatLiteral(model.DocumentName, quote: true));
            source.Append(", static (global::Capsule.Scenes.SceneContent content) => new ");
            source.Append(model.QualifiedName);
            source.AppendLine("(content)),");
        }
    }
}
