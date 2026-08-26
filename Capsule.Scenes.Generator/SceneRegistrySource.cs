using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Scenes.Generator;

/// <summary>
/// The <c>GameScenes</c> registry: every class in the assembly that is a non-abstract
/// <c>Capsule.Scenes.Scene</c>, under the constructor shape that says how it is built — one taking
/// a <c>MapSceneContext</c> is composed from the map its kebab-cased class name derives, one
/// taking nothing is a scene no map backs.
/// </summary>
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
        int mapConstructors = concreteScene
            ? Symbols.PublicConstructorsTaking(type, compilation, Symbols.MapSceneContext)
            : 0;
        bool parameterless = concreteScene && Symbols.HasPublicParameterlessConstructor(type);
        AttributeData? annotation = Symbols.Attribute(type, compilation, Symbols.MapNameAttribute);

        if (annotation is not null)
        {
            if (!concreteScene || mapConstructors == 0)
            {
                return new SceneModel(qualifiedName, displayName, null, SceneFault.MapNameRequiresMapScene, location);
            }

            if (mapConstructors > 1 || parameterless)
            {
                return new SceneModel(qualifiedName, displayName, null, SceneFault.AmbiguousConstructors, location);
            }

            if (annotation.ConstructorArguments.Length != 1)
            {
                return null;
            }

            string mapName = annotation.ConstructorArguments[0].Value as string ?? string.Empty;
            SceneFault fault = !TypeNaming.IsSafeMapName(mapName)
                ? SceneFault.UnsafeMapName
                : Symbols.IsAccessibleFromGeneratedCode(type)
                    ? SceneFault.None
                    : SceneFault.InaccessibleType;

            return new SceneModel(qualifiedName, displayName, mapName, fault, location);
        }

        if (!concreteScene || (mapConstructors == 0 && !parameterless))
        {
            return null;
        }

        if (mapConstructors > 1 || (mapConstructors == 1 && parameterless))
        {
            return new SceneModel(qualifiedName, displayName, null, SceneFault.AmbiguousConstructors, location);
        }

        string? conventionalMapName = mapConstructors == 1 ? TypeNaming.FromTypeName(type.Name) : null;
        SceneFault discoveredFault = conventionalMapName is not null && !TypeNaming.IsSafeMapName(conventionalMapName)
            ? SceneFault.UnsafeMapName
            : Symbols.IsAccessibleFromGeneratedCode(type)
                ? SceneFault.None
                : SceneFault.InaccessibleType;

        return new SceneModel(qualifiedName, displayName, conventionalMapName, discoveredFault, location);
    }

    internal static void Emit(SourceProductionContext context, ImmutableArray<SceneModel> models, bool enginePresent)
    {
        if (!enginePresent)
        {
            return;
        }

        List<SceneModel> sound = new(models.Length);
        HashSet<string> described = new(StringComparer.Ordinal);
        foreach (SceneModel model in models)
        {
            // The parts of a partial class are separate declarations of one type.
            if (described.Add(model.QualifiedName))
            {
                switch (model.Fault)
                {
                    case SceneFault.MapNameRequiresMapScene:
                        context.ReportDiagnostic(Diagnostic.Create(
                            RegistryDiagnostics.MapNameRequiresMapScene, model.Location, model.DisplayName));
                        break;
                    case SceneFault.UnsafeMapName:
                        context.ReportDiagnostic(Diagnostic.Create(
                            RegistryDiagnostics.UnsafeMapName, model.Location, model.DisplayName, model.MapName ?? string.Empty));
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
        // the collected order is whichever syntax trees the compiler handed over. Scenes no map
        // backs sort first, under the empty name, and never collide.
        sound.Sort(static (left, right) =>
        {
            int byMap = string.CompareOrdinal(left.MapName ?? string.Empty, right.MapName ?? string.Empty);

            return byMap != 0 ? byMap : string.CompareOrdinal(left.QualifiedName, right.QualifiedName);
        });

        List<SceneModel> registered = new(sound.Count);
        foreach (SceneModel model in sound)
        {
            if (model.MapName is not null
                && registered.Count > 0
                && string.Equals(registered[registered.Count - 1].MapName, model.MapName, StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.DuplicateSceneMapName,
                    model.Location,
                    registered[registered.Count - 1].DisplayName,
                    model.DisplayName,
                    model.MapName));
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
            if (model.MapName is null)
            {
                continue;
            }

            source.Append("[assembly: global::Capsule.Scenes.Generated.CapsuleGeneratedRegistryClaimAttribute(1, ");
            source.Append(SymbolDisplay.FormatLiteral(model.MapName, quote: true));
            source.Append(", typeof(");
            source.Append(model.QualifiedName);
            source.AppendLine("))]");
        }

        if (registered.Exists(static model => model.MapName is not null))
        {
            source.AppendLine();
        }

        source.AppendLine("namespace Capsule.Scenes.Generated");
        source.AppendLine("{");
        source.AppendLine("    /// <summary>Every scene this assembly declares, as one registry. Generated; do not edit.</summary>");
        source.AppendLine("    [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
        source.AppendLine("    public static class GameScenes");
        source.AppendLine("    {");
        source.AppendLine("        internal static global::Capsule.Scenes.SceneRegistration[] Registrations { get; } =");
        source.AppendLine("            new global::Capsule.Scenes.SceneRegistration[]");
        source.AppendLine("            {");

        AppendRegistrations(source, registered, "                ");

        source.AppendLine("            };");
        source.AppendLine();
        source.AppendLine("        /// <summary>The registry the engine boots a scene through.</summary>");
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
            if (model.MapName is null)
            {
                source.Append(indent);
                source.Append("global::Capsule.Scenes.SceneRegistration.Plain(typeof(");
                source.Append(model.QualifiedName);
                source.Append("), static () => new ");
                source.Append(model.QualifiedName);
                source.AppendLine("()),");
                continue;
            }

            source.Append(indent);
            source.Append("global::Capsule.Scenes.SceneRegistration.MapBacked(typeof(");
            source.Append(model.QualifiedName);
            source.Append("), ");
            source.Append(SymbolDisplay.FormatLiteral(model.MapName, quote: true));
            source.Append(", static (global::Capsule.Scenes.MapSceneContext context) => new ");
            source.Append(model.QualifiedName);
            source.AppendLine("(context)),");
        }
    }
}
