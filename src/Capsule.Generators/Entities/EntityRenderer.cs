using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

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
        foreach (Diagnostic diagnostic in plan.Diagnostics.Items)
        {
            context.ReportDiagnostic(diagnostic);
        }

        if (plan.Generates)
        {
            context.AddSource(FileName, SourceText.From(Render(plan), Encoding.UTF8));
        }
    }

    internal static string Render(EntityPlan plan)
    {
        List<RegisteredEntity> registrations = [.. plan.Registrations];
        List<string> claims = [.. registrations.Select(static entry =>
            GeneratedFile.ClaimAttribute(RegistryClaimKind.Entity, entry.SpawnType, entry.Model.QualifiedName))];
        string members = EntityAccessorRenderer.Constructors(registrations.Select(static entry => entry.Model).Where(static model => model.Required))
            + EntityAccessorRenderer.Setters(registrations.SelectMany(static entry => entry.Model.Authored).Where(static property => !property.Direct))
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

    // The key and spawner on one line, or one argument per line when appliers follow.
    private static string Registration(RegisteredEntity entry)
    {
        EntityModel model = entry.Model;
        string spawner = model.Required
            ? $"static (global::Capsule.Scenes.Spawning.EntitySpawn spawn) => {EntityAccessorRenderer.ConstructorName(model)}(spawn)"
            : $"static (global::Capsule.Scenes.Spawning.EntitySpawn spawn) => new {model.QualifiedName}(spawn)";
        bool applies = model.Authored.Any(static property => property.Kind != PropertyKind.Reference);
        bool links = model.Authored.Any(static property => property.Kind == PropertyKind.Reference);
        if (!applies && !links)
        {
            return $"new global::Capsule.Scenes.Spawning.EntityRegistration({CodeText.Literal(entry.SpawnType)}, {spawner})";
        }

        List<string> arguments = [CodeText.Literal(entry.SpawnType), spawner];
        if (applies)
        {
            arguments.Add(Applier(model, references: false));
        }

        if (links)
        {
            arguments.Add("Link: " + Applier(model, references: true));
        }

        return "new global::Capsule.Scenes.Spawning.EntityRegistration(\n"
            + string.Join(",\n", arguments.Select(static argument => ArgumentIndent + argument))
            + ")";
    }

    // Sets each required member, and each optional one the entry authors. The base constructor calls the
    // applier of every other member before the derived body runs. The scene calls the applier of references
    // once every entry of the document is constructed.
    private static string Applier(EntityModel model, bool references)
    {
        StringBuilder applier = new StringBuilder("static (placed, properties) =>\n")
            .Append(ArgumentIndent).Append("{\n")
            .Append(StatementIndent).Append($"{model.QualifiedName} entity = ({model.QualifiedName})placed;\n");
        foreach (PropertyModel property in model.Authored.Where(property => (property.Kind == PropertyKind.Reference) == references))
        {
            if (property.Required)
            {
                applier.Append(StatementIndent).Append(Assignment(property, StatementIndent)).Append('\n');
                continue;
            }

            const string Nested = StatementIndent + "    ";
            applier.Append(StatementIndent).Append($"if (properties.Has({CodeText.Literal(property.Key)}))\n")
                .Append(StatementIndent).Append("{\n")
                .Append(Nested).Append(Assignment(property, Nested)).Append('\n')
                .Append(StatementIndent).Append("}\n");
        }

        return applier.Append(ArgumentIndent).Append('}').ToString();
    }

    // Plain C# where the game can assign the member, and an accessor where it cannot.
    private static string Assignment(PropertyModel property, string indent)
    {
        string value = PropertyReadRenderer.Read(property, indent);
        if (property.Direct)
        {
            return $"entity.{CodeText.Identifier(property.Name)} = {value};";
        }

        string accessor = EntityAccessorRenderer.SetterReference(property);

        return property.Field ? $"{accessor}(entity) = {value};" : $"{accessor}(entity, {value});";
    }

    // The switch one asset type's reads resolve a key through, to the member the build declared it on.
    private static string Lookup(AssetLookup lookup) => $$"""

                private static {{lookup.Form.Type}}? {{lookup.Form.Lookup}}(string key) => key switch
                {
        {{string.Concat(lookup.Assets.Items.Select(static asset => $"            {CodeText.Literal(asset.Key)} => {asset.Member},\n"))}}            _ => null,
                };

        """;
}
