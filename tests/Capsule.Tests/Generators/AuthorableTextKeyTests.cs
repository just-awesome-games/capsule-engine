using System.Collections.Immutable;
using System.Reflection;
using Capsule.Localization;
using Capsule.Scenes;
using Capsule.Tests.Build;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

// A TextKey member is authored as a text's key path. The load finds the path in the build's CapsuleStrings
// table, or refuses one the game does not ship.
[Collection(SceneWorkspaceCollection.Name)]
public sealed class AuthorableTextKeyTests
{
    private const string Game = """
        namespace Game
        {
            public sealed class Sign(global::Capsule.Scenes.EntitySpawn spawn) : global::Capsule.Scenes.Entity(spawn)
            {
                [global::Capsule.Scenes.Authorable]
                public global::Capsule.Localization.TextKey Caption { get; set; }
            }
        }
        """;

    [Fact]
    public void ATextKeyMember_ComposesFromItsKeyPath()
    {
        Entity sign = Composed("hints.jump").Entities.ToArray()[0];

        TextKey caption = (TextKey)sign.GetType().GetProperty("Caption")!.GetValue(sign)!;
        Assert.Equal("hints.jump", caption.ToString());
    }

    [Fact]
    public void AKeyPathTheGameDoesNotShip_FailsTheLoad() =>
        GeneratorHarness.AssertLoadRefused(
            () => Composed("hints.fly"),
            "scene document 'scenes/room': entities[0] ('sign') ",
            "sets 'caption' to the string \"hints.fly\", but no TextKey keys as \"hints.fly\".");

    // Composes the room through the registry generated against the build's CapsuleStrings.
    private static Scene Composed(string caption)
    {
        string strings;
        using (ToolWorkspace workspace = new())
        {
            workspace.Write("Assets/en.strings.json", """{ "hints": { "jump": "Jump" } }""");
            workspace.Succeed();
            strings = File.ReadAllText(Path.Combine(ToolWorkspace.Out, "CapsuleStrings.g.cs"));
        }

        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(strings + Game, logic: true);
        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        SceneRegistry registry = (SceneRegistry)GeneratorHarness.Loaded(compiled)
            .GetType("Capsule.Generated.CapsuleScenes")!.GetProperty("Registry")!.GetValue(null)!;

        return registry.Create(
            new SceneKey("scenes/room"),
            SceneDocument.Parse($$"""{"entities": [{"type": "sign", "x": 0, "y": 0, "caption": "{{caption}}"}]}"""));
    }
}
