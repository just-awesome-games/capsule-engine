using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Keys every entity class, refuses faulted and colliding claims, and reports each refusal.
internal static class EntityResolver
{
    internal static EntityPlan Resolve(EntityInputs inputs)
    {
        if (!inputs.Shared.IsLogicAssembly)
        {
            return default;
        }

        ImmutableArray<EntityModel> models = inputs.EngineTileMap is { } engine ? inputs.Models.Items.Add(engine) : inputs.Models.Items;

        // Every class's key, faulted ones included, which a document still names as claimed.
        Dictionary<string, string?> keys = new(StringComparer.Ordinal);
        foreach (EntityModel model in models)
        {
            if (!keys.ContainsKey(model.QualifiedName))
            {
                keys.Add(model.QualifiedName, RegistryPass.KeyOf(model, inputs.Shared.RootNamespace));
            }
        }

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
                if (RegistryPass.Claimed(diagnostics, model, keys[model.QualifiedName]) is { } key)
                {
                    sound.Add(new RegisteredEntity(key, model));
                }
            });

        List<RegisteredEntity> registered = RegistryPass.RejectDuplicateKeys(
            diagnostics,
            sound,
            static (left, right) =>
            {
                int byType = string.CompareOrdinal(left.Key, right.Key);

                return byType != 0 ? byType : string.CompareOrdinal(left.Model.QualifiedName, right.Model.QualifiedName);
            },
            static entry => entry.Key,
            static entry => entry.Model.DisplayName,
            static entry => entry.Model.At,
            Diagnostics.DuplicateTypeKey);

        ImmutableArray<string> claimed = [.. keys.Values
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static key => key, StringComparer.Ordinal)];

        List<EntityModel> placed = [.. registered.Where(static entry => !entry.Model.CodeOnly).Select(static entry => entry.Model)];
        EquatableArray<KeyedObject> objects = ObjectRenderer.Keyed(placed.SelectMany(static model => model.Objects.Items), inputs.Shared.RootNamespace, diagnostics);
        IEnumerable<PropertyModel> authored = placed.SelectMany(static model => model.Authored).Concat(ObjectRenderer.Authored(objects));

        return new EntityPlan(
            Generates: true,
            new([.. registered]),
            new(claimed),
            objects,
            Lookups(authored, new AssetTable(inputs.Shared.Assets.Items, inputs.Shared.Documents.Items)),
            new([.. diagnostics]));
    }

    private static DiagnosticDescriptor? Reported(EntityFault fault) => fault switch
    {
        EntityFault.NotAConcreteEntity => Diagnostics.NotAConcreteEntity,
        EntityFault.MissingSpawnConstructor => Diagnostics.MissingSpawnConstructor,
        EntityFault.BlankTypeKey => Diagnostics.BlankTypeKey,
        EntityFault.InaccessibleType => Diagnostics.InaccessibleRegisteredType,
        EntityFault.AmbiguousSpawnConstructors => Diagnostics.AmbiguousEntityConstructors,
        EntityFault.SpawnNotPassedToBase => Diagnostics.SpawnNotPassedToBase,
        _ => null,
    };

    // One lookup per asset type an authored member takes, in PropertyForms.Assets order, keyed as the build declared each asset.
    internal static EquatableArray<AssetLookup> Lookups(IEnumerable<PropertyModel> authored, AssetTable assets) =>
        new([.. PropertyForms.Assets
            .Where(form => authored.Any(property => property.Kind == PropertyKind.Asset && property.Type == form.Type))
            .Select(form => new AssetLookup(
                form,
                new([.. assets.Of(form)
                    .OrderBy(static asset => asset.Key, StringComparer.Ordinal)
                    .Select(static asset => (asset.Key, asset.Value))])))]);
}
