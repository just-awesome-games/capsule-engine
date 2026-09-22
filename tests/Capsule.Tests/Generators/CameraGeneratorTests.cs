using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

public sealed class CameraGeneratorTests
{
    // An internal camera class is still constructible from a document's camera key, because the
    // generated factory that constructs it sits in the same assembly.
    [Fact]
    public void ADocumentsCamera_BakesAnInternalConcreteCamerasFactoryIntoTheRegistration()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(
            $$"""
            {{GeneratorHarness.Preamble}}

            internal sealed class GameCamera : Camera;
            """,
            logic: true,
            ("scenes/halls/hall.scene.json", """{"formatVersion": 6, "camera": "game-camera", "entities": [], "nextEntityId": 1}"""));

        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsuleScenesFile);
        Assert.Contains("Camera = static () => new global::Game.GameCamera()", generated, StringComparison.Ordinal);
    }

    // A key that resolves to a class failing the shape a camera needs is a defect naming the class,
    // not a silently unclaimed key. A protected constructor fails it too, because the generated
    // registration does not derive from the camera and could not call one.
    [Theory]
    [InlineData("public abstract class GameCamera : Camera;")]
    [InlineData("public sealed class GameCamera : Camera { protected GameCamera() { } }")]
    public void ACameraGeneratedCodeCannotConstruct_FailsTheBuildNamingTheClass(string declaration)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(
            $$"""
            {{GeneratorHarness.Preamble}}

            {{declaration}}
            """,
            logic: true,
            ("scenes/halls/hall.scene.json", """{"formatVersion": 6, "camera": "game-camera", "entities": [], "nextEntityId": 1}"""));

        Diagnostic error = Assert.Single(GeneratorHarness.Errors(diagnostics));
        Assert.Equal("CAP029", error.Id);
        Assert.Contains("Game.GameCamera", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Empty(GeneratorHarness.Errors(compiled.GetDiagnostics()));
    }

    // A key no class claims at all is CAP030, the same contract SceneGeneratorTests covers for a
    // baseScene's own unclaimed key: one test of "no class claims this key" is enough for both.

    // Two cameras resolving to one key used to pick the first by declaration order in silence, the
    // exact failure species this feature was built to remove: a room framed by the wrong camera.
    [Fact]
    public void TwoCamerasClaimingOneKey_FailTheBuildNamingBoth()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.Compile("""
            using Capsule.Scenes;

            namespace Game
            {
                public sealed class GameCamera : Camera;
            }

            namespace Game.Deep
            {
                public sealed class GameCamera : Camera;
            }
            """).Diagnostics;

        Diagnostic collision = Assert.Single(GeneratorHarness.Errors(diagnostics));
        Assert.Equal("CAP031", collision.Id);

        string message = collision.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("Game.GameCamera", message, StringComparison.Ordinal);
        Assert.Contains("Game.Deep.GameCamera", message, StringComparison.Ordinal);
        Assert.Contains("'game-camera'", message, StringComparison.Ordinal);
    }
}
