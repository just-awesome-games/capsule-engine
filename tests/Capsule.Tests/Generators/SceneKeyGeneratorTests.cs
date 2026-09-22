using System.Collections.Immutable;
using System.Reflection;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

public sealed class SceneKeyGeneratorTests
{
    // A key a class claims and a key only a shipped document carries are both named in code, the
    // nested one through the class its directory generates. The game writes no using for either
    // generated class.
    [Fact]
    public void EveryRegisteredDocument_HasAKeyConstantUnderCapsuleAssetsScenes()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(
            $$"""
            {{GeneratorHarness.Preamble}}

            public sealed class Room(SceneContent content) : Scene(content);

            public static class Probe
            {
                public const string Nested = CapsuleAssets.Scenes.Dev.RoomWide;

                public const string Root = CapsuleAssets.Scenes.Room;

                public static SceneRegistry Registry => CapsuleScenes.Registry;
            }
            """,
            logic: true,
            ("scenes/dev/room-wide.scene.json", null));

        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        Type probe = GeneratorHarness.Loaded(compiled).GetType("Game.Probe")!;
        Assert.Equal("dev/room-wide", Constant(probe, "Nested"));
        Assert.Equal("room", Constant(probe, "Root"));
    }

    // A key that cannot be a member where it lands is refused by the generator, never left to fail as
    // a C# error in generated code.
    [Theory]
    [InlineData("CAP016", "scenes/halls.scene.json", "scenes/halls/hall.scene.json")]
    [InlineData("CAP018", "scenes/scenes.scene.json")]
    public void ADocumentKeyNoMemberCanTake_FailsTheBuildNamingTheDocument(string diagnosticId, params string[] documents)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileWithSources(
            logic: true,
            [.. documents.Select(static path => (path, (string?)null))]);

        Diagnostic refused = Assert.Single(GeneratorHarness.Errors(diagnostics));
        Assert.Equal(diagnosticId, refused.Id);
        Assert.Contains(documents[0], refused.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Empty(GeneratorHarness.Errors(compiled.GetDiagnostics()));
    }

    // Keys at different tree paths pass the key tree. Their generated base-scene classes must not then
    // collide, as 'a/b' and 'a-b' did when segment identifiers were joined with nothing between them.
    [Fact]
    public void TwoDocumentsWhoseKeysDifferOnlyInSeparators_EachComposeTheirBaseScene()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(
            $$"""
            {{GeneratorHarness.Preamble}}

            public abstract class PlayableRoom(SceneContent content) : Scene(content);
            """,
            logic: true,
            ("scenes/a/b.scene.json", """{"formatVersion": 6, "baseScene": "playable-room", "entities": [], "nextEntityId": 1}"""),
            ("scenes/a-b.scene.json", """{"formatVersion": 6, "baseScene": "playable-room", "entities": [], "nextEntityId": 1}"""));

        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        Assembly assembly = GeneratorHarness.Loaded(compiled);
        SceneRegistry registry = (SceneRegistry)assembly.GetType("Capsule.Generated.CapsuleScenes")!
            .GetProperty("Registry")!.GetValue(null)!;
        Type playableRoom = assembly.GetType("Game.PlayableRoom")!;

        Scene nested = registry.CreateFromDocument("a/b", new SceneDocument([], 1));
        Scene joined = registry.CreateFromDocument("a-b", new SceneDocument([], 1));

        Assert.True(playableRoom.IsInstanceOfType(nested));
        Assert.True(playableRoom.IsInstanceOfType(joined));
        Assert.NotEqual(nested.GetType(), joined.GetType());
    }

    private static object? Constant(Type holder, string name) =>
        holder.GetField(name, BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue();
}
