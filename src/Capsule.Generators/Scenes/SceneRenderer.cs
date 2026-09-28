using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

// Writes CapsuleScenes.g.cs from a resolved plan. Scenes/CapsuleScenes.sample.g.cs shows the file.
internal static class SceneRenderer
{
    private const string FileName = "CapsuleScenes.g.cs";

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
                }
            """);
    }

    // The template a developer no longer writes: the document composes this instead of the Room01.cs a
    // class-only scene would need.
    private static string GeneratedScene(GeneratedBase generated) => $$"""
            {{GeneratedFile.ExcludeFromCodeCoverage}}
            internal sealed class {{generated.ClassName}} : {{generated.QualifiedBase}}
            {
                internal {{generated.ClassName}}(global::Capsule.Scenes.SceneContent content) : base(content)
                {
                }
            }


        """;

    private static string Registration(RegisteredScene entry) => entry.DocumentName is { } document
        ? $"global::Capsule.Scenes.SceneRegistration.FromDocument(typeof({entry.Model.QualifiedName}), {CodeText.Literal(document)}, "
            + $"static content => new {entry.Model.QualifiedName}({Content(entry.Camera)}))"
        : $"global::Capsule.Scenes.SceneRegistration.Plain(typeof({entry.Model.QualifiedName}), static _ => new {entry.Model.QualifiedName}())";

    private static string Registration(DocumentOnlyScene document)
    {
        string sceneType = document.Base is { } generated
            ? "global::Capsule.Generated." + generated.ClassName
            : "global::Capsule.Scenes.Scene";

        return $"global::Capsule.Scenes.SceneRegistration.DocumentOnly({CodeText.Literal(document.DocumentName)}, "
            + $"static content => new {sceneType}({Content(document.Camera)}))";
    }

    // The content a registration's factory passes on: the document alone, or the document with its
    // camera's factory folded in ahead of the constructor that installs it.
    private static string Content(string? camera) => camera is null
        ? "content!.Value"
        : $"content!.Value with {{ Camera = static () => new {camera}() }}";
}
