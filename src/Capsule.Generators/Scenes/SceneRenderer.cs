using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

// Writes CapsuleScenes.g.cs from a resolved plan. Scenes/CapsuleScenes.sample.g.cs shows the file.
internal static class SceneRenderer
{
    private const string FileName = "CapsuleScenes.g.cs";

    private const string UnsafeAccessor = "global::System.Runtime.CompilerServices.UnsafeAccessor";

    // An applier's statements sit one level inside its method.
    private const string StatementIndent = "            ";

    internal static void Emit(SourceProductionContext context, ScenePlan plan)
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

    internal static string Render(ScenePlan plan)
    {
        List<string> claims = [.. plan.Registered.Items
            .Where(static entry => entry.DocumentName is not null)
            .Select(static entry => GeneratedFile.ClaimAttribute(RegistryClaimKind.SceneDocument, entry.DocumentName!, entry.Model.QualifiedName))];
        string generatedScenes = string.Concat(plan.DocumentOnly.Items
            .Where(static document => document.Base is not null)
            .Select(static document => GeneratedScene(document.Base!.Value)));
        IEnumerable<string> registrations = plan.Registered.Items.Select(Registration)
            .Concat(plan.DocumentOnly.Items.Select(Registration));
        List<SceneModel> composing = [.. plan.Composing];
        string members = string.Concat(plan.Registered.Items
                .Where(static entry => entry.DocumentName is not null && entry.Model.Required)
                .Select(static entry => Constructor(entry.Model)))
            + string.Concat(composing.Select(Applier))
            + EntityAccessorRenderer.Setters(composing.SelectMany(static model => model.Authored).Where(static property => !property.Direct))
            + string.Concat(plan.Lookups.Items.Select(EntityRenderer.Lookup));

        return GeneratedFile.Write(claims, $$"""
            {{generatedScenes}}    /// <summary>Every scene this assembly declares. Generated code. Do not edit.</summary>
                {{GeneratedFile.ExcludeFromCodeCoverage}}
                public static class CapsuleScenes
                {
            {{GeneratedFile.Registrations("global::Capsule.Scenes.SceneRegistration", registrations)}}

                    /// <summary>The registry the engine composes every scene through.</summary>
                    public static global::Capsule.Scenes.SceneRegistry Registry { get; } =
                        new global::Capsule.Scenes.SceneRegistry(
                            global::Capsule.Generated.CapsuleEntities.Registry,
                            Registrations);
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

        string content = Content(entry.Camera, entry.Model);
        string construct = entry.Model.Required
            ? $"{EntityAccessorRenderer.ConstructorName(entry.Model.QualifiedName)}({content})"
            : $"new {entry.Model.QualifiedName}({content})";

        return $"global::Capsule.Scenes.SceneRegistration.FromDocument(typeof({entry.Model.QualifiedName}), {CodeText.Literal(document)}, static content => {construct})";
    }

    private static string Registration(DocumentOnlyScene document)
    {
        string sceneType = document.Base is { } generated
            ? "global::Capsule.Generated." + generated.ClassName
            : "global::Capsule.Scenes.Scene";

        return $"global::Capsule.Scenes.SceneRegistration.DocumentOnly({CodeText.Literal(document.DocumentName)}, "
            + $"static content => new {sceneType}({Content(document.Camera, document.Base?.Base)}))";
    }

    // The content a registration's factory passes on: the document alone, or the document with its camera's factory
    // and its class's applier folded in ahead of the constructor that runs them.
    private static string Content(string? camera, SceneModel? composing)
    {
        List<string> folded = [];
        if (camera is not null)
        {
            folded.Add($"Camera = static () => new {camera}()");
        }

        if (composing is { } model && model.Authored.Any())
        {
            folded.Add("Apply = " + ApplierName(model));
        }

        return folded.Count == 0 ? "content!.Value" : $"content!.Value with {{ {string.Join(", ", folded)} }}";
    }

    // C#'s required members are checked at a new expression. The document sets a class's required references
    // inside its constructor, so its factory calls the constructor through an accessor the check does not reach.
    private static string Constructor(SceneModel model) => $$"""

                [{{UnsafeAccessor}}({{UnsafeAccessor}}Kind.Constructor)]
                private static extern {{model.QualifiedName}} {{EntityAccessorRenderer.ConstructorName(model.QualifiedName)}}({{model.ContentModifier}}global::Capsule.Scenes.SceneContent content);

        """;

    // Sets each required member, and each optional one the document authors. The base constructor calls it once
    // every entry is constructed, before the derived body runs.
    private static string Applier(SceneModel model) => $$"""

                private static void {{ApplierName(model)}}(global::Capsule.Scenes.Scene composed, global::Capsule.Scenes.Spawning.AuthoredProperties properties)
                {
                    {{model.QualifiedName}} scene = ({{model.QualifiedName}})composed;
        {{PropertyReadRenderer.Assignments(model.Authored, "scene", StatementIndent)}}        }

        """;

    // Apply and the class's qualified name with every other character an underscore: Game.Level is Apply_Game_Level.
    private static string ApplierName(SceneModel model) => "Apply_" + CodeText.Underscored(model.QualifiedName.Substring("global::".Length));
}
