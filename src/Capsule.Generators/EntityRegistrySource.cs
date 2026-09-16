using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Capsule.Assets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

internal static class EntityRegistrySource
{
    private const string FileName = "CapsuleEntities.g.cs";

    // The namespace segment an entity is filed under says nothing its key has to repeat.
    private const string DomainSegment = "Entities";

    internal static EntityModel? Describe(INamedTypeSymbol type, TypeDeclarationSyntax declaration, Compilation compilation)
    {
        bool concreteEntity = Symbols.IsConcreteClass(type) && Symbols.DerivesFrom(type, compilation, Symbols.Entity);
        int spawnConstructors = concreteEntity
            ? Symbols.PublicConstructorsTaking(type, compilation, Symbols.EntitySpawn)
            : 0;
        AttributeData? annotation = Symbols.Attribute(type, compilation, Symbols.SpawnTypeAttribute);

        if (annotation is null)
        {
            // A class of the wrong shape claiming nothing is an ordinary class, not a mistake.
            if (spawnConstructors == 0)
            {
                return null;
            }

            EntityFault discoveredFault = spawnConstructors > 1
                ? EntityFault.AmbiguousSpawnConstructors
                : Symbols.IsAccessibleFromGeneratedCode(type)
                    ? EntityFault.None
                    : EntityFault.InaccessibleType;

            return SpawnChecked(type, declaration, null, discoveredFault, compilation);
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

        return SpawnChecked(type, declaration, spawnType!, fault, compilation);
    }

    // A sound claim is held to one more shape: the spawn constructor hands its spawn on, or the
    // authored band and factor the spawn carries never reach the entity. Reported at that
    // constructor rather than the type.
    private static EntityModel SpawnChecked(
        INamedTypeSymbol type,
        TypeDeclarationSyntax declaration,
        string? declared,
        EntityFault fault,
        Compilation compilation)
    {
        if (fault != EntityFault.None)
        {
            return Model(type, declaration, declared, fault);
        }

        IMethodSymbol? spawnConstructor = Symbols.SpawnConstructor(type, compilation);
        if (spawnConstructor is null || Symbols.PassesSpawnOn(spawnConstructor, compilation, out Location? at))
        {
            return Model(type, declaration, declared, fault);
        }

        return Model(type, declaration, declared, EntityFault.SpawnNotPassedToBase, at);
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

        List<Registration> sound = [];
        RegistryPass.Sound(
            context,
            models,
            static model => model.QualifiedName,
            static model => model.DisplayName,
            static model => model.Location,
            static model => Reported(model.Fault),
            model => Resolve(context, sound, model, rootNamespace));

        List<Registration> registered = RegistryPass.Claimed(
            context,
            sound,
            static (left, right) =>
            {
                int byType = string.CompareOrdinal(left.SpawnType, right.SpawnType);

                return byType != 0 ? byType : string.CompareOrdinal(left.Model.QualifiedName, right.Model.QualifiedName);
            },
            static entry => entry.SpawnType,
            static entry => entry.Model.DisplayName,
            static entry => entry.Model.Location,
            RegistryDiagnostics.DuplicateSpawnType);

        context.AddSource(FileName, SourceText.From(Render(registered), Encoding.UTF8));
    }

    private static DiagnosticDescriptor? Reported(EntityFault fault) => fault switch
    {
        EntityFault.NotAConcreteEntity => RegistryDiagnostics.NotAConcreteEntity,
        EntityFault.MissingSpawnConstructor => RegistryDiagnostics.MissingSpawnConstructor,
        EntityFault.BlankSpawnType => RegistryDiagnostics.BlankSpawnType,
        EntityFault.InaccessibleType => RegistryDiagnostics.InaccessibleRegisteredType,
        EntityFault.AmbiguousSpawnConstructors => RegistryDiagnostics.AmbiguousEntityConstructors,
        EntityFault.SpawnNotPassedToBase => RegistryDiagnostics.SpawnNotPassedToBase,
        _ => null,
    };

    // Where the type is declared is the key it claims, so the key is not settled until the
    // assembly's root namespace is. An override names a whole key, held to the same grammar.
    private static void Resolve(
        SourceProductionContext context,
        List<Registration> sound,
        EntityModel model,
        string rootNamespace)
    {
        string spawnType = model.Declared
            ?? TypeNaming.KeyFor(model.ContainingNamespace, model.TypeName, rootNamespace, DomainSegment);

        if (AssetPaths.IsKey(spawnType))
        {
            sound.Add(new Registration(spawnType, model));

            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RegistryDiagnostics.UnsafeSpawnType, model.Location, model.DisplayName, spawnType));
    }

    private static EntityModel Model(
        INamedTypeSymbol type,
        TypeDeclarationSyntax declaration,
        string? declared,
        EntityFault fault,
        Location? at = null) =>
        new(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.ToDisplayString(),
            type.ContainingNamespace is { IsGlobalNamespace: false } space ? space.ToDisplayString() : string.Empty,
            type.Name,
            declared,
            fault,
            at ?? declaration.Identifier.GetLocation());

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
        source.AppendLine("    public static class CapsuleEntities");
        source.AppendLine("    {");
        source.AppendLine("        internal static global::Capsule.Scenes.Spawning.EntityRegistration[] Registrations { get; } =");
        source.AppendLine("            new global::Capsule.Scenes.Spawning.EntityRegistration[]");
        source.AppendLine("            {");

        for (int i = 0; i < registered.Count; i++)
        {
            Registration entry = registered[i];

            source.Append("                new global::Capsule.Scenes.Spawning.EntityRegistration(");
            source.Append(SymbolDisplay.FormatLiteral(entry.SpawnType, quote: true));
            source.Append(", static (global::Capsule.Scenes.Spawning.EntitySpawn spawn) => new ");
            source.Append(entry.Model.QualifiedName);
            source.Append("(spawn)");
            source.AppendLine("),");
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
