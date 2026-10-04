using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Writes CapsuleScenes.g.cs from a resolved plan. Scenes/CapsuleScenes.sample.g.cs shows the file.
internal static class SceneRenderer
{
    private const string FileName = "CapsuleScenes.g.cs";

    // An applier's statements sit one level inside its method.
    private const string StatementIndent = "            ";

    private const string AppliersType = "global::System.Collections.Generic.KeyValuePair<global::System.Type, global::Capsule.Scenes.SceneApplier>";

    /// <param name="providerName">The assembly's registry provider, which <c>Registry</c> is built through.</param>
    internal static void Emit(SourceProductionContext context, (ScenePlan Plan, string? ProviderName) input)
    {
        (ScenePlan plan, string? providerName) = input;
        GeneratedFile.Report(context, plan.Diagnostics);

        if (plan.Generates)
        {
            GeneratedFile.Add(context, FileName, Render(plan, providerName!));
        }
    }

    internal static string Render(ScenePlan plan, string providerName)
    {
        List<string> claims = [.. plan.Registered.Items
            .Where(static entry => entry.DocumentName is not null)
            .Select(static entry => GeneratedFile.ClaimAttribute(RegistryClaimKind.SceneDocument, entry.DocumentName!, entry.Model.QualifiedName))];
        string generatedScenes = string.Concat(plan.DocumentOnly.Items
            .Where(static document => document.Base is not null)
            .Select(static document => GeneratedScene(document.Base!.Value)));
        IEnumerable<string> registrations = plan.Registered.Items.Select(Registration)
            .Concat(plan.DocumentOnly.Items.Select(Registration));
        List<SceneModel> composing = [.. plan.Applied.Items];
        List<PropertyModel> authored = [.. composing.SelectMany(static model => model.Authored)];
        string members = string.Concat(plan.Registered.Items
                .Where(static entry => entry.DocumentName is not null && entry.Model.Required)
                .Select(static entry => EntityAccessorRenderer.Constructor(
                    entry.Model.QualifiedName, entry.Model.ContentModifier + "global::Capsule.Scenes.SceneContent content")))
            + string.Concat(composing.Select(Applier))
            + ObjectRenderer.Methods(plan.Objects, authored)
            + EntityAccessorRenderer.Accessors(authored.Concat(ObjectRenderer.Authored(plan.Objects)))
            + string.Concat(plan.Lookups.Items.Select(EntityRenderer.Lookup));
        string appliers = string.Concat(composing.Select(static model => $"                new(typeof({model.QualifiedName}), {ApplierName(model.QualifiedName)}),\n"));
        string createRegistry = RegistryProviderRenderer.SceneRegistryBody(["global::Capsule.Generated." + providerName], StatementIndent);

        return GeneratedFile.Write(claims, $$"""
            {{generatedScenes}}    /// <summary>Every scene this assembly declares. Generated code. Do not edit.</summary>
                {{GeneratedFile.ExcludeFromCodeCoverage}}
                public static class CapsuleScenes
                {
            {{GeneratedFile.Registrations("global::Capsule.Scenes.SceneRegistration", registrations)}}

                    // The applier of each scene class authoring members, by class, which SceneRegistry.Content finds along a class's bases.
                    internal static {{AppliersType}}[] Appliers { get; } =
                        new {{AppliersType}}[]
                        {
            {{appliers}}            };

                    /// <summary>The registry the engine composes every scene through.</summary>
                    public static global::Capsule.Scenes.SceneRegistry Registry { get; } = CreateRegistry();

                    // Built through the provider the shell's CapsuleBoot reads. A test composing through Registry composes what a run does.
                    private static global::Capsule.Scenes.SceneRegistry CreateRegistry()
                    {
            {{createRegistry}}        }
            {{members}}    }
            """);
    }

    // The template a developer no longer writes: the document composes this instead of the Room01.cs a
    // class-only scene would need. The document sets the base's required references before the constructor returns.
    private static string GeneratedScene(GeneratedBase generated) => $$"""
            {{GeneratedFile.ExcludeFromCodeCoverage}}
            internal sealed class {{generated.ClassName}} : {{generated.Base.QualifiedName}}
            {
                {{(generated.Base.Required ? "[global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]\n        " : string.Empty)}}internal {{generated.ClassName}}(global::Capsule.Scenes.SceneContent content) : base(content)
                {
                }
            }


        """;

    private static string Registration(RegisteredScene entry)
    {
        if (entry.DocumentName is not { } document)
        {
            return $"global::Capsule.Scenes.SceneRegistration.Plain(typeof({entry.Model.QualifiedName}), static _ => new {entry.Model.QualifiedName}())";
        }

        string content = Content(entry.Model.Composing);
        string construct = entry.Model.Required
            ? $"{EntityAccessorRenderer.ConstructorName(entry.Model.QualifiedName)}({content})"
            : $"new {entry.Model.QualifiedName}({content})";

        return $"global::Capsule.Scenes.SceneRegistration.FromDocument(typeof({entry.Model.QualifiedName}), {CodeText.Literal(document)}, static content => {construct})";
    }

    private static string Registration(DocumentOnlyScene document)
    {
        string sceneType = document.Base is { } generated
            ? "global::Capsule.Generated." + generated.ClassName
            : SceneModel.EngineScene;
        string composing = document.Base?.Base.Composing ?? SceneModel.EngineScene;

        return $"global::Capsule.Scenes.SceneRegistration.DocumentOnly({CodeText.Literal(document.DocumentName)}, "
            + $"static content => new {sceneType}({Content(composing)}))";
    }

    // The content a registration's factory passes on, with the composing class's applier folded in ahead of the
    // constructor that runs it. The document's tile maps read this assembly's assets and tile types.
    private static string Content(string composing) =>
        $"content!.Value with {{ Apply = {ApplierName(composing)}, Entities = content!.Value.Entities.OwnedBy(global::Capsule.Generated.CapsuleEntities.Registrations) }}";

    // Sets each required member, and each optional one the document authors. The base constructor calls it once
    // every entry is constructed, before the derived body runs.
    private static string Applier(SceneModel model) => $$"""

                private static void {{ApplierName(model.QualifiedName)}}(global::Capsule.Scenes.Scene composed, global::Capsule.Scenes.Spawning.AuthoredProperties properties)
                {
                    {{model.QualifiedName}} scene = ({{model.QualifiedName}})composed;
        {{PropertyReadRenderer.Assignments(model.Authored, "scene", model.QualifiedName, StatementIndent)}}        }

        """;

    // Game.Level's applier is Apply_Game_Level.
    private static string ApplierName(string qualifiedName) => "Apply_" + CodeText.TypeIdentifier(qualifiedName);
}
