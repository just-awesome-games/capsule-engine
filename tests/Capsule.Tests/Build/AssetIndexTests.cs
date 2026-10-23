using System.Collections.Immutable;
using System.Reflection;
using Capsule.Assets;
using Capsule.Audio;
using Capsule.Scenes;
using Capsule.Tests.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Capsule.Tests.Build.AudioProbeFixtures;

namespace Capsule.Tests.Build;

/// <summary>
/// What a scene document's asset keys resolve through: the index the build writes beside
/// <c>CapsuleAssets</c>, which finds each texture, sound and scene document as its member declares it.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class AssetIndexTests
{
    private static readonly ImmutableArray<MetadataReference> References =
        GeneratorHarness.Referenced(null, typeof(TextureHandle).Assembly, typeof(SceneKey).Assembly, typeof(object).Assembly);

    // Every member's value is found by the key a document names it by, a sound's measured duration and loop
    // region included. A key that only prefixes one, sorts between two, names another type's asset or runs
    // past the stack finds none.
    [Fact]
    public void EveryDeclaredAsset_IsFoundAsItsMemberDeclaresIt()
    {
        using ToolWorkspace workspace = new();
        workspace.WritePng("Assets/Textures/b.png", 2, 2);
        workspace.WritePng("Assets/Textures/a/c.png", 2, 2);
        workspace.WritePng("Assets/Textures/a_c.png", 2, 2);
        workspace.Write("Assets/Audio/step.wav", Wav(1, 16, 22050, 1764));
        workspace.Write("Assets/Audio/theme.wav", Wav(1, 16, 22050, 2205, loop: (441, 1764)));
        workspace.Write("Assets/Scenes/hall.scene.json", """{"entities": []}""");
        workspace.Write("Assets/Scenes/halls/east.scene.json", """{"entities": []}""");

        workspace.Succeed();

        Assembly game = Loaded(workspace.Generated);
        Type index = game.GetType("Capsule.Generated.CapsuleAssetIndex")!;
        object[] declared = [.. Members(game.GetType("Capsule.Generated.CapsuleAssets")!)];
        TextureHandle[] textures = [.. declared.OfType<TextureHandle>()];
        AudioClip[] sounds = [.. declared.OfType<AudioClip>()];
        SceneKey[] scenes = [.. declared.OfType<SceneKey>()];

        Assert.Equal(3, textures.Length);
        Assert.Equal(2, sounds.Length);
        Assert.Contains(sounds, static sound => sound.LoopRegion.HasRegion);
        Assert.Equal(2, scenes.Length);
        Assert.All(textures, texture => Assert.Equal(texture, Find(index, "FindTexture", texture.Name + texture.Extension)));
        Assert.All(sounds, sound => Assert.Equal(sound, Find(index, "FindSound", sound.Name + sound.Extension)));
        Assert.All(scenes, scene => Assert.Equal(scene, Find(index, "FindScene", scene.Name)));
        Assert.All(
            ["textures/a", "textures/a.png", "textures/a_b.png", "textures/c.png", "audio/step.wav", "", "textures/" + new string('a', 300) + ".png"],
            path => Assert.Null(Find(index, "FindTexture", path)));
        Assert.Null(Find(index, "FindSound", "textures/b.png"));
        Assert.Null(Find(index, "FindScene", "scenes/halls"));
    }

    // Every static property value of a class and the classes nested in it.
    private static IEnumerable<object> Members(Type declaring) =>
        declaring.GetProperties(BindingFlags.Public | BindingFlags.Static).Select(static member => member.GetValue(null)!)
            .Concat(declaring.GetNestedTypes().SelectMany(Members));

    private static object? Find(Type index, string method, string key) =>
        index.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [key]);

    // The generated index as the game runs it, beside the members it indexes.
    private static Assembly Loaded(string generated) =>
        GeneratorHarness.Loaded(CSharpCompilation.Create(
            "IndexSpecs",
            [CSharpSyntaxTree.ParseText(generated)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));
}
