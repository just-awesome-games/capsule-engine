using Capsule.Runtime;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Entities;
using Capsule.Scenes.Spawning;
using Capsule.Tests.Documents;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Runtime;

// Reaches for the shell content directory a scene document ships into, which is one place for the
// whole process; the collection keeps it away from everything else that writes there.
[Collection(SceneWorkspaceCollection.Name)]
public sealed class SceneComposerTests : IDisposable
{
    private const string DocumentName = "hall";

    private static readonly string DocumentPath =
        Path.Combine(AppContext.BaseDirectory, "assets", "scenes", DocumentName + ".scene.json");

    // A game boots a document-backed scene by its class; nothing in game code names the document.
    // Turning the one into the other is this layer's job, and a game that lost it would boot into
    // an empty room rather than fail.
    [Fact]
    public void ADocumentBackedClass_BootedByItsClass_IsComposedFromTheDocumentItClaims()
    {
        Write(SceneFixtures.Room(new EntityPlacement(1, "chest", 48f, 16f)));
        SceneComposer composer = new(Registry());

        Scene composed = composer.Resolve(SceneTarget.ForScene(typeof(Hall)));

        Assert.IsType<Hall>(composed);
        Assert.IsType<TileMap>(composed.Entities[0]);
        Assert.IsType<SceneFixtures.Placed>(composed.Entities[1]);
    }

    // The scene layer is pure and knows no paths, so without this the commonest authoring mistake
    // names a spawn type and leaves the author to guess which document holds it.
    [Fact]
    public void APlacementNoEntityClaims_NamesTheDocumentFileThatHoldsIt()
    {
        Write(SceneFixtures.Room(new EntityPlacement(1, "wyvern", 0f, 0f)));
        SceneComposer composer = new(Registry());

        SpawnException failure = Assert.Throws<SpawnException>(
            () => composer.Resolve(SceneTarget.ForName(DocumentName)));

        Assert.Contains(DocumentName + ".scene.json", failure.Message, StringComparison.Ordinal);
        Assert.Contains("wyvern", failure.Message, StringComparison.Ordinal);
    }

    public void Dispose() => File.Delete(DocumentPath);

    private static void Write(SceneDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DocumentPath)!);
        SceneDocumentFile.Save(document, DocumentPath);
    }

    private static SceneRegistry Registry() =>
        new(
            SceneFixtures.Registry(("chest", static spawn => new SceneFixtures.Placed(spawn))),
            [SceneRegistration.FromDocument(typeof(Hall), DocumentName, static content => new Hall(content))]);

    private sealed class Hall(SceneContent content) : Scene(content);
}
