using System.Collections.Immutable;
using Capsule.Assets;
using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Keys every entity class, refuses faulted and colliding claims, and reports each refusal.
internal static class EntityResolver
{
    internal static EntityPlan Resolve(EntityInputs inputs)
    {
        if (!inputs.IsLogicAssembly)
        {
            return default;
        }

        ImmutableArray<EntityModel> models = inputs.Models.Items;
        List<Diagnostic> diagnostics = [];
        List<RegisteredEntity> sound = [];
        RegistryPass.ValidateAndOrder(
            diagnostics,
            models,
            static model => model.QualifiedName,
            static model => model.DisplayName,
            static model => model.At,
            static model => Reported(model.Fault),
            model =>
            {
                string spawnType = KeyOf(model, inputs.RootNamespace);
                if (AssetPaths.IsKey(spawnType))
                {
                    sound.Add(new RegisteredEntity(spawnType, model));
                }
                else
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.UnsafeSpawnType, model.At.Location(), model.DisplayName, spawnType));
                }
            });

        List<RegisteredEntity> registered = RegistryPass.RejectDuplicateKeys(
            diagnostics,
            sound,
            static (left, right) =>
            {
                int byType = string.CompareOrdinal(left.SpawnType, right.SpawnType);

                return byType != 0 ? byType : string.CompareOrdinal(left.Model.QualifiedName, right.Model.QualifiedName);
            },
            static entry => entry.SpawnType,
            static entry => entry.Model.DisplayName,
            static entry => entry.Model.At,
            Diagnostics.DuplicateSpawnType);

        ImmutableArray<string> claimed = [.. models
            .Select(model => KeyOf(model, inputs.RootNamespace))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static key => key, StringComparer.Ordinal)];

        IEnumerable<PropertyModel> authored = registered
            .Where(static entry => !entry.Model.CodeOnly)
            .SelectMany(static entry => entry.Model.Authored);

        return new EntityPlan(
            Generates: true,
            new([.. registered]),
            new(claimed),
            Lookups(authored, new AssetTable(inputs.Assets.Items, inputs.Documents.Items)),
            new([.. diagnostics]));
    }

    private static DiagnosticDescriptor? Reported(EntityFault fault) => fault switch
    {
        EntityFault.NotAConcreteEntity => Diagnostics.NotAConcreteEntity,
        EntityFault.MissingSpawnConstructor => Diagnostics.MissingSpawnConstructor,
        EntityFault.BlankSpawnType => Diagnostics.BlankSpawnType,
        EntityFault.InaccessibleType => Diagnostics.InaccessibleRegisteredType,
        EntityFault.AmbiguousSpawnConstructors => Diagnostics.AmbiguousEntityConstructors,
        EntityFault.SpawnNotPassedToBase => Diagnostics.SpawnNotPassedToBase,
        _ => null,
    };

    // The key comes from where the type is declared, so it is not settled until the assembly's root
    // namespace is known. An explicit [SpawnType] names the full key under the same grammar.
    private static string KeyOf(EntityModel model, string rootNamespace) =>
        model.Declared ?? TypeNaming.KeyFor(model.ContainingNamespace, model.TypeName, rootNamespace);

    // One lookup per asset type an authored member takes, in PropertyForms.Assets order, keyed as the build declared each asset.
    private static EquatableArray<AssetLookup> Lookups(IEnumerable<PropertyModel> authored, AssetTable assets) =>
        new([.. PropertyForms.Assets
            .Where(form => authored.Any(property => property.Kind == PropertyKind.Asset && property.Type == form.Type))
            .Select(form => new AssetLookup(
                form,
                new([.. assets.Of(form)
                    .OrderBy(static asset => asset.Key, StringComparer.Ordinal)
                    .Select(static asset => (asset.Key, asset.Value))])))]);
}
