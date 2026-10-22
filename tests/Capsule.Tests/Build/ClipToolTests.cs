using System.Collections.Immutable;
using System.Reflection;
using Capsule.Animation;
using Capsule.Tests.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Capsule.Tests.Build;

/// <summary>
/// What the build will and will not read out of a value clip, and what a game compiles against once it
/// has: every key is literal data by the time the game runs, and no clip ships.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class ClipToolTests
{
    private static readonly ImmutableArray<MetadataReference> References =
        GeneratorHarness.Referenced(null, typeof(ValueClip<>).Assembly, typeof(object).Assembly);

    // A stepping clip by default, and a linear one only where the file asks. Both compile into one
    // shared clip under the folders the file sits in, named for the file.
    [Theory]
    [InlineData("""{ "formatVersion": 1, "keys": [ { "value": 2, "ticks": 8 }, { "value": 3.5, "ticks": 1 } ] }""", false, false)]
    [InlineData("""{ "formatVersion": 1, "loop": true, "blend": "linear", "keys": [ { "value": 2, "ticks": 8 }, { "value": 3.5, "ticks": 1 } ] }""", true, true)]
    public void AClip_CompilesIntoOneValueClipOfItsKeysUnderItsFolders(string json, bool loop, bool linear)
    {
        using ToolWorkspace workspace = Authored(json);
        workspace.Succeed();

        Type declaring = Loaded(workspace.Generated).GetType("Capsule.Generated.CapsuleAssets")!.GetNestedType("Clips")!.GetNestedType("Glow")!;
        PropertyInfo member = declaring.GetProperty("PulseClip")!;
        ValueClip<float> clip = (ValueClip<float>)member.GetValue(null)!;

        Assert.Same(clip, member.GetValue(null));
        Assert.Equal([2f, 3.5f], clip.Values.ToArray());
        Assert.Equal([8, 1], clip.KeyTicks.ToArray());
        Assert.Equal(loop, clip.Loop);
        Assert.Equal(linear, clip.IsLinear);
        Assert.Empty(workspace.Shipped);
    }

    [Theory]
    [InlineData("""{ "formatVersion": 1 }""", "has no keys. A clip plays at least one. Add a key to \"keys\"")]
    [InlineData("""{ "formatVersion": 1, "keys": [] }""", "has no keys.")]
    [InlineData("""{ "formatVersion": 1, "keys": [ { "value": 1, "ticks": 2 }, { "value": 2, "ticks": 0 } ] }""", "has keys[1] lasting 0 ticks. Every key lasts at least 1 fixed step. Set its \"ticks\" to 1 or more.")]
    [InlineData("""{ "formatVersion": 1, "keys": [ { "value": 1 } ] }""", "has keys[0] with no ticks.")]
    [InlineData("""{ "formatVersion": 1, "keys": [ { "ticks": 1 } ] }""", "has keys[0] with no value.")]
    [InlineData("""{ "formatVersion": 1, "keys": [ { "value": 1e39, "ticks": 1 } ] }""", "has keys[0] with a value past the range of a float.")]
    [InlineData("""{ "formatVersion": 1, "blend": "cubic", "keys": [ { "value": 1, "ticks": 1 } ] }""", "has an unknown member or value at $.blend (line 1, byte 39). A clip holds \"formatVersion\" and \"keys\", and may hold \"loop\" (true or false) and \"blend\" (\"step\" or \"linear\").")]
    [InlineData("""{ "formatVersion": 1, "keys": [ { "value": [1, 0], "ticks": 1 } ] }""", "has an unknown member or value at $.keys[0].value")]
    [InlineData("""{ "keys": [ { "value": 1, "ticks": 1 } ] }""", "has no formatVersion.")]
    public void AClipTheBuildCannotRead_FailsTheBuildNamingTheDefectAndTheFix(string json, string defect)
    {
        using ToolWorkspace workspace = Authored(json);
        string refused = workspace.Fail();

        Assert.Contains("Assets/Clips/Glow/pulse.clip.json", refused, StringComparison.Ordinal);
        Assert.Contains(defect, refused, StringComparison.Ordinal);
    }

    private static ToolWorkspace Authored(string json)
    {
        ToolWorkspace workspace = new();
        workspace.Write("Assets/Clips/Glow/pulse.clip.json", json);

        return workspace;
    }

    // The generated registry as the game runs it. A member hands back what it declares.
    private static Assembly Loaded(string generated) =>
        GeneratorHarness.Loaded(CSharpCompilation.Create(
            "ClipSpecs",
            [CSharpSyntaxTree.ParseText(generated)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));
}
