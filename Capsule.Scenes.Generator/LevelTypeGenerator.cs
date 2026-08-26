using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Scenes.Generator;

/// <summary>
/// Emits one assembly's <c>[LevelType]</c> classes as
/// <c>Capsule.Scenes.Generated.LevelTypes.Registry</c>. Generating the registry rather than
/// reflecting for it at boot is what keeps a Capsule game NativeAOT-able and its spawn table
/// compile-checked; the diagnostics here are the checks that buys.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class LevelTypeGenerator : IIncrementalGenerator
{
    private const string LevelTypeAttributeName = "Capsule.Scenes.Spawning.LevelTypeAttribute";
    private const string EntityTypeName = "Capsule.Scenes.Entity";
    private const string SpawnTypeName = "Capsule.Scenes.Spawning.EntitySpawn";

    /// <summary>The file the registry is generated into.</summary>
    public const string GeneratedFileName = "CapsuleLevelTypes.g.cs";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // No candidates means the assembly declares no level types — including every assembly
        // that does not reference Capsule.Scenes at all — and nothing is generated there.
        IncrementalValuesProvider<LevelTypeModel> levelTypes = context.SyntaxProvider.ForAttributeWithMetadataName(
            LevelTypeAttributeName,
            static (node, _) => node is TypeDeclarationSyntax,
            static (attributed, _) => Describe(attributed));

        context.RegisterSourceOutput(levelTypes.Collect(), static (production, models) => Emit(production, models));
    }

    private static LevelTypeModel Describe(GeneratorAttributeSyntaxContext context)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.TargetSymbol;
        Location location = ((TypeDeclarationSyntax)context.TargetNode).Identifier.GetLocation();
        string qualified = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string display = type.ToDisplayString();

        string? explicitId = ExplicitId(context.Attributes);
        if (explicitId != null && string.IsNullOrWhiteSpace(explicitId))
        {
            return new LevelTypeModel(qualified, display, string.Empty, LevelTypeFault.BlankType, location);
        }

        string id = explicitId ?? LevelTypeId.FromTypeName(type.Name);
        Compilation compilation = context.SemanticModel.Compilation;

        LevelTypeFault fault = LevelTypeFault.None;
        if (!IsConcreteEntity(type, compilation))
        {
            fault = LevelTypeFault.NotAConcreteEntity;
        }
        else if (!HasSpawnConstructor(type, compilation))
        {
            fault = LevelTypeFault.MissingSpawnConstructor;
        }

        return new LevelTypeModel(qualified, display, id, fault, location);
    }

    private static string? ExplicitId(ImmutableArray<AttributeData> attributes)
    {
        foreach (AttributeData attribute in attributes)
        {
            // One argument means the string constructor: a null literal reaching it is an
            // explicit blank id, not an absent one.
            if (attribute.ConstructorArguments.Length == 1)
            {
                return attribute.ConstructorArguments[0].Value as string ?? string.Empty;
            }
        }

        return null;
    }

    private static bool IsConcreteEntity(INamedTypeSymbol type, Compilation compilation)
    {
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic || type.IsGenericType)
        {
            return false;
        }

        INamedTypeSymbol? entity = compilation.GetTypeByMetadataName(EntityTypeName);
        if (entity is null)
        {
            return false;
        }

        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, entity))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSpawnConstructor(INamedTypeSymbol type, Compilation compilation)
    {
        INamedTypeSymbol? spawn = compilation.GetTypeByMetadataName(SpawnTypeName);
        if (spawn is null)
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
            if (passable && SymbolEqualityComparer.Default.Equals(parameter.Type, spawn))
            {
                return true;
            }
        }

        return false;
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<LevelTypeModel> models)
    {
        if (models.IsDefaultOrEmpty)
        {
            return;
        }

        List<LevelTypeModel> sound = new(models.Length);
        foreach (LevelTypeModel model in models)
        {
            switch (model.Fault)
            {
                case LevelTypeFault.NotAConcreteEntity:
                    context.ReportDiagnostic(Diagnostic.Create(
                        LevelTypeDiagnostics.NotAConcreteEntity, model.Location, model.DisplayName));
                    break;
                case LevelTypeFault.MissingSpawnConstructor:
                    context.ReportDiagnostic(Diagnostic.Create(
                        LevelTypeDiagnostics.MissingSpawnConstructor, model.Location, model.DisplayName));
                    break;
                case LevelTypeFault.BlankType:
                    context.ReportDiagnostic(Diagnostic.Create(
                        LevelTypeDiagnostics.BlankLevelTypeId, model.Location, model.DisplayName));
                    break;
                default:
                    sound.Add(model);
                    break;
            }
        }

        // Sorted before anything is read off it: the collected order follows whichever syntax
        // trees the compiler happened to hand over, and both the duplicate check and the
        // emitted file must not depend on that.
        sound.Sort(static (left, right) =>
        {
            int byId = string.CompareOrdinal(left.Id, right.Id);

            return byId != 0 ? byId : string.CompareOrdinal(left.QualifiedName, right.QualifiedName);
        });

        List<LevelTypeModel> registered = new(sound.Count);
        foreach (LevelTypeModel model in sound)
        {
            if (registered.Count > 0 && string.Equals(registered[registered.Count - 1].Id, model.Id, StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    LevelTypeDiagnostics.DuplicateLevelTypeId,
                    model.Location,
                    registered[registered.Count - 1].DisplayName,
                    model.DisplayName,
                    model.Id));
                continue;
            }

            registered.Add(model);
        }

        // Emitted even when every candidate faulted, so the failure is the diagnostic above
        // rather than a cascade of "LevelTypes does not exist" at every call site.
        context.AddSource(GeneratedFileName, SourceText.From(Render(registered), Encoding.UTF8));
    }

    private static string Render(List<LevelTypeModel> registered)
    {
        StringBuilder source = new();

        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace Capsule.Scenes.Generated");
        source.AppendLine("{");
        source.AppendLine("    /// <summary>Every [LevelType] class in this assembly, as one registry. Generated; do not edit.</summary>");
        source.AppendLine("    public static class LevelTypes");
        source.AppendLine("    {");
        source.AppendLine("        /// <summary>The registry a scene resolves its level's entity types through.</summary>");
        source.AppendLine("        public static global::Capsule.Scenes.Spawning.LevelTypeRegistry Registry { get; } =");
        source.AppendLine("            new global::Capsule.Scenes.Spawning.LevelTypeRegistry(");
        source.AppendLine("                new global::System.Collections.Generic.KeyValuePair<string, global::Capsule.Scenes.Spawning.EntitySpawner>[]");
        source.AppendLine("                {");

        foreach (LevelTypeModel model in registered)
        {
            source.Append("                    new global::System.Collections.Generic.KeyValuePair<string, global::Capsule.Scenes.Spawning.EntitySpawner>(");
            source.Append(SymbolDisplay.FormatLiteral(model.Id, quote: true));
            source.Append(", static (global::Capsule.Scenes.Spawning.EntitySpawn spawn) => new ");
            source.Append(model.QualifiedName);
            source.AppendLine("(spawn)),");
        }

        source.AppendLine("                });");
        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }
}
