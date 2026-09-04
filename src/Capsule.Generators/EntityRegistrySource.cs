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

    // The namespace segment an entity is filed under says nothing its key has to repeat.
    private const string DomainSegment = "Entities";

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

            return Model(type, declaration, null, discoveredFault);
        }

        // The attribute has one form; any other call is the compiler's error to report, not this.
        if (annotation.ConstructorArguments.Length != 1)
        {
            return null;
        }

        string? spawnType = annotation.ConstructorArguments[0].Value as string;
        if (string.IsNullOrWhiteSpace(spawnType))
        {
            return Model(type, declaration, null, EntityFault.BlankSpawnType);
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

    internal static void Emit(
        SourceProductionContext context,
        ImmutableArray<EntityModel> models,
        bool enginePresent,
        string rootNamespace)
    {
        if (!enginePresent)
        {
            return;
        }

        // Partial declarations are visited in path order so the fault's location is stable.
        List<EntityModel> ordered = new(models);
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
        foreach (EntityModel model in ordered)
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
                    // Where the type is declared is the key it claims, so the key — and whether it
                    // is a key at all — is not settled until the assembly's root namespace is.
                    Resolve(context, sound, model, rootNamespace);
                    break;
            }
        }

        // Sorted before anything is read off it: the collected order follows whichever syntax
        // trees the compiler happened to hand over, and both the duplicate check and the
        // emitted file must not depend on that.
        sound.Sort(static (left, right) =>
        {
            int byType = string.CompareOrdinal(left.SpawnType, right.SpawnType);

            return byType != 0 ? byType : string.CompareOrdinal(left.Model.QualifiedName, right.Model.QualifiedName);
        });

        List<Registration> registered = new(sound.Count);
        foreach (Registration entry in sound)
        {
            if (registered.Count > 0 && string.Equals(registered[registered.Count - 1].SpawnType, entry.SpawnType, StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.DuplicateSpawnType,
                    entry.Model.Location,
                    registered[registered.Count - 1].Model.DisplayName,
                    entry.Model.DisplayName,
                    entry.SpawnType));
                continue;
            }

            registered.Add(entry);
        }

        context.AddSource(FileName, SourceText.From(Render(registered), Encoding.UTF8));
    }

    // A key names the document entry a scene spawns from, and an override names it whole, so it is
    // held to the same grammar as the key the namespace would have named.
    private static void Resolve(
        SourceProductionContext context,
        List<Registration> sound,
        EntityModel model,
        string rootNamespace)
    {
        string spawnType = model.Declared
            ?? TypeNaming.KeyFor(model.ContainingNamespace, model.TypeName, rootNamespace, DomainSegment);

        if (TypeNaming.IsKey(spawnType))
        {
            sound.Add(new Registration(spawnType, model));

            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RegistryDiagnostics.UnsafeSpawnType, model.Location, model.DisplayName, spawnType));
    }

    private static EntityModel Model(INamedTypeSymbol type, TypeDeclarationSyntax declaration, string? declared, EntityFault fault) =>
        new(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.ToDisplayString(),
            type.ContainingNamespace is { IsGlobalNamespace: false } space ? space.ToDisplayString() : string.Empty,
            type.Name,
            declared,
            fault,
            declaration.Identifier.GetLocation());

    private static string Render(List<Registration> registered)
    {
        StringBuilder source = new();

        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();

        foreach (Registration entry in registered)
        {
            source.Append("[assembly: global::Capsule.Scenes.Generated.CapsuleGeneratedRegistryClaimAttribute(0, ");
            source.Append(SymbolDisplay.FormatLiteral(entry.SpawnType, quote: true));
            source.Append(", typeof(");
            source.Append(entry.Model.QualifiedName);
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

        foreach (Registration entry in registered)
        {
            source.Append("                new global::System.Collections.Generic.KeyValuePair<string, global::Capsule.Scenes.Spawning.EntitySpawner>(");
            source.Append(SymbolDisplay.FormatLiteral(entry.SpawnType, quote: true));
            source.Append(", static (global::Capsule.Scenes.Spawning.EntitySpawn spawn) => new ");
            source.Append(entry.Model.QualifiedName);
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

    private readonly struct Registration(string spawnType, EntityModel model)
    {
        internal string SpawnType { get; } = spawnType;

        internal EntityModel Model { get; } = model;
    }
}
