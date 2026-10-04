using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Writes CapsuleEntities.g.cs from a resolved plan. Entities/CapsuleEntities.sample.g.cs shows the file.
internal static class EntityRenderer
{
    private const string FileName = "CapsuleEntities.g.cs";

    // A multi-line registration's arguments sit one level inside the registration.
    private const string ArgumentIndent = "                    ";

    // An applier's statements sit one level inside its braces.
    private const string StatementIndent = ArgumentIndent + "    ";

    internal static void Emit(SourceProductionContext context, EntityPlan plan)
    {
        GeneratedFile.Report(context, plan.Diagnostics);

        if (plan.Generates)
        {
            GeneratedFile.Add(context, FileName, Render(plan));
        }
    }

    internal static string Render(EntityPlan plan)
    {
        List<RegisteredEntity> registrations = [.. plan.Registrations];
        List<string> claims = [.. registrations.Select(static entry =>
            GeneratedFile.ClaimAttribute(RegistryClaimKind.Entity, entry.SpawnType, entry.Model.QualifiedName))];
        List<PropertyModel> authored = [.. registrations.SelectMany(static entry => entry.Model.Authored)];
        string members = string.Concat(registrations
                .Where(static entry => entry.Model.Required)
                .Select(static entry => EntityAccessorRenderer.Constructor(
                    entry.Model.QualifiedName, entry.Model.SpawnModifier + "global::Capsule.Scenes.Spawning.EntitySpawn spawn")))
            + ObjectRenderer.Methods(plan.Objects, authored)
            + EntityAccessorRenderer.Accessors(authored.Concat(ObjectRenderer.Authored(plan.Objects)))
            + string.Concat(plan.Lookups.Items.Select(Lookup));

        return GeneratedFile.Write(claims, $$"""
                /// <summary>Every spawnable entity this assembly declares, as one registry. Generated code. Do not edit.</summary>
                {{GeneratedFile.ExcludeFromCodeCoverage}}
                public static class CapsuleEntities
                {
            {{GeneratedFile.Registrations("global::Capsule.Scenes.Spawning.EntityRegistration", registrations.Select(Registration))}}

                    /// <summary>The registry a scene resolves its spawn types through.</summary>
                    public static global::Capsule.Scenes.Spawning.EntityRegistry Registry { get; } =
                        new global::Capsule.Scenes.Spawning.EntityRegistry(Registrations);
            {{members}}    }
            """);
    }

    // The key and spawner on one line, or one argument per line when an applier follows.
    private static string Registration(RegisteredEntity entry)
    {
        EntityModel model = entry.Model;
        string spawner = model.Required
            ? $"static (global::Capsule.Scenes.Spawning.EntitySpawn spawn) => {EntityAccessorRenderer.ConstructorName(model.QualifiedName)}(spawn)"
            : $"static (global::Capsule.Scenes.Spawning.EntitySpawn spawn) => new {model.QualifiedName}(spawn)";
        if (!model.Authored.Any())
        {
            return $"new global::Capsule.Scenes.Spawning.EntityRegistration({CodeText.Literal(entry.SpawnType)}, {spawner})";
        }

        string[] arguments = [CodeText.Literal(entry.SpawnType), spawner, Applier(model)];

        return "new global::Capsule.Scenes.Spawning.EntityRegistration(\n"
            + string.Join(",\n", arguments.Select(static argument => ArgumentIndent + argument))
            + ")";
    }

    // The base constructor calls the applier before the derived body runs. The applier defers an entity reference
    // until every entry of the document is constructed.
    private static string Applier(EntityModel model) =>
        "static (placed, properties) =>\n"
        + ArgumentIndent + "{\n"
        + StatementIndent + $"{model.QualifiedName} entity = ({model.QualifiedName})placed;\n"
        + PropertyReadRenderer.Assignments(model.Authored, "entity", model.QualifiedName, StatementIndent)
        + ArgumentIndent + "}";

    // The switch one asset type's reads resolve a key through, to the member the build declared it on.
    internal static string Lookup(AssetLookup lookup) => $$"""

                private static {{lookup.Form.Type}}? {{lookup.Form.Lookup}}(string key) => key switch
                {
        {{string.Concat(lookup.Assets.Items.Select(static asset => $"            {CodeText.Literal(asset.Key)} => {asset.Member},\n"))}}            _ => null,
                };

        """;
}
