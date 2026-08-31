using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

internal static class EntityRegistrySource
{
    private const string FileName = "CapsuleGameEntities.g.cs";

    internal static EntityModel? Describe(GeneratorSyntaxContext context, CancellationToken cancellation)
    {
        TypeDeclarationSyntax declaration = (TypeDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration, cancellation) is not INamedTypeSymbol type)
        {
            return null;
        }

        Compilation compilation = context.SemanticModel.Compilation;
        bool concreteEntity = Symbols.IsConcreteClass(type) && Symbols.DerivesFrom(type, compilation, Symbols.Entity);
        int spawnConstructors = concreteEntity
            ? Symbols.PublicConstructorsTaking(type, compilation, Symbols.EntitySpawn)
            : 0;
        AttributeData? annotation = Symbols.Attribute(type, compilation, Symbols.SpawnTypeAttribute);

        if (annotation is null)
        {
            // A class of the wrong shape has said nothing and is claiming nothing: it is an
            // ordinary class, not a mistake.
            if (spawnConstructors == 0)
            {
                return null;
            }

            EntityFault discoveredFault = spawnConstructors > 1
                ? EntityFault.AmbiguousSpawnConstructors
                : Symbols.IsAccessibleFromGeneratedCode(type)
                    ? EntityFault.None
                    : EntityFault.InaccessibleType;

            return Model(type, declaration, TypeNaming.FromTypeName(type.Name), discoveredFault);
        }

        // The attribute has one form; any other call is the compiler's error to report, not this.
        if (annotation.ConstructorArguments.Length != 1)
        {
            return null;
        }

        string? spawnType = annotation.ConstructorArguments[0].Value as string;
        if (string.IsNullOrWhiteSpace(spawnType))
        {
            return Model(type, declaration, string.Empty, EntityFault.BlankSpawnType);
        }

        EntityFault fault = EntityFault.None;
        if (!concreteEntity)
        {
            fault = EntityFault.NotAConcreteEntity;
        }
        else if (spawnConstructors == 0)
        {
            fault = EntityFault.MissingSpawnConstructor;
        }
        else if (spawnConstructors > 1)
        {
            fault = EntityFault.AmbiguousSpawnConstructors;
        }
        else if (!Symbols.IsAccessibleFromGeneratedCode(type))
        {
            fault = EntityFault.InaccessibleType;
        }

        return Model(type, declaration, spawnType!, fault);
    }

    internal static void Emit(SourceProductionContext context, ImmutableArray<EntityModel> models, bool enginePresent)
    {
        if (!enginePresent)
        {
            return;
        }

        List<EntityModel> sound = new(models.Length);
        HashSet<string> described = new(StringComparer.Ordinal);
        foreach (EntityModel model in models)
        {
            // The parts of a partial class are separate declarations of one type, and only the
            // type is registered or faulted.
            if (!described.Add(model.QualifiedName))
            {
                continue;
            }

            switch (model.Fault)
            {
                case EntityFault.NotAConcreteEntity:
                    context.ReportDiagnostic(Diagnostic.Create(
                        RegistryDiagnostics.NotAConcreteEntity, model.Location, model.DisplayName));
                    break;
                case EntityFault.MissingSpawnConstructor:
                    context.ReportDiagnostic(Diagnostic.Create(
                        RegistryDiagnostics.MissingSpawnConstructor, model.Location, model.DisplayName));
                    break;
                case EntityFault.BlankSpawnType:
                    context.ReportDiagnostic(Diagnostic.Create(
                        RegistryDiagnostics.BlankSpawnType, model.Location, model.DisplayName));
                    break;
                case EntityFault.InaccessibleType:
                    context.ReportDiagnostic(Diagnostic.Create(
                        RegistryDiagnostics.InaccessibleRegisteredType, model.Location, model.DisplayName));
                    break;
                case EntityFault.AmbiguousSpawnConstructors:
                    context.ReportDiagnostic(Diagnostic.Create(
                        RegistryDiagnostics.AmbiguousEntityConstructors, model.Location, model.DisplayName));
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
            int byType = string.CompareOrdinal(left.SpawnType, right.SpawnType);

            return byType != 0 ? byType : string.CompareOrdinal(left.QualifiedName, right.QualifiedName);
        });

        List<EntityModel> registered = new(sound.Count);
        foreach (EntityModel model in sound)
        {
            if (registered.Count > 0 && string.Equals(registered[registered.Count - 1].SpawnType, model.SpawnType, StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.DuplicateSpawnType,
                    model.Location,
                    registered[registered.Count - 1].DisplayName,
                    model.DisplayName,
                    model.SpawnType));
                continue;
            }

            registered.Add(model);
        }

        context.AddSource(FileName, SourceText.From(Render(registered), Encoding.UTF8));
    }

    private static EntityModel Model(INamedTypeSymbol type, TypeDeclarationSyntax declaration, string spawnType, EntityFault fault) =>
        new(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.ToDisplayString(),
            spawnType,
            fault,
            declaration.Identifier.GetLocation());

    private static string Render(List<EntityModel> registered)
    {
        StringBuilder source = new();

        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();

        foreach (EntityModel model in registered)
        {
            source.Append("[assembly: global::Capsule.Scenes.Generated.CapsuleGeneratedRegistryClaimAttribute(0, ");
            source.Append(SymbolDisplay.FormatLiteral(model.SpawnType, quote: true));
            source.Append(", typeof(");
            source.Append(model.QualifiedName);
            source.AppendLine("))]");
        }

        if (registered.Count > 0)
        {
            source.AppendLine();
        }

        source.AppendLine("namespace Capsule.Scenes.Generated");
        source.AppendLine("{");
        source.AppendLine("    /// <summary>Every spawnable entity this assembly declares, as one registry. Generated; do not edit.</summary>");
        source.AppendLine("    [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
        source.AppendLine("    public static class GameEntities");
        source.AppendLine("    {");
        source.AppendLine("        internal static global::System.Collections.Generic.KeyValuePair<string, global::Capsule.Scenes.Spawning.EntitySpawner>[] Registrations { get; } =");
        source.AppendLine("            new global::System.Collections.Generic.KeyValuePair<string, global::Capsule.Scenes.Spawning.EntitySpawner>[]");
        source.AppendLine("            {");

        foreach (EntityModel model in registered)
        {
            source.Append("                new global::System.Collections.Generic.KeyValuePair<string, global::Capsule.Scenes.Spawning.EntitySpawner>(");
            source.Append(SymbolDisplay.FormatLiteral(model.SpawnType, quote: true));
            source.Append(", static (global::Capsule.Scenes.Spawning.EntitySpawn spawn) => new ");
            source.Append(model.QualifiedName);
            source.AppendLine("(spawn)),");
        }

        source.AppendLine("            };");
        source.AppendLine();
        source.AppendLine("        /// <summary>The registry a scene resolves its spawn types through.</summary>");
        source.AppendLine("        public static global::Capsule.Scenes.Spawning.EntityRegistry Registry { get; } =");
        source.AppendLine("            new global::Capsule.Scenes.Spawning.EntityRegistry(Registrations);");
        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }
}
