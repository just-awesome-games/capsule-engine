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

    // The namespace segment a scene is filed under says nothing its key has to repeat.
    private const string DomainSegment = "Scenes";

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
            new(qualifiedName, displayName, space, type.Name, documented, declared, fault, location);
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

        List<Registration> sound = new(ordered.Count);
        HashSet<string> described = new(StringComparer.Ordinal);
        foreach (SceneModel model in ordered)
        {
            // The parts of a partial class are separate declarations of one type.
            if (!described.Add(model.QualifiedName))
            {
                continue;
            }

            switch (model.Fault)
            {
                case SceneFault.SceneDocumentRequiresContentConstructor:
                    context.ReportDiagnostic(Diagnostic.Create(
                        RegistryDiagnostics.SceneDocumentRequiresContentConstructor, model.Location, model.DisplayName));
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
                    Resolve(context, sound, model, rootNamespace);
                    break;
            }
        }

        // Sorted before the duplicate check reads off it, for the same reason the entities are:
        // the collected order is whichever syntax trees the compiler handed over. Scenes no
        // document backs sort first, under the empty name, and never collide.
        sound.Sort(static (left, right) =>
        {
            int byDocument = string.CompareOrdinal(left.DocumentName ?? string.Empty, right.DocumentName ?? string.Empty);

            return byDocument != 0 ? byDocument : string.CompareOrdinal(left.Model.QualifiedName, right.Model.QualifiedName);
        });

        List<Registration> registered = new(sound.Count);
        foreach (Registration entry in sound)
        {
            if (entry.DocumentName is not null
                && registered.Count > 0
                && string.Equals(registered[registered.Count - 1].DocumentName, entry.DocumentName, StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.DuplicateSceneDocumentName,
                    entry.Model.Location,
                    registered[registered.Count - 1].Model.DisplayName,
                    entry.Model.DisplayName,
                    entry.DocumentName));
                continue;
            }

            registered.Add(entry);
        }

        context.AddSource(FileName, SourceText.From(Render(registered), Encoding.UTF8));
    }

    // Where the type is declared is the key it claims, so the key — and whether it is a name a file
    // can carry — is not settled until the assembly's root namespace is.
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

        string documentName = model.Declared
            ?? TypeNaming.KeyFor(model.ContainingNamespace, model.TypeName, rootNamespace, DomainSegment);

        if (TypeNaming.IsKey(documentName))
        {
            sound.Add(new Registration(documentName, model));

            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RegistryDiagnostics.UnsafeSceneDocumentName, model.Location, model.DisplayName, documentName));
    }

    private static string Render(List<Registration> registered)
    {
        StringBuilder source = new();

        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();

        foreach (Registration entry in registered)
        {
            if (entry.DocumentName is null)
            {
                continue;
            }

            source.Append("[assembly: global::Capsule.Scenes.Generated.CapsuleGeneratedRegistryClaimAttribute(1, ");
            source.Append(SymbolDisplay.FormatLiteral(entry.DocumentName, quote: true));
            source.Append(", typeof(");
            source.Append(entry.Model.QualifiedName);
            source.AppendLine("))]");
        }

        if (registered.Exists(static entry => entry.DocumentName is not null))
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

    private static void AppendRegistrations(StringBuilder source, List<Registration> registered, string indent)
    {
        foreach (Registration entry in registered)
        {
            source.Append(indent);

            if (entry.DocumentName is null)
            {
                source.Append("global::Capsule.Scenes.SceneRegistration.Plain(typeof(");
                source.Append(entry.Model.QualifiedName);
                source.Append("), static () => new ");
                source.Append(entry.Model.QualifiedName);
                source.AppendLine("()),");
                continue;
            }

            source.Append("global::Capsule.Scenes.SceneRegistration.FromDocument(typeof(");
            source.Append(entry.Model.QualifiedName);
            source.Append("), ");
            source.Append(SymbolDisplay.FormatLiteral(entry.DocumentName, quote: true));
            source.Append(", static (global::Capsule.Scenes.SceneContent content) => new ");
            source.Append(entry.Model.QualifiedName);
            source.AppendLine("(content)),");
        }
    }

    private readonly struct Registration(string? documentName, SceneModel model)
    {
        internal string? DocumentName { get; } = documentName;

        internal SceneModel Model { get; } = model;
    }
}
