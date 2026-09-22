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

    // Entity keys drop this namespace segment, since it repeats the domain.
    private const string DomainSegment = "Entities";

    internal static EntityModel? Describe(INamedTypeSymbol type, TypeDeclarationSyntax declaration, SemanticModel model)
    {
        Compilation compilation = model.Compilation;
        bool concreteEntity = Symbols.IsConcreteClass(type) && Symbols.DerivesFrom(type, compilation, Symbols.Entity);
        int spawnConstructors = concreteEntity
            ? Symbols.PublicConstructorsTaking(type, compilation, Symbols.EntitySpawn)
            : 0;
        AttributeData? annotation = Symbols.Attribute(type, compilation, Symbols.SpawnTypeAttribute);

        if (annotation is null)
        {
            // A class of the wrong shape that claims nothing is an ordinary class, not a mistake.
            if (spawnConstructors == 0)
            {
                return null;
            }

            EntityFault discoveredFault = spawnConstructors > 1
                ? EntityFault.AmbiguousSpawnConstructors
                : Symbols.IsAccessibleFromGeneratedCode(type)
                    ? EntityFault.None
                    : EntityFault.InaccessibleType;

            return SpawnChecked(type, declaration, null, discoveredFault, model);
        }

        // The attribute has a single form. Any other call is the compiler's error to report.
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

        return SpawnChecked(type, declaration, spawnType!, fault, model);
    }

    // A sound claim must also pass its spawn to the base constructor, or the authored band and
    // factor the spawn carries never reach the entity. The fault is reported at the constructor.
    private static EntityModel SpawnChecked(
        INamedTypeSymbol type,
        TypeDeclarationSyntax declaration,
        string? declared,
        EntityFault fault,
        SemanticModel model)
    {
        if (fault != EntityFault.None)
        {
            return Model(type, declaration, declared, fault);
        }

        IMethodSymbol? spawnConstructor = Symbols.SpawnConstructor(type, model.Compilation);
        if (spawnConstructor is null || Symbols.PassesSpawnOn(spawnConstructor, model, out Location? at))
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
            static model => model.At,
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
            static entry => entry.Model.At,
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

    // The key comes from where the type is declared, so it is not settled until the assembly's root
    // namespace is known. An explicit [SpawnType] names the full key under the same grammar.
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
            RegistryDiagnostics.UnsafeSpawnType, model.At.Location(), model.DisplayName, spawnType));
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
            DeclaredAt.From(at ?? declaration.Identifier.GetLocation()));

    private static string Render(List<Registration> registered)
    {
        StringBuilder claims = new();
        StringBuilder registrations = new();

        foreach (Registration entry in registered)
        {
            claims.Append("[assembly: global::Capsule.Generated.CapsuleGeneratedRegistryClaimAttribute(0, ")
                .Append(SymbolDisplay.FormatLiteral(entry.SpawnType, quote: true))
                .Append(", typeof(").Append(entry.Model.QualifiedName).AppendLine("))]");

            registrations.Append("                new global::Capsule.Scenes.Spawning.EntityRegistration(")
                .Append(SymbolDisplay.FormatLiteral(entry.SpawnType, quote: true))
                .Append(", static (global::Capsule.Scenes.Spawning.EntitySpawn spawn) => new ")
                .Append(entry.Model.QualifiedName).AppendLine("(spawn)),");
        }

        if (registered.Count > 0)
        {
            claims.AppendLine();
        }

        return $$"""
            // <auto-generated/>
            #nullable enable

            {{claims}}namespace Capsule.Generated
            {
                /// <summary>Every spawnable entity this assembly declares, as one registry. Generated code. Do not edit.</summary>
                [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
                public static class CapsuleEntities
                {
                    internal static global::Capsule.Scenes.Spawning.EntityRegistration[] Registrations { get; } =
                        new global::Capsule.Scenes.Spawning.EntityRegistration[]
                        {
            {{registrations}}            };

                    /// <summary>The registry a scene resolves its spawn types through.</summary>
                    public static global::Capsule.Scenes.Spawning.EntityRegistry Registry { get; } =
                        new global::Capsule.Scenes.Spawning.EntityRegistry(Registrations);
                }
            }

            """;
    }

    private readonly struct Registration(string spawnType, EntityModel model)
    {
        internal string SpawnType { get; } = spawnType;

        internal EntityModel Model { get; } = model;
    }
}
