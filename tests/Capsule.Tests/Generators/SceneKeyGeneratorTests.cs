using System.Collections.Immutable;
using System.Reflection;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

public sealed class SceneKeyGeneratorTests
{
    // Keys at different tree paths pass the build's key tree. Their generated base-scene classes must
    // not then collide, as 'a/b' and 'a-b' did when segment identifiers were joined with nothing
    // between them.
    [Fact]
    public void TwoDocumentsWhoseKeysDifferOnlyInSeparators_EachComposeTheirBaseScene()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(
            $$"""
            {{GeneratorHarness.Preamble}}

            public abstract class PlayableRoom(SceneContent content) : Scene(content);
            """,
            logic: true,
            ("scenes/a/b.scene.json", """{"baseScene": "playable-room", "entities": []}"""),
            ("scenes/a-b.scene.json", """{"baseScene": "playable-room", "entities": []}"""));

        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        Assembly assembly = GeneratorHarness.Loaded(compiled);
        SceneRegistry registry = (SceneRegistry)assembly.GetType("Capsule.Generated.CapsuleScenes")!
            .GetProperty("Registry")!.GetValue(null)!;
        Type playableRoom = assembly.GetType("Game.PlayableRoom")!;

        Scene nested = registry.Create(new SceneKey("scenes/a/b"), new SceneDocument([]));
        Scene joined = registry.Create(new SceneKey("scenes/a-b"), new SceneDocument([]));

        Assert.True(playableRoom.IsInstanceOfType(nested));
        Assert.True(playableRoom.IsInstanceOfType(joined));
        Assert.NotEqual(nested.GetType(), joined.GetType());
    }
}
